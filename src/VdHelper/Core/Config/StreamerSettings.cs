using System.IO;
using System.Text.Json;

namespace VdHelper.Core.Config;

/// <summary>
/// Reader for the Streamer's own settings file. Location and key set were confirmed on this
/// machine: <c>C:\ProgramData\Virtual Desktop\StreamerSettings.json</c> (the registry candidates
/// are absent — do not add a registry fallback).
/// </summary>
public sealed class StreamerSettings
{
    public const string DefaultPath = @"C:\ProgramData\Virtual Desktop\StreamerSettings.json";

    public string Path { get; init; } = DefaultPath;
    public bool Exists { get; init; }
    public JsonElement? Root { get; init; }
    public string? ParseError { get; init; }
    public DateTime? LastWriteTime { get; init; }

    public IReadOnlyList<string> TopLevelKeys { get; init; } = Array.Empty<string>();

    public static StreamerSettings Load(string path = DefaultPath)
    {
        try
        {
            if (!File.Exists(path))
                return new StreamerSettings { Path = path, Exists = false };

            var info = new FileInfo(path);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var keys = doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.EnumerateObject().Select(p => p.Name).ToList()
                : new List<string>();
            return new StreamerSettings
            {
                Path = path,
                Exists = true,
                Root = doc.RootElement.Clone(),
                TopLevelKeys = keys,
                LastWriteTime = info.LastWriteTime,
            };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new StreamerSettings { Path = path, Exists = false, ParseError = ex.Message };
        }
    }

    /// <summary>Reads a top-level value as string, or null when absent/null/other kind.</summary>
    public string? GetString(string key)
    {
        if (Root is not { ValueKind: JsonValueKind.Object } root) return null;
        if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var s = value.GetString();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public bool? GetBool(string key)
    {
        if (Root is not { ValueKind: JsonValueKind.Object } root) return null;
        if (!root.TryGetProperty(key, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.GetDouble() != 0,
            _ => null,
        };
    }

    public string? GetRaw(string key)
    {
        if (Root is not { ValueKind: JsonValueKind.Object } root) return null;
        return root.TryGetProperty(key, out var value) ? value.GetRawText() : null;
    }

    /// <summary>Strings inside an array-valued key such as <c>DontWarnApps</c>.</summary>
    public IReadOnlyList<string> GetStringArray(string key)
    {
        if (Root is not { ValueKind: JsonValueKind.Object } root) return Array.Empty<string>();
        if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return value.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();
    }

    public static DateTime? ParseTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParse(raw, out var direct)) return direct;
        if (long.TryParse(raw, out var unix))
        {
            try { return DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime; }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        return null;
    }
}