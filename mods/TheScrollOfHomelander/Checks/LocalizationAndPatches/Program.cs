using System.Runtime.CompilerServices;
using BetterTaiwuScroll.Frontend;
using BetterTaiwuScroll.Shared;
using HarmonyLib;

static class Program
{
    static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(name + ": expected " + expected + ", actual " + actual);
    }

    static void Main()
    {
        foreach (var pair in ModLocalizationCatalog.ModText)
        {
            LocalStringManager.CurLanguageKey = "CN";
            Equal(pair.Value.Cn, ModLocalization.T(pair.Key), "Chinese catalog: " + pair.Key);
            Equal(pair.Value.Cn, ModLocalization.T(pair.Value.Cn), "Chinese wording preserved");
            LocalStringManager.CurLanguageKey = "EN";
            Equal(pair.Key, ModLocalization.T(pair.Value.Cn), "English catalog: " + pair.Key);
        }
        Equal("Tier 6", ModLocalization.T(ModLocalizationCatalog.PlainGradeChinese[5]), "Plain grade");
        Equal("50%", ModLocalization.T("50%"), "Numeric option");
        Equal("Uncatalogued", ModLocalization.T("Uncatalogued"), "Missing translation");
        Equal<string>(null, ModLocalization.T(null), "Null text");
        var options = ModLocalization.Options(new[] { "行囊", "私库", "公库" });
        Equal("Travel Bag", options[0], "Storage option");
        var row = new UnityEngine.Component { gameObject = new() { name = "Setting_是否包括行囊" } };
        Equal("是否包括行囊", ModLocalization.RowKey(row), "Row identity independent of displayed language");
        LocalStringManager.CurLanguageKey = "CNH";
        Equal("连续制作", ModLocalization.T("Continuous Crafting"), "Traditional fallback");
        LocalStringManager.CurLanguageKey = "KO";
        Equal("Continuous Crafting", ModLocalization.T("Continuous Crafting"), "Korean fallback");
        LocalStringManager.CurLanguageKey = "unknown";
        Equal("连续制作", ModLocalization.T("Continuous Crafting"), "Unknown language fallback");
        var labels = ModLocalization.BuildBilingualLabelSet(new[] { "名称", "品阶" });
        Equal(true, labels.Contains("名称") && labels.Contains("Name") && labels.Contains("Tier"), "Bilingual matching");
        var troughLabels = ModLocalization.BuildBilingualLabelSet(new[] { "饲槽" });
        Equal(true, troughLabels.Contains("Trough") && troughLabels.Contains("飼槽") && troughLabels.Contains("사료통"), "Native trough matching");

        var reports = new List<string>();
        using (var patches = new ModPatchGroups())
        {
            patches.Install(typeof(Program).Assembly, "homelander.pr2.check", ModPatchGroups.Classify, reports.Add);
            Equal(1, PatchTarget.Make(), "Failed group rolls back earlier patches");
            Equal(2, PatchTarget.Ui(), "Unrelated group remains installed");
            Equal(true, reports.Any(s => s.StartsWith("Disabled patch group 'make'")), "Failure is reported");
        }
        Equal(1, PatchTarget.Ui(), "Dispose restores target");
        Console.WriteLine("PASS: localization catalog, language changes, stable row identity, options, fallbacks, Harmony rollback and independent groups.");
    }
}

static class PatchTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Make() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Ui() => 1;
}

[HarmonyPatch(typeof(PatchTarget), nameof(PatchTarget.Make))]
static class MakeABeforeFailurePatch
{
    static void Postfix(ref int __result) => __result += 100;
}

[HarmonyPatch(typeof(PatchTarget), "RemovedMethod")]
static class MakeZMissingTargetPatch
{
    static void Postfix() { }
}

[HarmonyPatch(typeof(PatchTarget), nameof(PatchTarget.Ui))]
static class UiHealthyPatch
{
    static void Postfix(ref int __result) => __result++;
}

static class LocalStringManager
{
    public static string CurLanguageKey { get; set; }
}

namespace UnityEngine
{
    public class Component { public GameObject gameObject { get; set; } }
    public class GameObject { public string name { get; set; } }
}

namespace HarmonyLib
{
    static class GameHarmonyCompatibility
    {
        public static void UnpatchSelf(this Harmony harmony) => harmony.UnpatchAll(harmony.Id);
    }
}
