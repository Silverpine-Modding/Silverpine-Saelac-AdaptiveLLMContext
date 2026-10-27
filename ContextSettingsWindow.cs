#nullable enable

using Silverpine.ModdingTools;
using System.Globalization;
using UnityEngine;

namespace SilverpineMods.AdaptiveLLMContext;

internal sealed class ContextSettingsWindow : ModToolBehaviour
{
    private const float DesignWidth = 1920f;
    private const float DesignHeight = 1080f;

    private static ContextSettingsWindow? instance;

    private string maximumContextText = "";
    private string status = "";
    private bool open;

    internal static void Open(ModToolSession session)
    {
        if (instance != null)
            instance.Close();

        GameObject root = new("Adaptive LLM Context IMGUI");
        instance = root.AddComponent<ContextSettingsWindow>();
        instance.AttachSession(session);
        instance.maximumContextText =
            ContextBudget.ConfiguredMaximumContextTokens.ToString(
                CultureInfo.InvariantCulture);
        instance.open = true;
    }

    private void Update()
    {
        if (open && Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    private void OnGUI()
    {
        if (!open)
            return;

        GUI.enabled = true;
        GUI.color = Color.white;
        GUI.backgroundColor = Color.white;
        GUI.depth = -1000;

        using ModGuiScope scope = ModGui.BeginScaled(
            DesignWidth,
            DesignHeight);
        Color oldColor = GUI.color;
        GUI.color = new Color(0.055f, 0.045f, 0.075f, 0.995f);
        GUI.DrawTexture(
            new Rect(0f, 0f, DesignWidth, DesignHeight),
            Texture2D.whiteTexture);
        GUI.color = oldColor;

        GUILayout.BeginArea(new Rect(390f, 90f, 1140f, 900f));
        DrawHeader();
        GUILayout.Space(14f);
        GUILayout.Label(
            "Choose the combined prompt-and-response context. Every other " +
            "budget is calculated from this value; Silverpine's normal " +
            "512-token response maximum is unchanged.",
            GUI.skin.box,
            GUILayout.MinHeight(72f));
        GUILayout.Label(
            ContextBudget.GetRuntimeModeDescription(),
            GUI.skin.box,
            GUILayout.MinHeight(58f));

        GUILayout.Space(18f);
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("Maximum context tokens");
        GUI.SetNextControlName("AdaptiveLLMContext.MaximumContextTokens");
        maximumContextText = GUILayout.TextField(
            maximumContextText,
            12,
            GUILayout.Height(54f));
        GUILayout.Label(
            $"Allowed: {ContextBudget.MinimumMaximumContextTokens:N0}–" +
            $"{ContextBudget.MaximumMaximumContextTokens:N0}; values are " +
            $"rounded to {ContextBudget.TokenQuantum}-token steps.");

        GUILayout.BeginHorizontal();
        DrawPresetButton("8K", 8192);
        DrawPresetButton("12K", 12288);
        DrawPresetButton("16K", 16384);
        DrawPresetButton("24K", 24576);
        DrawPresetButton("32K", 32768);
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        GUILayout.Space(14f);
        bool valid = ContextBudget.TryCreatePreview(
            maximumContextText,
            out ContextBudgetSnapshot preview,
            out string previewError);
        if (valid)
            DrawBudget(preview);
        else
            GUILayout.Label(previewError, GUI.skin.box, GUILayout.Height(54f));

        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal(GUI.skin.box);
        if (GUILayout.Button("Reset to 16K", GUILayout.Width(170f)))
        {
            maximumContextText =
                ContextBudget.DefaultMaximumContextTokens.ToString(
                    CultureInfo.InvariantCulture);
            status = "Default value loaded. Choose Apply to save it.";
        }
        GUILayout.FlexibleSpace();
        bool previousEnabled = GUI.enabled;
        GUI.enabled = valid;
        if (GUILayout.Button("Apply", GUILayout.Width(140f)))
            Apply(preview.MaximumContextTokens);
        GUI.enabled = previousEnabled;
        if (GUILayout.Button("Close", GUILayout.Width(120f)))
            Close();
        GUILayout.EndHorizontal();

        if (!string.IsNullOrWhiteSpace(status))
            GUILayout.Label(status, GUI.skin.box, GUILayout.MinHeight(54f));
        GUILayout.EndArea();
    }

    private static void DrawHeader()
    {
        GUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label("Adaptive LLM Context", GUILayout.Width(390f));
        GUILayout.FlexibleSpace();
        GUILayout.Label("Version " + Plugin.PluginVersion);
        GUILayout.EndHorizontal();
    }

    private void DrawPresetButton(string label, int value)
    {
        if (GUILayout.Button(label, GUILayout.Height(48f)))
        {
            maximumContextText = value.ToString(CultureInfo.InvariantCulture);
            status = "Preset loaded. Choose Apply to save it.";
        }
    }

    private static void DrawBudget(ContextBudgetSnapshot budget)
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("Derived context budget");
        DrawBudgetRow(
            "Maximum context",
            budget.MaximumContextTokens,
            "Combined prompt and possible response");
        DrawBudgetRow(
            "Response headroom",
            budget.ResponseReserveTokens,
            "Reserved before pruning begins");
        DrawBudgetRow(
            "Pruning trigger",
            budget.PruneTriggerTokens,
            "Compression begins above this prompt size");
        DrawBudgetRow(
            "Post-pruning prompt target",
            budget.RetainedHistoryTokens,
            "Includes dialogue, character context, and memories");
        DrawBudgetRow(
            "Nominal summary batch",
            budget.SummaryChunkTokens,
            "Old dialogue summarized per pruning pass");
        DrawBudgetRow(
            "Advanced-memory allowance",
            budget.AdvancedMemoryCharacterBudget,
            "Characters, matching Silverpine's existing formula");
        GUILayout.EndVertical();
    }

    private static void DrawBudgetRow(
        string label,
        int value,
        string explanation)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(310f));
        GUILayout.Label(value.ToString("N0"), GUILayout.Width(150f));
        GUILayout.Label(explanation);
        GUILayout.EndHorizontal();
    }

    private void Apply(int value)
    {
        if (ContextBudget.TrySetMaximumContext(value, out string message))
        {
            maximumContextText =
                ContextBudget.ConfiguredMaximumContextTokens.ToString(
                    CultureInfo.InvariantCulture);
        }

        status = message;
    }

    private void Close()
    {
        if (!open)
            return;

        open = false;
        ReleaseSession();
        Destroy(gameObject);
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(instance, this))
            instance = null;
        base.OnDestroy();
    }
}
