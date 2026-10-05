﻿using System.Globalization;
using System.Text.Json;

namespace VdHelper.Core.Config;

/// <summary>
/// Whether a value the user typed is one the catalog says this key accepts.
/// <para>
/// <c>ParameterInfo</c> carries <c>Type</c>, <c>Range</c> and <c>Caution</c> because the survey
/// recorded them, and until now nothing read them at write time. The parameter page only toggles
/// bools through a fixed pair of choices, so it never needed a check; <c>--set-param</c> takes a raw
/// JSON value and was writing whatever it was given. That left the <c>range</c> column in
/// parameters.json as documentation with no reader — an enum would accept 999, a float would ignore
/// the slider bounds the survey wrote down, and a path key would take any string at all.
/// </para>
/// <para>
/// Deliberately conservative: a key whose type or range this cannot interpret is <b>allowed</b>,
/// with the reason saying so. Refusing writes the tool cannot explain would be worse than writing one
/// the catalog was vague about, and the point is to stop the catalog from being quietly ignored.
/// </para>
/// </summary>
public static class ParameterValues
{
    public static bool IsAcceptable(ParameterInfo info, JsonElement value, out string why)
    {
        var raw = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => value.GetRawText(),
        };

        var type = info.Type.Trim().ToLowerInvariant();

        // bool: the raw text of a JSON bool is already true/false, and a quoted "true" is what the
        // Streamer's own file holds, so accept either spelling but nothing else.
        if (type.StartsWith("bool", StringComparison.Ordinal))
        {
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) { why = ""; return true; }
            var s = raw.Trim().Trim('"').ToLowerInvariant();
            if (s is "true" or "false") { why = ""; return true; }
            why = "这是布尔键，只接受 true / false";
            return false;
        }

        // int / enum: the catalog's Range column lists the legal values for an enum.
        if (type.StartsWith("int", StringComparison.Ordinal)
            || type.StartsWith("enum", StringComparison.Ordinal))
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                {
                    why = "不是整数";
                    return false;
                }
                if (type.StartsWith("enum", StringComparison.Ordinal) && !EnumAllows(info, n.ToString()))
                {
                    why = "不在目录列出的合法取值里";
                    return false;
                }
                why = "";
                return true;
            }

            // An enum is often written as a string in this file; the survey's Range column has the
            // list, so a string is checked against it when it looks like a list.
            var allowed = EnumAllowed(info);
            if (allowed.Count > 0 && !allowed.Contains(raw.Trim().Trim('"'), StringComparer.OrdinalIgnoreCase))
            {
                why = "不在目录列出的合法取值里：" + string.Join(" / ", allowed);
                return false;
            }
            if (allowed.Count == 0 && !long.TryParse(raw.Trim().Trim('"'), out _))
            {
                why = "类型是 enum 或 int，但没有给出可校验的取值列表，且这个值不是整数";
                return false;
            }
            why = "";
            return true;
        }

        if (type.StartsWith("float", StringComparison.Ordinal)
            || type.StartsWith("double", StringComparison.Ordinal))
        {
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                why = "不是数字";
                return false;
            }
            var (lo, hi) = NumericRange(info);
            if (lo is not null && hi is not null)
            {
                var d = double.Parse(raw, CultureInfo.InvariantCulture);
                if (d < lo || d > hi)
                {
                    why = $"超出目录写的范围 {lo}–{hi}";
                    return false;
                }
            }
            why = "";
            return true;
        }

        // Anything else (paths, free strings, DateTime): the catalog states no constraint this can
        // check, so say so rather than pretending it validated.
        why = "";
        return true;
    }

    private static List<string> EnumAllowed(ParameterInfo info)
    {
        var range = (info.Range ?? "").Trim();
        if (range.Length == 0 || range is "(无)" or "-" or "n/a") return new List<string>();
        // Only treat the Range column as a list when it is actually a list — "0.4–1.0" is not.
        if (range.Any(ch => ch is '–' or '—' or '~' or '≤' or '≥')) return new List<string>();
        var allowed = new List<string>();
        foreach (var chunk in range.Split('/', '|', ','))
        {
            var s = chunk.Trim().Trim('"', '\'', '`');
            if (s.Length == 0) continue;
            var parts = s.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && long.TryParse(parts[0], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out _))
            {
                allowed.Add(parts[0]);            // the number that goes in the file
                allowed.Add(parts[1].Trim());     // and the label a person would type
                continue;
            }
            if (parts.Length == 1) allowed.Add(parts[0]);
        }
        return allowed.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool EnumAllows(ParameterInfo info, string n) =>
        EnumAllowed(info).Any(a => a.Equals(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>Low/high from a range written as "0.4–1.0", "0.4~1.0", "1..10" and similar.</summary>
    private static (double? Lo, double? Hi) NumericRange(ParameterInfo info)
    {
        var r = (info.Range ?? "").Trim();
        var seps = new[] { "–", "—", "~", "..", " to ", " - " };
        foreach (var s in seps)
        {
            var i = r.IndexOf(s, StringComparison.Ordinal);
            if (i <= 0) continue;
            var lo = r[..i].Trim();
            var hi = r[(i + s.Length)..].Trim();
            if (double.TryParse(lo, NumberStyles.Float, CultureInfo.InvariantCulture, out var l)
                && double.TryParse(hi, NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
                return (l, h);
        }
        return (null, null);
    }
}
