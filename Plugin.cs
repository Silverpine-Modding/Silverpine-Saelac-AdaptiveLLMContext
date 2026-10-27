#nullable enable

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Silverpine.ModdingTools;

namespace SilverpineMods.AdaptiveLLMContext;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(
    Silverpine.ModdingTools.Plugin.PluginGuid,
    "1.9.3")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid =
        "renegadex.silverpine.adaptivellmcontext";
    public const string PluginName = "Adaptive LLM Context";
    public const string PluginVersion = "1.0.0";

    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;
        ContextBudget.Initialize(Config, Logger);

        ModdingToolsMenu.RegisterSession(
            PluginGuid + ".settings.main-menu",
            PluginName,
            (_, session) => ContextSettingsWindow.Open(session),
            order: 345);
        InventoryModTools.RegisterSession(
            PluginGuid + ".settings.in-game",
            PluginName,
            (_, session) => ContextSettingsWindow.Open(session),
            order: 345);

        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, PluginGuid);
        ContextBudget.ApplyToGameDefinitions();

        ContextBudgetSnapshot budget = ContextBudget.Effective;
        Logger.LogInfo(
            $"Loaded with a {budget.MaximumContextTokens:N0}-token context, " +
            $"{budget.PruneTriggerTokens:N0}-token prune trigger, and " +
            $"{budget.RetainedHistoryTokens:N0}-token retention target.");
    }

    private void OnDestroy()
    {
        // Silverpine destroys this bootstrap host while the process and game
        // continue. Persistent patches, configuration state, and static
        // Modding Tools registrations must remain installed for process life.
        Logger.LogInfo(
            "Plugin host destroyed during Silverpine bootstrap; persistent " +
            "context patches and Modding Tools registrations remain installed.");
    }
}
