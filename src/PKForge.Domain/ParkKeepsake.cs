using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PKForge.Domain;

/// <summary>One stored preference: its key, its stored type (string, int, long, bool, float) and its value as text.</summary>
public sealed record KeepsakePreference(string Key, string Type, string Value);

/// <summary>One file of the Bank's folder: its path inside the folder and its bytes in base64.</summary>
public sealed record KeepsakeFile(string Path, string Base64);

/// <summary>The Poképark and the Bank as they stood when it was written: every Poképark preference and the Bank's own
/// files (so Bank residents keep their ids), with how many residents and Bank entries they hold.</summary>
public sealed record ParkKeepsake(
    int Version, DateTimeOffset SavedAt, string AppVersion,
    IReadOnlyList<KeepsakePreference> Preferences, IReadOnlyList<KeepsakeFile> Bank,
    int Residents, int BankEntries);

/// <summary>
/// The park keepsake's file rules. It lives outside the app, in the linked saves folder's root, as
/// <see cref="Slots"/> rotating copies: each write replaces only the oldest (or an unreadable) copy, and every copy
/// carries a SHA-256 of its payload, so a write cut off halfway is ignored and the others still restore. It only ever
/// fills an empty app.
/// </summary>
public static class ParkKeepsakeFormat
{
    public const int CurrentVersion = 1;
    public const int Slots = 5;
    public const string ParkPrefix = "pkforge.pokepark.";
    private const string NamePrefix = "pkforge-keepsake-";
    private static readonly TimeSpan MinimumGap = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Envelope(int Version, string Sha256, string Payload);

    public static byte[] Write(ParkKeepsake keepsake)
    {
        var payload = JsonSerializer.Serialize(keepsake, Json);
        return JsonSerializer.SerializeToUtf8Bytes(new Envelope(CurrentVersion, Hash(payload), payload), Json);
    }

    /// <summary>The keepsake in <paramref name="bytes"/>, or null when it's cut off, changed, or from a newer format.</summary>
    public static ParkKeepsake? Read(byte[] bytes)
    {
        try
        {
            if (JsonSerializer.Deserialize<Envelope>(bytes, Json) is not { Version: CurrentVersion, Payload: { } payload } envelope) return null;
            if (!string.Equals(envelope.Sha256, Hash(payload), StringComparison.Ordinal)) return null;
            return JsonSerializer.Deserialize<ParkKeepsake>(payload, Json) is { Version: CurrentVersion } keepsake ? keepsake : null;
        }
        catch (JsonException) { return null; }
    }

    private static string Hash(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    public static IReadOnlyList<string> ParkKeys(IEnumerable<string> keys) =>
        keys.Where(k => k.StartsWith(ParkPrefix, StringComparison.Ordinal)).ToArray();

    public static string SlotName(int slot) => $"{NamePrefix}{slot}.json";

    public static bool IsKeepsake(string fileName) =>
        Enumerable.Range(1, Slots).Any(i => string.Equals(fileName, SlotName(i), StringComparison.Ordinal));

    /// <summary>The copy to write next: a free slot, else an unreadable one (null date), else the oldest.</summary>
    public static string NextSlot(IReadOnlyList<(string Name, DateTimeOffset? SavedAt)> existing)
    {
        var known = existing.Where(e => IsKeepsake(e.Name)).ToDictionary(e => e.Name, e => e.SavedAt);
        for (var i = 1; i <= Slots; i++)
            if (!known.ContainsKey(SlotName(i))) return SlotName(i);
        return known.OrderBy(e => e.Value.HasValue).ThenBy(e => e.Value).First().Key;
    }

    public static ParkKeepsake? Newest(IEnumerable<ParkKeepsake?> copies) =>
        copies.Where(c => c is not null).OrderByDescending(c => c!.SavedAt).FirstOrDefault();

    public static bool IsDue(DateTimeOffset? lastWritten, DateTimeOffset now) => lastWritten is null || now - lastWritten >= MinimumGap;

    /// <summary>Restore is only offered to an app with no park and no Bank: it never merges into what's there.</summary>
    public static bool CanRestore(int residents, int bankEntries) => residents == 0 && bankEntries == 0;

    /// <summary>An empty app isn't kept: a fresh install leaving its empty park would push a real park's copies out.</summary>
    public static bool WorthKeeping(int residents, int bankEntries) => residents > 0 || bankEntries > 0;
}
