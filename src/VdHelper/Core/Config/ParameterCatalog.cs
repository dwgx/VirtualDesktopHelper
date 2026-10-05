using System.IO;
using System.Reflection;
using System.Text.Json;

namespace VdHelper.Core.Config;

/// <summary>
/// One Streamer setting, as researched. The catalog is generated from
/// <c>research/04-streamer-settings/01-config-keys.md</c> by <c>tools/extract-parameters.py</c>
/// and embedded, so the app never invents a setting the Streamer does not have.
/// </summary>
public sealed record ParameterInfo(
    string Key,
    string Type,
    string Default,
    string Range,
    string Effect,
    bool AffectsLan,
    bool AffectsQuality,
    bool NeedsRestart,
    bool NeedsReconnect,
    bool ReadOnly,
    /// <summary>True only when ReadOnly is because the value is DPAPI-encrypted pairing material.</summary>
    bool Secret,
    bool Caution,
    string Source,
    int Table)
{
    /// <summary>Table 0 is the PC-side StreamerSettings; later tables are shared/headset settings.</summary>
    public bool LivesOnPc => Table == 0;
}

public static class ParameterCatalog
{
    private const string ResourceName = "VdHelper.Resources.parameters.json";

    private static readonly Lazy<IReadOnlyList<ParameterInfo>> Cache = new(Load);

    public static IReadOnlyList<ParameterInfo> All => Cache.Value;

    public static IReadOnlyList<ParameterInfo> PcSide => All.Where(p => p.LivesOnPc).ToList();

    public static IReadOnlyList<ParameterInfo> HeadsetSide => All.Where(p => !p.LivesOnPc).ToList();

    private static IReadOnlyList<ParameterInfo> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
            return Array.Empty<ParameterInfo>();

        using var doc = JsonDocument.Parse(stream);
        var list = new List<ParameterInfo>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var key = Str(e, "key");
            if (string.IsNullOrWhiteSpace(key)) continue;
            list.Add(new ParameterInfo(
                key,
                Str(e, "type"),
                Str(e, "default"),
                Str(e, "range"),
                Str(e, "effect"),
                Bool(e, "affectsLan"),
                Bool(e, "affectsQuality"),
                Bool(e, "needsRestart"),
                Bool(e, "needsReconnect"),
                Bool(e, "readOnly"),
                Bool(e, "secret"),
                Bool(e, "caution"),
                Str(e, "source"),
                (int)(Num(e, "table"))));
        }
        return list;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}

/// <summary>
/// Writer for StreamerSettings.json. Two upstream hazards make this delicate and are enforced
/// here rather than left to the UI: the Streamer debounces saves by 2 s and would overwrite an
/// external write, and it silently falls back to <c>{}</c> on malformed JSON — so the Streamer
/// must be closed and the JSON must be validated before it is written.
/// </summary>
public static class StreamerConfigWriter
{
    public sealed record WriteResult(bool Success, string Message, string? BackupPath = null);

    public static WriteResult Write(string path, string key, JsonElement value)
    {
        var backup = path + ".vdhelper.bak";
        var text = File.ReadAllText(path);

        JsonNodeShim.Parse(text);           // throws on malformed JSON before we touch anything
        File.Copy(path, backup, overwrite: true);

        var doc = System.Text.Json.Nodes.JsonNode.Parse(text)!.AsObject();
        doc[key] = System.Text.Json.Nodes.JsonValue.Create(value);
        var updated = doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        System.Text.Json.Nodes.JsonNode.Parse(updated);  // validate what we are about to write
        File.WriteAllText(path, updated);
        return new WriteResult(true, $"已写入 {key}", backup);
    }
}

/// <summary>Indirection so the write path fails loudly on bad JSON instead of silently resetting.</summary>
internal static class JsonNodeShim
{
    public static void Parse(string text)
    {
        _ = System.Text.Json.Nodes.JsonNode.Parse(text)
            ?? throw new System.Text.Json.JsonException("empty JSON document");
    }
}