using System.Text.Json;
using PKForge.App.ViewModels;
using PKForge.Domain;
using PKForge.Engine;

namespace PKForge.App.Services;

/// <summary>A resident left at the Poképark daycare: since when, and who it is at check-in
/// (<see cref="ParkIdentity"/>; stays from before kept a hash of its whole data), so the daycare only
/// ever writes back to the very Pokémon it was given, however it was played since.</summary>
public sealed record ParkStay(string ResidentId, DateTimeOffset Since, string? Fingerprint);

/// <summary>
/// The Poképark daycare against the player's real Pokémon. A stay earns EXP at the chosen pace, as far as
/// the Pokémon's own game allows (<see cref="ParkDaycareService"/>); on pick-up the EXP goes back
/// to its save slot or Bank entry when the player allows it and the Pokémon has not changed since.
/// Eggs from two guests go to the Bank. With write-back off, everything stays in the park.
/// </summary>
public sealed class PokeparkDaycareService(PokeparkService park, IBankService bank, ISaveEngine engine, BoxBrowserViewModel boxes)
{
    private const string Key = "pkforge.pokepark.daycare.v1";
    public const int Capacity = 4;

    // Notebook: Blissey's notebook (ParkNotebook), null in states saved before it existed.
    private sealed record State(ParkStay[] Stays, string[]? Egg, ParkNote[]? Notebook = null);

    private State Load()
    {
        try { return JsonSerializer.Deserialize<State>(Preferences.Default.Get(Key, "")) ?? new([], null); }
        catch (JsonException) { return new([], null); }
    }

    private static void Save(State state) => Preferences.Default.Set(Key, JsonSerializer.Serialize(state));

    public IReadOnlyList<ParkStay> Stays => Load().Stays;

    /// <summary>Blissey's notebook: stays whose EXP couldn't be written when they ended, owed first, then settled ones.</summary>
    public IReadOnlyList<ParkNote> Notebook => Load().Notebook ?? [];

    public bool IsGuest(string id) => Load().Stays.Any(s => s.ResidentId == id);

    /// <summary>The egg Blissey is keeping, if any (the two parents' ids).</summary>
    public (string First, string Second)? Egg => Load().Egg is [var a, var b] ? (a, b) : null;

    public void KeepEgg((string First, string Second)? egg) => Save(Load() with { Egg = egg is { } e ? [e.First, e.Second] : null });

    public static bool WritesBack(PokeparkSettings settings) => settings.DaycareWritesBack;

    /// <summary>What a daycare pace means for a guest, in words.</summary>
    public static string PaceNote(DaycarePace pace) => pace switch
    {
        DaycarePace.Off => "Daycare pace is Off: it won't gain EXP here.",
        DaycarePace.Slow => "It will grow about a level a day.",
        DaycarePace.Gentle => "It will grow about a level every 8 hours.",
        _ => "It will grow a little for every hour it stays."
    };

    /// <summary>The next pace in the cycle Off, Slow, Gentle, Original, then Off again.</summary>
    public static DaycarePace NextPace(DaycarePace pace) => pace switch
    {
        DaycarePace.Off => DaycarePace.Slow,
        DaycarePace.Slow => DaycarePace.Gentle,
        DaycarePace.Gentle => DaycarePace.Original,
        _ => DaycarePace.Off
    };

    /// <summary>Leaves a resident at the daycare.</summary>
    public string CheckIn(ParkPokemon mon)
    {
        var state = Load();
        if (state.Stays.Any(s => s.ResidentId == mon.Id)) return $"{mon.Name} is already at the daycare.";
        if (state.Stays.Length >= Capacity) return $"The daycare is full: Blissey can look after {Capacity} at a time.";
        var source = park.SourceOf(mon);
        Save(state with { Stays = [.. state.Stays, new ParkStay(mon.Id, DateTimeOffset.Now,
            ParkIdentity.ForStay(source is null ? null : ParkIdentity.Of(source.Data, source.Format), mon.Identity, mon.Id))] });
        var rules = source is null ? null : ParkDaycareService.RulesFor(source.Data, source.Format);
        var note = rules switch
        {
            null => "Its save is not open now: open it before you pick it up, so it can take what it learns home.",
            { Grows: true } => PaceNote(park.Settings.DaycarePace),
            { Breeds: true } => "Its game has a Nursery, not a daycare: it won't gain EXP, but it may find a partner.",
            _ => "Its game has no daycare, so this is a holiday: no EXP, no eggs."
        };
        return $"Blissey takes {mon.Name} by the paw and shows it around. {note}";
    }

    /// <summary>What a stay has been worth so far, for the card: hours and the level it would reach.</summary>
    public (TimeSpan Time, ParkDaycareGrowth? Growth) Progress(ParkPokemon mon)
    {
        if (Load().Stays.FirstOrDefault(s => s.ResidentId == mon.Id) is not { } stay) return (TimeSpan.Zero, null);
        var time = DateTimeOffset.Now - stay.Since;
        var source = park.SourceOf(mon);
        return (time, source is null ? null : ParkDaycareService.Preview(source.Data, source.Format,
            ParkDaycareService.ExpForStay(source.Data, source.Format, time, park.Settings.DaycarePace)));
    }

    /// <summary>Picks a resident up. The EXP goes back to the real Pokémon when allowed.</summary>
    public async Task<string> PickUpAsync(ParkPokemon mon)
    {
        var state = Load();
        if (state.Stays.FirstOrDefault(s => s.ResidentId == mon.Id) is not { } stay) return $"{mon.Name} is not at the daycare.";
        var time = DateTimeOffset.Now - stay.Since;
        var stayed = Duration(time);
        var (message, outcome) = await GrowAsync(mon, stay, time, stayed);
        // A refused or failed write keeps the stay: the time isn't lost, and it can be picked up again.
        if (!ParkDaycareService.EndsStay(outcome)) return message;
        var left = Load();
        // A stay whose EXP couldn't be written goes in the notebook, settled once its save is open.
        var notebook = ParkDaycareService.GoesInNotebook(outcome)
            ? ParkNotebook.Add(left.Notebook ?? [], new ParkNote(mon.Id, ParkIdentity.ForStay(stay.Fingerprint, mon.Identity, mon.Id), mon.Name,
                park.SourceOf(mon)?.Format, stay.Since, DateTimeOffset.Now, time.TotalHours, park.Settings.DaycarePace)).ToArray()
            : left.Notebook;
        Save(left with { Stays = left.Stays.Where(s => s.ResidentId != mon.Id).ToArray(), Egg = left.Egg is { } egg && egg.Contains(mon.Id) ? null : left.Egg, Notebook = notebook });
        return message;
    }

    /// <summary>Called when a resident leaves the park: its stay ends with it, nothing written.</summary>
    public void Forget(string id)
    {
        var state = Load();
        Save(state with { Stays = state.Stays.Where(s => s.ResidentId != id).ToArray(), Egg = state.Egg is { } egg && egg.Contains(id) ? null : state.Egg });
    }

    private async Task<(string Message, DaycarePickup Outcome)> GrowAsync(ParkPokemon mon, ParkStay stay, TimeSpan time, string stayed)
    {
        var settings = park.Settings;
        var source = park.SourceOf(mon);
        var hello = $"Blissey brings {mon.Name} back. It stayed {stayed}.";
        var owes = WritesBack(settings) && settings.DaycarePace != DaycarePace.Off;
        if (source is null)
            return owes
                ? ($"{hello} Its save is not open, so Blissey writes its stay in her notebook. Open its save and settle it from her menu.", DaycarePickup.Owed)
                : ($"{hello} Its save is not open, so the time it spent here stays a park memory.", DaycarePickup.ParkMemory);
        if (!ParkDaycareService.WritesBackTo(source.Format))
            return owes
                ? ($"{hello} Its game can't take daycare EXP from PKForge yet, so Blissey writes its stay in her notebook until it can.", DaycarePickup.Owed)
                : ($"{hello} Its game can't take daycare EXP from PKForge yet, so the time it spent here stays a park memory.", DaycarePickup.ParkMemory);
        var rules = ParkDaycareService.RulesFor(source.Data, source.Format);
        var exp = ParkDaycareService.ExpForStay(source.Data, source.Format, time, settings.DaycarePace);
        if (!rules.Grows) return ($"{hello} {(rules.Breeds ? "Its game has a Nursery, which doesn't train" : "Its game has no daycare")}, so it comes back as it was, only happier.", DaycarePickup.NothingToWrite);
        var preview = ParkDaycareService.Preview(source.Data, source.Format, exp);
        var grew = preview.LevelAfter > preview.LevelBefore ? $"from level {preview.LevelBefore} to {preview.LevelAfter}" : $"by {preview.ExpGained} EXP";
        if (preview.ExpGained == 0)
            return (preview.LevelBefore >= 100 ? $"{hello} It is already as strong as it can get."
                : settings.DaycarePace == DaycarePace.Off ? $"{hello} Daycare pace is Off, so it comes back as it was."
                : $"{hello} It wasn't here long enough to grow.", DaycarePickup.NothingToWrite);
        if (!WritesBack(settings)) return ($"{hello} It grew {grew} here. Writing back is off, so that stays in the park.", DaycarePickup.ParkMemory);
        if (!ParkIdentity.IsSameIndividual(ParkIdentity.ForStay(stay.Fingerprint, mon.Identity, mon.Id), source.Data, source.Format))
            return ($"{mon.Name} has stayed {stayed} and grew {grew}, but the Pokémon in its save isn't the one you left. Blissey keeps it here: pick it up again once it's back in its save.", DaycarePickup.NotTheSame);
        return await WriteAsync(mon, source, exp, grew) is not { } failure
            ? ($"{hello} It grew {grew}, and {Updated(source)}.{CatchUp(source, preview)}", DaycarePickup.Written)
            : ($"{mon.Name} has stayed {stayed} and grew {grew}, but {failure} Blissey keeps it here so the time isn't lost.", DaycarePickup.WriteFailed);
    }

    private static string Updated(PokeparkService.ParkSource source) =>
        source.BankId is null ? "its save is updated. A restore point was made first" : "its Bank entry is updated";

    /// <summary>Unbound gets EXP only: a party Pokémon shows its new level after its next battle.</summary>
    private static string CatchUp(PokeparkService.ParkSource source, ParkDaycareGrowth growth) =>
        ParkDaycareService.WritesExpOnly(source.Format) && source.BankId is null && source.Box == -1 && growth.LevelAfter > growth.LevelBefore
            ? " The game shows its new level after its next battle." : "";

    /// <summary>Writes <paramref name="exp"/> to the Pokémon behind <paramref name="source"/>, in its Bank entry or its open
    /// save. Null when written, else why not (a sentence ending in a full stop).</summary>
    private async Task<string?> WriteAsync(ParkPokemon mon, PokeparkService.ParkSource source, uint exp, string grew)
    {
        if (source.BankId is { } id)
        {
            var entry = bank.GetAll().FirstOrDefault(e => e.Id == id);
            if (entry is null || ParkDaycareService.GrowBytes(source.Data, source.Format, exp) is not { } grown) return "its Bank entry could not be updated.";
            bank.Replace(id, grown.Data, engine.TryDescribeEntity(grown.Data, entry.Info.SourceName, source.Format) ?? entry.Info);
            return null;
        }
        var written = await boxes.RunMutationAsync(session =>
        {
            var growth = ParkDaycareService.GrowSlot(session, source.Box, source.Slot, exp);
            return new GenerationOutcome(true, $"{mon.Name} grew to level {growth.LevelAfter} at the Poképark daycare.");
        }, source.Slot, refreshSlot: false, changeDescription: $"Poképark daycare: {mon.Name} grew {grew}", action: SaveAction.EditMon);
        return written ? null : $"the save could not be written: {boxes.Status}";
    }

    /// <summary>The owed notes Blissey can settle now: their Pokémon are in the park with their saves (or Bank entries)
    /// open, in a game PKForge can write, and writing back is on.</summary>
    public IReadOnlyList<(ParkNote Note, ParkPokemon Mon)> Settleable(IEnumerable<ParkPokemon> residents)
    {
        if (!WritesBack(park.Settings)) return [];
        var roster = residents.ToDictionary(r => r.Id);
        return ParkNotebook.Owed(Notebook)
            .Where(n => roster.ContainsKey(n.ResidentId) && park.SourceOf(roster[n.ResidentId]) is { } source && ParkDaycareService.WritesBackTo(source.Format))
            .Select(n => (n, roster[n.ResidentId])).ToArray();
    }

    /// <summary>Settles every note it can: each stay's EXP reckoned on the Pokémon as it is now and written to the same
    /// individual. A note whose Pokémon isn't the one that stayed, or whose write fails, stays owed.</summary>
    public async Task<string> SettleAsync(IEnumerable<ParkPokemon> residents)
    {
        var lines = new List<string>();
        foreach (var (note, mon) in Settleable(residents))
        {
            if (park.SourceOf(mon) is not { } source) continue;
            var when = $"its {Duration(TimeSpan.FromHours(note.Hours))} from {note.Ended.LocalDateTime:d MMM}";
            if (!ParkIdentity.IsSameIndividual(ParkIdentity.ForStay(note.Identity, mon.Identity, mon.Id), source.Data, source.Format))
            {
                lines.Add($"{mon.Name}: the Pokémon in its save isn't the one that stayed, so {when} waits.");
                continue;
            }
            var exp = ParkDaycareService.ExpForStay(source.Data, source.Format, TimeSpan.FromHours(note.Hours), note.Pace);
            var preview = ParkDaycareService.Preview(source.Data, source.Format, exp);
            var grew = preview.LevelAfter > preview.LevelBefore ? $"from level {preview.LevelBefore} to {preview.LevelAfter}" : $"by {preview.ExpGained} EXP";
            if (preview.ExpGained > 0 && await WriteAsync(mon, source, exp, grew) is { } failure)
            {
                lines.Add($"{mon.Name}: {failure} {char.ToUpper(when[0])}{when[1..]} waits.");
                continue;
            }
            var state = Load();
            Save(state with { Notebook = ParkNotebook.Settle(state.Notebook ?? [], note, DateTimeOffset.Now, preview.ExpGained).ToArray() });
            lines.Add(preview.ExpGained > 0
                ? $"{mon.Name} grew {grew} for {when}.{CatchUp(source, preview)}"
                : $"{mon.Name}: {when} was already as much as it could take.");
        }
        return lines.Count == 0 ? "There's nothing Blissey can settle right now." : $"Blissey settles her notebook. {string.Join(" ", lines)}";
    }

    /// <summary>Whether two guests could have an egg, by their real data (false when either can't be read).</summary>
    public bool CanBreed(ParkPokemon a, ParkPokemon b) =>
        park.SourceOf(a) is { } x && park.SourceOf(b) is { } y && ParkDaycareService.CanBreed(x.Data, x.Format, y.Data, y.Format);

    /// <summary>The player takes the egg from Blissey: into the Bank when allowed, otherwise a keepsake.</summary>
    public string TakeEgg(ParkPokemon first, ParkPokemon second, Action<ParkTreasure> keepsake)
    {
        KeepEgg(null);
        var settings = park.Settings;
        if (!WritesBack(settings) || park.SourceOf(first) is not { } a || park.SourceOf(second) is not { } b)
        {
            keepsake(new ParkTreasure($"a daycare egg from {first.Name} and {second.Name}", 3));
            return WritesBack(settings)
                ? $"Blissey hands you the egg. Their saves are not open, so it goes in your bag as a keepsake."
                : $"Blissey hands you the egg. Writing back is off, so it goes in your bag as a keepsake.";
        }
        if (ParkDaycareService.MakeEgg(a.Data, a.Format, b.Data, b.Format, out var why) is not { } egg)
        {
            keepsake(new ParkTreasure($"a daycare egg from {first.Name} and {second.Name}", 3));
            return $"Blissey hands you the egg, but no legal egg fits in their game ({why}). It goes in your bag as a keepsake.";
        }
        var info = engine.TryDescribeEntity(egg.Data, "Poképark daycare", egg.Format);
        if (info is null) return "Blissey hands you the egg, but it could not be read. Nothing was added.";
        var entry = bank.Add(egg.Data, info);
        return $"Blissey hands you the egg, warm from the sun. It's in your Bank, box {entry.Box + 1}, slot {entry.Slot + 1}.";
    }

    private static string Duration(TimeSpan time) => time.TotalHours switch
    {
        < 1 => "less than an hour",
        < 2 => "about an hour",
        < 24 => $"{(int)time.TotalHours} hours",
        < 48 => "a day",
        _ => $"{(int)time.TotalDays} days"
    };

}
