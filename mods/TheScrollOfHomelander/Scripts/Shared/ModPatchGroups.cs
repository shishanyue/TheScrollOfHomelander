using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace BetterTaiwuScroll.Shared;

internal sealed class ModPatchGroups : IDisposable
{
    private readonly List<Harmony> _installed = new();
    private Action<string> _report;

    internal void Install(Assembly assembly, string prefix, Func<Type, string> classify,
        Action<string> report, Action<string> validate = null)
    {
        _report = report;
        var groups = GetPatchTypes(assembly)
            .Select(type => new { Type = type, Group = classify(type) })
            .Where(entry => entry.Group != null)
            .GroupBy(entry => entry.Group).OrderBy(group => group.Key);
        foreach (var group in groups)
        {
            var harmony = new Harmony(prefix + "." + group.Key);
            try
            {
                validate?.Invoke(group.Key);
                foreach (var entry in group.OrderBy(entry => entry.Type.FullName))
                    harmony.CreateClassProcessor(entry.Type).Patch();
                _installed.Add(harmony);
                report("Installed patch group: " + group.Key);
            }
            catch (Exception ex)
            {
                if (!TryUninstall(harmony)) _installed.Add(harmony);
                report("Disabled patch group '" + group.Key + "': " + ex);
            }
        }
    }

    public void Dispose()
    {
        for (var i = _installed.Count - 1; i >= 0; i--)
            TryUninstall(_installed[i]);
        _installed.Clear();
    }

    private IEnumerable<Type> GetPatchTypes(Assembly assembly)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
            foreach (var error in ex.LoaderExceptions)
                _report?.Invoke("Patch type unavailable: " + error);
        }

        foreach (var type in types)
        {
            if (type == null) continue;
            bool isPatch;
            try { isPatch = type.IsDefined(typeof(HarmonyPatch), false); }
            catch (Exception ex)
            {
                _report?.Invoke("Patch metadata unavailable for '" + type.FullName + "': " + ex);
                continue;
            }
            // Discovery must not instantiate attributes referencing removed game types.
            if (isPatch) yield return type;
        }
    }

    private bool TryUninstall(Harmony harmony)
    {
        try
        {
            // Harmony rebuilds wrappers during unpatching. Remove this owner's IL
            // transforms first so a failed transform cannot run again during rollback.
            foreach (var method in Harmony.GetAllPatchedMethods().ToArray())
            {
                var info = Harmony.GetPatchInfo(method);
                if (info != null && info.Transpilers.Any(patch => patch.owner == harmony.Id))
                    harmony.Unpatch(method, HarmonyPatchType.Transpiler, harmony.Id);
            }
            harmony.UnpatchSelf();
            return true;
        }
        catch (Exception ex)
        {
            _report?.Invoke("Patch cleanup incomplete for '" + harmony.Id + "': " + ex);
            return false;
        }
    }

    internal static string Classify(Type type)
    {
        var name = type.Name;
        if (name.StartsWith("ModSession", StringComparison.Ordinal)) return "session";
        if (name.StartsWith("ItemScroll", StringComparison.Ordinal)) return "item-scroll";
        if (name.Contains("AdvanceMonth")) return "month";
        if (name.Contains("Recruit") || name.Contains("Harvest") || name.Contains("BuildingEarnings")) return "recruit";
        if (name.Contains("Make") || name.Contains("Repair")) return "make";
        if (name.Contains("Map") || name.Contains("WorldState")) return "map";
        if (name.Contains("Exchange") || name.Contains("Shop") || name.Contains("Purchase") || name.Contains("Transfer")) return "trade";
        if (name.Contains("Cultivation")) return "cultivation";
        if (name.Contains("Search")) return "search";
        return "ui";
    }
}
