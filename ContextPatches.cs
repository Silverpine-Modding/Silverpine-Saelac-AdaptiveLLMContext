#nullable enable

using HarmonyLib;
using KoboldSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SilverpineMods.AdaptiveLLMContext;

[HarmonyPatch(typeof(InferenceServerSetupHandler), "Awake")]
internal static class InferenceServerContextPatch
{
    private static void Postfix(InferenceServerSetupHandler __instance)
    {
        ContextBudget.PrepareInferenceServer(__instance);
    }
}

[HarmonyPatch(
    typeof(GenParams),
    MethodType.Constructor,
    typeof(string),
    typeof(string),
    typeof(bool))]
internal static class GenerationContextPatch
{
    private static void Postfix(GenParams __instance)
    {
        __instance.max_context_length =
            ContextBudget.Effective.MaximumContextTokens;
    }
}

[HarmonyPatch]
internal static class CompressionTriggerPatch
{
    private static readonly FieldInfo MaxContextLengthField =
        AccessTools.Field(
            typeof(LegacyJSON_ModelDefinition),
            nameof(LegacyJSON_ModelDefinition.maxContextLength));
    private static readonly MethodInfo ReserveGetter =
        AccessTools.Method(
            typeof(ContextBudget),
            nameof(ContextBudget.GetResponseReserveTokens));

    private static MethodBase TargetMethod()
    {
        MethodInfo checkMemoryCompression = AccessTools.Method(
            typeof(NeuralNPC),
            "CheckMemoryCompression");
        AsyncStateMachineAttribute? stateMachine = checkMemoryCompression
            .GetCustomAttribute<AsyncStateMachineAttribute>();
        if (stateMachine == null)
        {
            throw new MissingMethodException(
                "Could not find NeuralNPC.CheckMemoryCompression's async " +
                "state machine.");
        }

        return AccessTools.Method(stateMachine.StateMachineType, "MoveNext")
            ?? throw new MissingMethodException(
                "Could not find NeuralNPC.CheckMemoryCompression.MoveNext.");
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> code = instructions.ToList();
        int replacements = ReplaceReserveConstants(code);
        if (replacements != 1)
        {
            throw new InvalidOperationException(
                "Expected one CheckMemoryCompression response reserve, but " +
                $"found {replacements}.");
        }

        Plugin.Log.LogInfo(
            "Installed the formula-derived context pruning trigger.");
        return code;
    }

    internal static int ReplaceReserveConstants(List<CodeInstruction> code)
    {
        int replacements = 0;
        for (int index = 1; index + 1 < code.Count; index++)
        {
            if (!LoadsInteger(code[index], 512)
                || code[index - 1].opcode != OpCodes.Ldfld
                || !Equals(code[index - 1].operand, MaxContextLengthField)
                || code[index + 1].opcode != OpCodes.Sub)
            {
                continue;
            }

            code[index].opcode = OpCodes.Call;
            code[index].operand = ReserveGetter;
            replacements++;
        }

        return replacements;
    }

    private static bool LoadsInteger(CodeInstruction instruction, int value)
    {
        if (instruction.opcode == OpCodes.Ldc_I4)
            return instruction.operand is int integer && integer == value;
        if (instruction.opcode == OpCodes.Ldc_I4_S)
        {
            return instruction.operand is sbyte shortInteger
                && shortInteger == value;
        }

        return false;
    }
}

[HarmonyPatch]
internal static class CompressionDebugDisplayPatch
{
    private static MethodBase TargetMethod() =>
        FindCompilerGeneratedMethod(
            "<CheckMemoryCompression>g__PrintRemainingTokensDebug|");

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> code = instructions.ToList();
        int replacements =
            CompressionTriggerPatch.ReplaceReserveConstants(code);
        if (replacements != 2)
        {
            throw new InvalidOperationException(
                "Expected two debug-display response reserves, but found " +
                replacements + ".");
        }

        return code;
    }

    internal static MethodInfo FindCompilerGeneratedMethod(string prefix)
    {
        foreach (Type nestedType in typeof(NeuralNPC).GetNestedTypes(
                     BindingFlags.Public | BindingFlags.NonPublic))
        {
            MethodInfo? method = nestedType.GetMethods(
                    BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic)
                .FirstOrDefault(candidate =>
                    candidate.Name.StartsWith(
                        prefix,
                        StringComparison.Ordinal));
            if (method != null)
                return method;
        }

        throw new MissingMethodException(
            "Could not find compiler-generated method " + prefix + ".");
    }
}

[HarmonyPatch]
internal static class RetainedHistoryTargetPatch
{
    private static readonly MethodInfo VanillaMin =
        AccessTools.Method(
            typeof(Mathf),
            nameof(Mathf.Min),
            new[] { typeof(float), typeof(float) });
    private static readonly MethodInfo TargetResolver =
        AccessTools.Method(
            typeof(ContextBudget),
            nameof(ContextBudget.ResolveRetainedHistoryTarget));

    private static MethodBase TargetMethod() =>
        CompressionDebugDisplayPatch.FindCompilerGeneratedMethod(
            "<CheckMemoryCompression>g__Valid|");

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        int replacements = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(VanillaMin))
            {
                instruction.operand = TargetResolver;
                replacements++;
            }

            yield return instruction;
        }

        if (replacements != 1)
        {
            throw new InvalidOperationException(
                "Expected one retained-history target calculation, but " +
                $"found {replacements}.");
        }

        Plugin.Log.LogInfo(
            "Replaced Silverpine's fixed 3,400-token pruning ceiling with " +
            "the adaptive retention target.");
    }
}
