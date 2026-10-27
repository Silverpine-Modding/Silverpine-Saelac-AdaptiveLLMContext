#nullable enable

using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SilverpineMods.AdaptiveLLMContext;

internal readonly struct ContextBudgetSnapshot
{
    internal ContextBudgetSnapshot(int maximumContextTokens)
    {
        MaximumContextTokens = maximumContextTokens;
        ResponseReserveTokens = ContextBudget.Clamp(
            ContextBudget.RoundToQuantum(maximumContextTokens / 16),
            512,
            2048);
        PruneTriggerTokens = maximumContextTokens - ResponseReserveTokens;
        SummaryChunkTokens = ContextBudget.Clamp(
            ContextBudget.RoundToQuantum(maximumContextTokens / 4),
            1536,
            4096);
        RetainedHistoryTokens = Math.Max(
            2048,
            PruneTriggerTokens - SummaryChunkTokens);
        AdvancedMemoryCharacterBudget =
            maximumContextTokens * 75 / 256;
    }

    internal int MaximumContextTokens { get; }
    internal int ResponseReserveTokens { get; }
    internal int PruneTriggerTokens { get; }
    internal int SummaryChunkTokens { get; }
    internal int RetainedHistoryTokens { get; }
    internal int AdvancedMemoryCharacterBudget { get; }
}

internal static class ContextBudget
{
    internal const int DefaultMaximumContextTokens = 16384;
    internal const int MinimumMaximumContextTokens = 4096;
    internal const int MaximumMaximumContextTokens = 32768;
    internal const int TokenQuantum = 256;

    private static ConfigEntry<int> maximumContextTokens = null!;
    private static ManualLogSource log = null!;
    private static int effectiveMaximumContextTokens =
        DefaultMaximumContextTokens;
    private static bool normalizingSetting;

    internal static int ConfiguredMaximumContextTokens =>
        maximumContextTokens.Value;

    internal static ContextBudgetSnapshot Effective =>
        new(effectiveMaximumContextTokens);

    internal static void Initialize(ConfigFile config, ManualLogSource logger)
    {
        log = logger;
        maximumContextTokens = config.Bind(
            "Context",
            "MaximumContextTokens",
            DefaultMaximumContextTokens,
            new ConfigDescription(
                "Maximum combined prompt-and-response context. Values are " +
                "normalized to 256-token steps. Local GPU mode requires a " +
                "game restart after changing this value.",
                new AcceptableValueRange<int>(
                    MinimumMaximumContextTokens,
                    MaximumMaximumContextTokens)));

        NormalizeStoredSetting();
        effectiveMaximumContextTokens = maximumContextTokens.Value;
        maximumContextTokens.SettingChanged += (_, _) =>
            OnMaximumContextChanged();
    }

    internal static bool TrySetMaximumContext(
        int requested,
        out string message)
    {
        if (requested < MinimumMaximumContextTokens
            || requested > MaximumMaximumContextTokens)
        {
            message = string.Format(
                CultureInfo.InvariantCulture,
                "Enter a value from {0:N0} through {1:N0} tokens.",
                MinimumMaximumContextTokens,
                MaximumMaximumContextTokens);
            return false;
        }

        int normalized = Normalize(requested);
        maximumContextTokens.Value = normalized;
        maximumContextTokens.ConfigFile.Save();

        if (IsLoadedLocalServer())
        {
            message = string.Format(
                CultureInfo.InvariantCulture,
                "Saved {0:N0} tokens. Restart Silverpine to resize the " +
                "local Kobold context; this session remains at {1:N0}.",
                normalized,
                effectiveMaximumContextTokens);
        }
        else
        {
            message = string.Format(
                CultureInfo.InvariantCulture,
                "Applied {0:N0} tokens. New LLM requests use the updated " +
                "context and pruning budgets.",
                normalized);
        }

        return true;
    }

    internal static bool TryCreatePreview(
        string text,
        out ContextBudgetSnapshot budget,
        out string error)
    {
        if (!int.TryParse(
                text,
                NumberStyles.Integer | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out int requested))
        {
            budget = default;
            error = "Enter a whole number of tokens.";
            return false;
        }

        if (requested < MinimumMaximumContextTokens
            || requested > MaximumMaximumContextTokens)
        {
            budget = default;
            error = string.Format(
                CultureInfo.InvariantCulture,
                "Allowed range: {0:N0}–{1:N0} tokens.",
                MinimumMaximumContextTokens,
                MaximumMaximumContextTokens);
            return false;
        }

        budget = new ContextBudgetSnapshot(Normalize(requested));
        error = "";
        return true;
    }

    internal static string GetRuntimeModeDescription()
    {
        try
        {
            InferenceServerSetupHandler? handler =
                InferenceServerSetupHandler.Instance;
            if (handler == null || !handler.modelLoaded)
                return "The value will be active when the inference mode starts.";
            if (handler.apiMode)
                return "OpenRouter mode: changes apply to new requests immediately.";
            if (handler.remoteMode)
            {
                return "Remote endpoint mode: changes apply immediately, but " +
                    "the remote server must support the selected context.";
            }

            return "Local GPU mode: changing the value requires restarting " +
                "Silverpine so Kobold can resize its context cache.";
        }
        catch
        {
            return "The value will be active when the inference mode starts.";
        }
    }

    internal static void PrepareInferenceServer(
        InferenceServerSetupHandler handler)
    {
        effectiveMaximumContextTokens = maximumContextTokens.Value;
        ApplyToGameDefinitions(handler);
    }

    internal static void ApplyToGameDefinitions()
    {
        try
        {
            InferenceServerSetupHandler? handler =
                InferenceServerSetupHandler.Instance;
            if (handler != null)
                ApplyToGameDefinitions(handler);
        }
        catch (Exception exception)
        {
            log.LogWarning(
                "Could not update Silverpine's model definitions yet: " +
                exception.Message);
        }
    }

    private static void ApplyToGameDefinitions(
        InferenceServerSetupHandler handler)
    {
        var definitions = new HashSet<LegacyJSON_ModelDefinition>();
        Add(definitions, handler.gemma4SparseDefinition);
        Add(definitions, handler.gemma4SparseQATDefinition);
        Add(definitions, handler.gemma4DenseSmallDefinition);
        Add(definitions, handler.gemma4DenseLargeDefinition);
        Add(definitions, handler.debugQwenDefinition);
        Add(definitions, handler.apiDefinition);
        Add(definitions, handler.activeModelDefinition);

        if (handler.modelDefinitions != null)
        {
            foreach (LegacyJSON_ModelDefinition definition in
                     handler.modelDefinitions)
            {
                Add(definitions, definition);
            }
        }

        foreach (LegacyJSON_ModelDefinition definition in definitions)
            definition.maxContextLength = effectiveMaximumContextTokens;
    }

    private static void Add(
        HashSet<LegacyJSON_ModelDefinition> definitions,
        LegacyJSON_ModelDefinition? definition)
    {
        if (definition != null)
            definitions.Add(definition);
    }

    private static void OnMaximumContextChanged()
    {
        if (normalizingSetting)
            return;

        NormalizeStoredSetting();
        if (IsLoadedLocalServer())
        {
            log.LogWarning(
                $"Saved a {maximumContextTokens.Value:N0}-token context. " +
                "The running local Kobold server cannot resize its context " +
                $"cache, so this session remains at " +
                $"{effectiveMaximumContextTokens:N0}; restart required.");
            return;
        }

        effectiveMaximumContextTokens = maximumContextTokens.Value;
        ApplyToGameDefinitions();
        ContextBudgetSnapshot budget = Effective;
        log.LogInfo(
            $"Applied context budget: max {budget.MaximumContextTokens:N0}, " +
            $"trigger {budget.PruneTriggerTokens:N0}, retain " +
            $"{budget.RetainedHistoryTokens:N0}, summary batch " +
            $"{budget.SummaryChunkTokens:N0} tokens.");
    }

    private static void NormalizeStoredSetting()
    {
        int normalized = Normalize(maximumContextTokens.Value);
        if (normalized == maximumContextTokens.Value)
            return;

        normalizingSetting = true;
        try
        {
            maximumContextTokens.Value = normalized;
        }
        finally
        {
            normalizingSetting = false;
        }
    }

    private static bool IsLoadedLocalServer()
    {
        try
        {
            InferenceServerSetupHandler? handler =
                InferenceServerSetupHandler.Instance;
            return handler != null
                && handler.modelLoaded
                && !handler.remoteMode
                && !handler.apiMode;
        }
        catch
        {
            return false;
        }
    }

    private static int Normalize(int value)
    {
        int clamped = Clamp(
            value,
            MinimumMaximumContextTokens,
            MaximumMaximumContextTokens);
        return RoundToQuantum(clamped);
    }

    internal static int RoundToQuantum(int value) =>
        ((value + TokenQuantum / 2) / TokenQuantum) * TokenQuantum;

    internal static int Clamp(int value, int minimum, int maximum) =>
        Math.Min(maximum, Math.Max(minimum, value));

    // Harmony replacements use methods rather than fields so a setting change
    // is observed by the very next pruning check.
    internal static int GetResponseReserveTokens() =>
        Effective.ResponseReserveTokens;

    internal static float ResolveRetainedHistoryTarget(
        float vanillaFixedCeiling,
        float vanillaProportionalTarget) =>
        Effective.RetainedHistoryTokens;
}
