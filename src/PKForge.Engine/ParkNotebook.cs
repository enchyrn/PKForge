namespace PKForge.Engine;

/// <summary>A daycare stay whose EXP couldn't be written when it ended: who, from when, for how long and at what pace.
/// Settling it later reckons the EXP on the Pokémon as it is then, the way a pickup would have.</summary>
public sealed record ParkNote(
    string ResidentId, string? Identity, string Name, string? Format,
    DateTimeOffset Since, DateTimeOffset Ended, double Hours, DaycarePace Pace,
    DateTimeOffset? Settled = null, uint ExpWritten = 0);

/// <summary>Blissey's notebook: stays that ended without their EXP written (<see cref="DaycarePickup.Owed"/>), kept until
/// their saves are open and settled; settled ones stay as a record, the oldest making room first.</summary>
public static class ParkNotebook
{
    public const int MostKept = 100;

    public static IReadOnlyList<ParkNote> Owed(IReadOnlyList<ParkNote> notes) => notes.Where(n => n.Settled is null).ToArray();

    public static IReadOnlyList<ParkNote> Add(IReadOnlyList<ParkNote> notes, ParkNote note)
    {
        var all = notes.Append(note).ToList();
        // Owed notes are never dropped: only settled ones make room, oldest first.
        while (all.Count > MostKept && all.FindIndex(n => n.Settled is not null) is var i and >= 0) all.RemoveAt(i);
        return all;
    }

    public static IReadOnlyList<ParkNote> Settle(IReadOnlyList<ParkNote> notes, ParkNote note, DateTimeOffset at, uint expWritten) =>
        notes.Select(n => ReferenceEquals(n, note) || n == note ? n with { Settled = at, ExpWritten = expWritten } : n).ToArray();
}
