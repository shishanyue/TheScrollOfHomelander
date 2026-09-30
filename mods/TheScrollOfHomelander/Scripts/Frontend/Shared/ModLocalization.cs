#nullable disable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterTaiwuScroll.Frontend;

internal static class ModLocalization
{
    internal enum Lang { Cn, Cnh, En, Ko }

    internal readonly struct LangSet
    {
        internal readonly string Cn;
        internal readonly string Cnh;
        internal readonly string Ko;

        internal LangSet(string cn = null, string cnh = null, string ko = null)
        {
            Cn = cn;
            Cnh = cnh;
            Ko = ko;
        }

        internal string For(Lang lang) => lang switch
        {
            Lang.Cn => Cn,
            Lang.Cnh => string.IsNullOrEmpty(Cnh) ? Cn : Cnh,
            Lang.Ko => Ko,
            _ => null
        };
    }

    // Use the game's active language, which can differ from persisted preferences.
    internal static Lang Current
    {
        get
        {
            try { return Normalize(LocalStringManager.CurLanguageKey); }
            catch { return Lang.Cn; }
        }
    }

    internal static bool IsEnglish => Current == Lang.En;
    private static readonly Dictionary<string, string> ChineseKeys = BuildChineseKeys();

    private static Lang Normalize(string raw) => (raw ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "EN" => Lang.En,
        "CNH" or "TC" => Lang.Cnh,
        "KO" or "KR" => Lang.Ko,
        _ => Lang.Cn
    };

    private static Dictionary<string, string> BuildChineseKeys()
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in ModLocalizationCatalog.ModText)
            if (!string.IsNullOrEmpty(entry.Value.Cn)) keys[entry.Value.Cn] = entry.Key;
        for (var i = 0; i < ModLocalizationCatalog.PlainGradeChinese.Length; i++)
            keys[ModLocalizationCatalog.PlainGradeChinese[i]] = "Tier " + (i + 1);
        return keys;
    }

    internal static string T(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var language = Current;
        var chineseSource = ChineseKeys.TryGetValue(text, out var english);
        if (!chineseSource) english = text;
        // Existing Chinese wording and stable settings keys are preserved verbatim.
        if (chineseSource && language == Lang.Cn) return text;
        if (!ModLocalizationCatalog.ModText.TryGetValue(english, out var set)) return text;
        if (language == Lang.En) return english;
        return set.For(language) ?? (chineseSource ? text : english);
    }

    internal static string[] Options(IReadOnlyList<string> options)
    {
        var result = new string[options.Count];
        for (var i = 0; i < result.Length; i++) result[i] = T(options[i]);
        return result;
    }

    internal static string RowKey(Component item)
    {
        const string prefix = "Setting_";
        var name = item == null ? null : item.gameObject.name;
        return name != null && name.StartsWith(prefix, StringComparison.Ordinal)
            ? name.Substring(prefix.Length) : name;
    }

    internal static string GameEnglish(string chinese) =>
        ModLocalizationCatalog.GameLabels.TryGetValue(chinese, out var en) ? en : null;

    internal static HashSet<string> BuildBilingualLabelSet(IEnumerable<string> chineseLabels)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cn in chineseLabels)
        {
            if (string.IsNullOrEmpty(cn)) continue;
            labels.Add(cn);
            var en = GameEnglish(cn);
            if (!string.IsNullOrEmpty(en)) labels.Add(en);
            if (ModLocalizationCatalog.GameLabelVariants.TryGetValue(cn, out var variants))
                foreach (var variant in variants) labels.Add(variant);
        }
        return labels;
    }
}
