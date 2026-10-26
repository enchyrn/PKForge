using System.Security.Cryptography;
using PKHeX.Core;

namespace PKForge.Engine;

/// <summary>
/// Who a Poképark resident is, from what never changes in play: its trainer ID with its PID and
/// encryption constant (Gen 3 on), or with its DVs on the Game Boy, where there is no PID.
/// Friendship, EXP, HP, moves and evolving leave it the same Pokémon; another Pokémon put in its
/// slot or a different trainer's copy does not match. The key has no ':' so it fits in a resident id.
/// </summary>
public static class ParkIdentity
{
    /// <summary>The identity of <paramref name="data"/>, or null when it isn't a readable Pokémon.</summary>
    public static string? Of(byte[] data, string? format) =>
        // A CFRU hack's record (Unbound, Radical Red) is read as it is: one Gen 3 can't hold doesn't convert.
        RadicalRed.CfruEntity.Recognize(format) is { } cfru
            ? RadicalRed.CfruEntity.Identity(data, cfru) is { } id ? $"id{id.Otid:X8}{id.Pid:X8}{id.Pid:X8}" : null
            : EntityBytes.Parse(data, format) is { Species: > 0 } pk ? Of(pk) : null;

    /// <summary>Whether <paramref name="data"/> is an egg, read from the record itself (a CFRU hack's through its
    /// own layout, which has no met data to ask); null when it isn't a readable Pokémon.</summary>
    public static bool? IsEgg(byte[] data, string? format) =>
        RadicalRed.CfruEntity.Recognize(format) is { } cfru
            ? RadicalRed.CfruEntity.Facts(data, cfru)?.IsEgg
            : EntityBytes.Parse(data, format)?.IsEgg;

    public static string Of(PKM pk) => pk.Format <= 2
        ? $"gb{pk.ID32:X8}{pk.IV_ATK:X}{pk.IV_DEF:X}{pk.IV_SPE:X}{pk.IV_SPA:X}"
        : $"id{pk.ID32:X8}{pk.PID:X8}{pk.EncryptionConstant:X8}";

    /// <summary>The identity a save resident's id ends with (save:document:box:slot:identity), or null for
    /// a bank resident, an auto-filled one (no fifth part) or one sent before identities were kept.</summary>
    public static string? InResidentId(string residentId) =>
        residentId.Split(':') is ["save", _, _, _, var last] && !IsLegacyFingerprint(last) ? last : null;

    /// <summary>Who a daycare stay is for: what its save said at check-in, else the resident's own identity (kept
    /// when it was sent), else the one its id carries. A stay checked in while its save was closed used to record
    /// nothing, and its pickup then refused every Pokémon as "not the one you left".</summary>
    public static string? ForStay(string? recorded, string? residentIdentity, string residentId) =>
        recorded ?? residentIdentity ?? InResidentId(residentId);

    /// <summary>Stays and residents recorded before identities were kept a SHA-256 of the whole
    /// Pokémon: 64 hex digits, which no identity key is.</summary>
    public static bool IsLegacyFingerprint(string recorded) =>
        recorded.Length == 64 && recorded.All(Uri.IsHexDigit);

    /// <summary>Whether <paramref name="data"/> is still the Pokémon <paramref name="recorded"/>
    /// was taken from. A legacy fingerprint keeps its old meaning: the bytes must be unchanged.</summary>
    public static bool IsSameIndividual(string? recorded, byte[] data, string? format) => recorded switch
    {
        null => false,
        _ when IsLegacyFingerprint(recorded) =>
            string.Equals(recorded, Convert.ToHexString(SHA256.HashData(data)), StringComparison.OrdinalIgnoreCase),
        _ => Of(data, format) == recorded,
    };
}
