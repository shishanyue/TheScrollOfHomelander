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
            patches.Install(typeof(Program).Assembly, "homelander.pr2.check",
                type => type == typeof(MakeABeforeFailurePatch) || type == typeof(MakeZMissingTargetPatch)
                    || type == typeof(UiHealthyPatch) ? ModPatchGroups.Classify(type) : null, reports.Add);
            Equal(1, PatchTarget.Make(), "Failed group rolls back earlier patches");
            Equal(2, PatchTarget.Ui(), "Unrelated group remains installed");
            Equal(true, reports.Any(s => s.StartsWith("Disabled patch group 'make'")), "Failure is reported");
        }
        Equal(1, PatchTarget.Ui(), "Dispose restores target");

        CheckMonthlyIsolation("diagnostics failure", diagnosticsFailure: true);
        CheckMonthlyIsolation("performance failure", performanceFailure: true);
        CheckMonthlyIsolation("diagnostics and performance failures", diagnosticsFailure: true, performanceFailure: true);
        CheckMonthlyIsolation("purchase failure", purchaseFailure: true);
        CheckMonthlyIsolation("healthy monthly features");
        Console.WriteLine("PASS: localization catalog, language changes, stable row identity, options, fallbacks, Harmony rollback and monthly feature isolation.");
    }

    static void CheckMonthlyIsolation(string scenario, bool diagnosticsFailure = false,
        bool performanceFailure = false, bool purchaseFailure = false)
    {
        var reports = new List<string>();
        using (var patches = new ModPatchGroups())
        {
            patches.Install(typeof(Program).Assembly, "homelander.monthly.check", type =>
            {
                var participates = type == typeof(AutoMonthlyPurchaseAdvanceMonthHealthyPatch)
                    || type == typeof(AutoCultivationAdvanceMonthHealthyPatch)
                    || (diagnosticsFailure && (type == typeof(AdvanceMonthDiagnosticsABeforeFailurePatch)
                        || type == typeof(AdvanceMonthDiagnosticsZMissingTargetPatch)))
                    || (performanceFailure && (type == typeof(AdvanceMonthPerformanceABeforeFailurePatch)
                        || type == typeof(AdvanceMonthPerformanceZMissingTargetPatch)))
                    || (purchaseFailure && type == typeof(AutoMonthlyPurchaseAdvanceMonthZMissingTargetPatch));
                return participates ? ModPatchGroups.Classify(type) : null;
            }, reports.Add);

            Equal(purchaseFailure ? 1 : 2, PatchTarget.MonthlyPurchase(), scenario + ": purchase hook executes unless its own group fails");
            Equal(3, PatchTarget.Cultivation(), scenario + ": cultivation hook still executes");
            Equal(1, PatchTarget.Diagnostics(), scenario + ": failed diagnostics patches are rolled back");
            Equal(1, PatchTarget.Performance(), scenario + ": failed performance patches are rolled back");
            Equal(true, reports.Contains("Installed patch group: cultivation"), scenario + ": cultivation installed independently");
            Equal(!purchaseFailure, reports.Contains("Installed patch group: monthly-purchase"), scenario + ": purchase group status");
            Equal(diagnosticsFailure, reports.Any(s => s.StartsWith("Disabled patch group 'month-diagnostics'")), scenario + ": diagnostics failure reported");
            Equal(performanceFailure, reports.Any(s => s.StartsWith("Disabled patch group 'month'")), scenario + ": performance failure reported");
            Equal(purchaseFailure, reports.Any(s => s.StartsWith("Disabled patch group 'monthly-purchase'")), scenario + ": purchase failure reported");
        }

        Equal(1, PatchTarget.MonthlyPurchase(), scenario + ": dispose restores purchase target");
        Equal(1, PatchTarget.Cultivation(), scenario + ": dispose restores cultivation target");
        Equal(1, PatchTarget.Diagnostics(), scenario + ": dispose restores diagnostics target");
        Equal(1, PatchTarget.Performance(), scenario + ": dispose restores performance target");
        Console.WriteLine("PASS: " + scenario);
    }
}

static class PatchTarget
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Make() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Ui() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int MonthlyPurchase() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Cultivation() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Diagnostics() => 1;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Performance() => 1;
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

[HarmonyPatch(typeof(PatchTarget), nameof(PatchTarget.MonthlyPurchase))]
static class AutoMonthlyPurchaseAdvanceMonthHealthyPatch
{
    static void Postfix(ref int __result) => __result++;
}

[HarmonyPatch(typeof(PatchTarget), "RemovedPurchaseMethod")]
static class AutoMonthlyPurchaseAdvanceMonthZMissingTargetPatch
{
    static void Postfix() { }
}

[HarmonyPatch(typeof(PatchTarget), nameof(PatchTarget.Cultivation))]
static class AutoCultivationAdvanceMonthHealthyPatch
{
    static void Postfix(ref int __result) => __result += 2;
}

[HarmonyPatch(typeof(PatchTarget), nameof(PatchTarget.Diagnostics))]
static class AdvanceMonthDiagnosticsABeforeFailurePatch
{
    static void Postfix(ref int __result) => __result += 100;
}

[HarmonyPatch(typeof(PatchTarget), "RemovedDiagnosticsMethod")]
static class AdvanceMonthDiagnosticsZMissingTargetPatch
{
    static void Postfix() { }
}

[HarmonyPatch(typeof(PatchTarget), nameof(PatchTarget.Performance))]
static class AdvanceMonthPerformanceABeforeFailurePatch
{
    static void Postfix(ref int __result) => __result += 100;
}

[HarmonyPatch(typeof(PatchTarget), "RemovedPerformanceMethod")]
static class AdvanceMonthPerformanceZMissingTargetPatch
{
    static void Postfix() { }
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
