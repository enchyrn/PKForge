using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PKForge.Domain;
using PKForge.Engine;

namespace PKForge.App.Services;

public sealed record ParkPokemon(string Id, int Species, int Form, bool Shiny, string Name, string Source,
    string GameName = "", string TrainerName = "", string Origin = "", string? Identity = null);
public enum PokeparkSource { Both, Bank, Save, Disabled }
public sealed record PokeparkSettings
{
    public PokeparkSource Source { get; init; } = PokeparkSource.Both;
    public bool Random { get; init; } = true;
    public int Count { get; init; } = 6;
    public string[] SelectedIds { get; init; } = [];
    /// <summary>The daycare writes EXP and eggs back to saves and the Bank, where the game allows it.</summary>
    public bool DaycareWritesBack { get; init; } = true;
    /// <summary>How fast a stay at the daycare grows a Pokémon (default: about a level every 8 hours).</summary>
    public DaycarePace DaycarePace { get; init; } = DaycarePace.Gentle;
}

public sealed class PokeparkService(IBankService bank, ISaveSessionService saves, IGameDataService data)
{
    private readonly object _initializationGate = new();
    private const string SettingsKey = "pkforge.pokepark.settings.v1";
    private const string ResidentsKey = "pkforge.pokepark.residents.v1";
    private const string RosterKey = "pkforge.pokepark.roster.v1";
    public PokeparkSettings Settings => Normalize(Read<PokeparkSettings>(SettingsKey) ?? new());

    public IReadOnlyList<ParkPokemon> GetCandidates()
    {
        var result = bank.GetAll().Where(e => e.Info.Species > 0).Select(e =>
            new ParkPokemon($"bank:{e.Id:N}", e.Info.Species, e.Info.Form, e.Info.Shiny,
                Name(e.Info.Nickname, e.Info.Species), $"Bank · Box {e.Box + 1} · Slot {e.Slot + 1}",
                GameName: $"Gen {e.Info.Generation}", TrainerName: "", Origin: e.Info.SourceName)).ToList();
        var session = saves.CurrentSession;
        var document = saves.Current?.Document;
        if (session is null || document is null) return result;
        var documentId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document.DocumentId)))[..16];
        var trainer = session.GetTrainer().Name;
        var gameName = $"Gen {session.Generation}";
        // Include the party as well as PC boxes. Party entries use Box == -1
        // and are valid save-backed Poképark candidates.
        foreach (var slot in session.Snapshot.Slots.Where(s => s.Species is > 0 && !s.IsEgg))
        {
            var species = slot.Species!.Value;
            result.Add(new ParkPokemon($"save:{documentId}:{slot.Box}:{slot.Slot}",
                species, slot.Form, slot.IsShiny,
                string.IsNullOrWhiteSpace(slot.Nickname) ? SpeciesName(species) : slot.Nickname,
                $"{document.DisplayName} · {(slot.Box < 0 ? "Party" : $"Box {slot.Box + 1}")} · Slot {slot.Slot + 1}",
                GameName: gameName, TrainerName: trainer, Origin: document.DisplayName));
        }
        return result;
    }

    /// <summary>The species name, the same table every picker shows.</summary>
    public string SpeciesName(int species) =>
        (uint)species < (uint)data.SpeciesNames.Count ? data.SpeciesNames[species] : $"#{species}";

    private const string InitializedKey = "pkforge.pokepark.initialized.v2";
    private const string AutoFillSuppressedKey = "pkforge.pokepark.autofill-suppressed.v1";
    public const int MaximumResidents = ParkLife.MostResidents;

    /// <summary>Called once at app startup. Opening the park never chooses new residents.</summary>
    public void EnsureInitialized()
    {
        lock (_initializationGate)
        {
            // Home and a deep-linked park can appear nearly together. Only one of
            // them may enumerate/export/hash the open save at a time.
            if (Read<bool>(InitializedKey)) return;
            var roster = LoadRoster();
            if (roster.Count == 0 && Settings.Source != PokeparkSource.Disabled)
            {
                var candidates = GetCandidates().Where(p => Settings.Source == PokeparkSource.Both ||
                    p.Id.StartsWith(Settings.Source == PokeparkSource.Bank ? "bank:" : "save:"));
                roster = ParkSelection.Select(candidates, p => p.Id, true, Settings.Count, []);
            }
            SaveRoster(roster);
        }
    }

    public int AutoFillIfEmpty()
    {
        if (LoadRoster().Count > 0) return 0;
        return FillAutomatically([]);
    }

    public int RefreshAutoFill()
    {
        if (Read<bool>(AutoFillSuppressedKey)) return 0;
        var previous = LoadRoster();
        return FillAutomatically(previous);
    }

    private int FillAutomatically(IReadOnlyList<ParkPokemon> previous)
    {
        if (Read<bool>(AutoFillSuppressedKey)) return 0;
        var settings = Settings;
        if (settings.Source == PokeparkSource.Disabled) return 0;
        var candidates = GetCandidates().Where(p => settings.Source == PokeparkSource.Both ||
            p.Id.StartsWith(settings.Source == PokeparkSource.Bank ? "bank:" : "save:"));
        var available = candidates.ToArray();
        if (available.Length == 0) return 0;
        var fresh = available.Where(p => !previous.Any(old => old.Id == p.Id)).ToArray();
        if (fresh.Length >= Math.Min(settings.Count, MaximumResidents))
            available = fresh;
        // Automatic filling is independent from the old manual-selection
        // list. In combined mode, guarantee representation from both Save and
        // Bank whenever both are available and the requested count allows it.
        var roster = settings.Source == PokeparkSource.Both
            ? ParkSelection.SelectBalanced(available, p => p.Id,
                p => p.Id.StartsWith("save:", StringComparison.Ordinal) ? "save" : "bank",
                settings.Count)
            : ParkSelection.Select(available, p => p.Id, true, settings.Count, []);
        if (roster.Count == 0) return 0;
        SaveRoster(roster);
        return roster.Count;
    }

    public void SaveSettings(PokeparkSettings settings)
    {
        var normalized = Normalize(settings);
        Write(SettingsKey, normalized);
        if (normalized.Source != PokeparkSource.Disabled) Write(AutoFillSuppressedKey, false);
        if (LoadRoster().Count == 0) Write(InitializedKey, false);
    }

    public IReadOnlyList<ParkPokemon> GetSelectableCandidates() => GetCandidates()
        .Concat(LoadRoster()).DistinctBy(p => p.Id).ToArray();

    /// <summary>Persisted visual snapshots survive save switches and source deletion.</summary>
    public IReadOnlyList<ParkPokemon> LoadRoster()
    {
        var roster = Read<ParkPokemon[]>(RosterKey) ?? [];
        if (!Read<bool>(InitializedKey))
        {
            // Upgrade existing explicitly selected visitors before the old random sample.
            var settings = Settings;
            var selected = (Read<ParkPokemon[]>(ResidentsKey) ?? [])
                .Where(p => settings.SelectedIds.Contains(p.Id)).ToArray();
            if (!settings.Random && selected.Length > 0) roster = selected;
        }

        // Auto-fill used to name un-nicknamed residents "Pokémon #25". Heal persisted
        // rosters to the species name so every surface (journal, park, speech bubbles)
        // shows the real name without a re-invite.
        var healed = false;
        roster = roster.Select(p =>
        {
            if (!IsLegacyFallbackName(p.Name, p.Species)) return p;
            healed = true;
            return p with { Name = SpeciesName(p.Species) };
        }).ToArray();
        if (healed) SaveRoster(roster);
        return roster.Where(p => p.Species > 0).DistinctBy(p => p.Id).Take(MaximumResidents).ToArray();
    }

    /// <summary>Matches exactly the old fallback pattern "Pokémon #N" (and its ASCII
    /// spelling), never a real nickname the user could have typed.</summary>
    private static bool IsLegacyFallbackName(string name, int species) =>
        name.Equals($"Pokémon #{species}", StringComparison.OrdinalIgnoreCase) ||
        name.Equals($"Pokemon #{species}", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ParkPokemon> GetLastRoster() => LoadRoster();

    public string AddBankVisitor(Guid id)
    {
        var entry = bank.GetAll().FirstOrDefault(e => e.Id == id);
        if (entry is null || entry.Info.Species <= 0) return "This Pokémon is no longer in the bank.";
        var visitor = new ParkPokemon($"bank:{entry.Id:N}", entry.Info.Species, entry.Info.Form,
            entry.Info.Shiny, Name(entry.Info.Nickname, entry.Info.Species),
            $"Bank · Box {entry.Box + 1} · Slot {entry.Slot + 1}",
            GameName: $"Gen {entry.Info.Generation}", TrainerName: "", Origin: entry.Info.SourceName);
        return AddVisitor(visitor, returning: Departed.Any(p => p.Id == visitor.Id));
    }

    public string AddSaveVisitor(int box, int slot)
    {
        var session = saves.CurrentSession;
        var document = saves.Current?.Document;
        if (session is null || document is null) return "Open a save to send a Pokémon to Poképark.";
        if (!session.Snapshot.Slots.Any(s => s.Box == box && s.Slot == slot && s.Species is > 0))
            return "Select a Pokémon first.";
        if (session.GetMetInfo(box, slot).IsEgg) return "Eggs can visit Poképark after they hatch.";
        var detail = session.ReadEntity(box, slot);
        var documentId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document.DocumentId)))[..16];
        var export = session.ExportSlot(box, slot);
        if (ParkIdentity.Of(export.Data, export.Format) is not { } identity) return "This Pokémon can't be read, so it can't visit Poképark.";
        // Keyed by who it is, not its bytes: playing on, or moving it to another box, doesn't make it a new resident.
        if (LoadRoster().FirstOrDefault(p => IsResident(p, documentId, identity)) is { } already)
            return $"{already.Name} already lives in Poképark.";
        var visitor = new ParkPokemon($"save:{documentId}:{box}:{slot}:{identity}", detail.Species,
            detail.Form, detail.IsShiny, string.IsNullOrWhiteSpace(detail.Nickname) ? detail.SpeciesName : detail.Nickname,
            $"{document.DisplayName} · {(box < 0 ? "Party" : $"Box {box + 1}")} · Slot {slot + 1}",
            GameName: $"Gen {session.Generation}", TrainerName: session.GetTrainer().Name, Origin: document.DisplayName, Identity: identity);
        // One that lived here before comes back under its old id, so its care, bond and friendships are its own again.
        if (Departed.FirstOrDefault(p => IsResident(p, documentId, identity)) is { } back)
            return AddVisitor(visitor with { Id = back.Id }, returning: true);
        return AddVisitor(visitor);
    }

    private readonly Dictionary<string, int> _natures = new();

    private readonly Dictionary<string, DateOnly> _metDates = new();

    /// <summary>The day the player met the resident (Gen 4 and later), from the Bank or the open save.</summary>
    public DateOnly? MetDateOf(ParkPokemon mon)
    {
        lock (_metDates) if (_metDates.TryGetValue(mon.Id, out var known)) return known;
        DateOnly? met = null;
        try
        {
            var parts = mon.Id.Split(':');
            if (parts is ["bank", var guid, ..] && Guid.TryParse(guid, out var id) && bank.GetAll().FirstOrDefault(e => e.Id == id) is { } entry)
                met = PKForge.Engine.EntityBytes.Parse(entry, bank.GetData(id))?.MetDate;
            else if (parts is ["save", var document, var boxText, var slotText, ..] && saves.CurrentSession is { } session &&
                     saves.Current?.Document is { } open &&
                     Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(open.DocumentId)))[..16] == document &&
                     int.TryParse(boxText, out var box) && int.TryParse(slotText, out var slot) &&
                     session.GetMetInfo(box, slot) is { SupportsMetDate: true } info &&
                     DateOnly.TryParse(info.MetDate, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                met = parsed;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or IOException or FormatException)
        {
            // No date to celebrate.
        }
        if (met is { } found) lock (_metDates) _metDates[mon.Id] = found;
        return met;
    }

    /// <summary>A nature's name, in the game data's language.</summary>
    public string NatureName(int nature) => (uint)nature < (uint)data.NatureNames.Count ? data.NatureNames[nature] : $"#{nature}";

    /// <summary>The resident's nature, read from the Bank or from the open save (for its berry
    /// tastes); null when its save is not open.</summary>
    public int? NatureOf(ParkPokemon mon)
    {
        lock (_natures) if (_natures.TryGetValue(mon.Id, out var known)) return known;
        int? nature = null;
        try
        {
            var parts = mon.Id.Split(':');
            if (parts is ["bank", var guid, ..] && Guid.TryParse(guid, out var id) && bank.GetAll().FirstOrDefault(e => e.Id == id) is { } entry)
                nature = PKForge.Engine.EntityBytes.Parse(entry, bank.GetData(id)) is { } pk ? (int)pk.Nature : null;
            else if (parts is ["save", var document, var boxText, var slotText, ..] && saves.CurrentSession is { } session &&
                     saves.Current?.Document is { } open &&
                     Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(open.DocumentId)))[..16] == document &&
                     int.TryParse(boxText, out var box) && int.TryParse(slotText, out var slot) &&
                     session.ReadEntity(box, slot) is { } detail && detail.Species == mon.Species)
                nature = detail.Nature;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or IOException or FormatException)
        {
            // An unreadable entry keeps the taste the park gives it from its id.
        }
        if (nature is { } found) lock (_natures) _natures[mon.Id] = found;
        return nature;
    }

    /// <summary>Whether the save a save resident came from is the one open now.</summary>
    public bool SaveIsOpenFor(ParkPokemon mon) =>
        mon.Id.Split(':') is ["save", var document, ..] && saves.CurrentSession is not null &&
        saves.Current?.Document is { } open &&
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(open.DocumentId)))[..16] == document;

    /// <summary>Where a resident's real data lives: a Bank entry, or a slot of the open save.</summary>
    public sealed record ParkSource(byte[] Data, string? Format, Guid? BankId, int Box, int Slot, string SourceName);

    /// <summary>The resident's real data, from the Bank or from the open save (null when its save is not open).</summary>
    public ParkSource? SourceOf(ParkPokemon mon)
    {
        try
        {
            var parts = mon.Id.Split(':');
            if (parts is ["bank", var guid, ..] && Guid.TryParse(guid, out var id) && bank.GetAll().FirstOrDefault(e => e.Id == id) is { } entry)
                return new(bank.GetData(id), entry.Info.Format, id, entry.Box, entry.Slot, entry.Info.SourceName);
            if (parts is ["save", var document, var boxText, var slotText, ..] && saves.CurrentSession is { } session &&
                saves.Current?.Document is { } open &&
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(open.DocumentId)))[..16] == document &&
                int.TryParse(boxText, out var box) && int.TryParse(slotText, out var slot))
            {
                var identity = mon.Identity ?? ParkIdentity.InResidentId(mon.Id);
                if (identity is null)
                {
                    // A resident from before identities, not yet given one: the Pokémon of its species in the slot it came from.
                    if (session.ReadEntity(box, slot) is { } detail && detail.Species == mon.Species)
                    {
                        var export = session.ExportSlot(box, slot);
                        return new(export.Data, export.Format, null, box, slot, open.DisplayName);
                    }
                }
                else
                {
                    // Where it was sent from first, else wherever it has been moved to in the save (evolved or not).
                    var places = session.Snapshot.Slots.Where(s => s.Species is > 0)
                        .OrderBy(s => s.Box == box && s.Slot == slot ? 0 : 1);
                    foreach (var place in places)
                    {
                        var export = session.ExportSlot(place.Box, place.Slot);
                        if (ParkIdentity.IsSameIndividual(identity, export.Data, export.Format))
                            return new(export.Data, export.Format, null, place.Box, place.Slot, open.DisplayName);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or IOException or FormatException)
        {
            // Unreadable: the park keeps it as it is.
        }
        return null;
    }

    /// <summary>Takes a resident out of the park. With <paramref name="remember"/> (it left; it wasn't
    /// released or deleted) the park remembers it, so sending it back restores the same resident.</summary>
    public bool RemoveVisitor(string id, bool remember = false)
    {
        var roster = LoadRoster();
        if (roster.FirstOrDefault(p => p.Id == id) is not { } leaving) return false;
        if (remember) Write(DepartedKey, ParkDepartures.Remember(Departed, leaving, p => p.Id));
        SaveRoster(roster.Where(p => p.Id != id).ToArray());
        Write(AutoFillSuppressedKey, true);
        return true;
    }

    public int RemoveAllVisitors(bool remember = false)
    {
        var roster = LoadRoster();
        if (remember)
            Write(DepartedKey, roster.Aggregate(Departed, (departed, leaving) => ParkDepartures.Remember(departed, leaving, p => p.Id)));
        if (roster.Count > 0) SaveRoster([]);
        Write(AutoFillSuppressedKey, true);
        return roster.Count;
    }

    /// <summary>Gives residents that have no identity yet (sent before identities were kept, or
    /// auto-filled) the identity of the Pokémon they came from, once their save is open: from then on
    /// they are followed when moved or evolved, and recognised when they come back.</summary>
    public int RememberIdentities()
    {
        var learned = 0;
        var roster = LoadRoster().Select(p =>
        {
            if (p.Identity is not null || !p.Id.StartsWith("save:", StringComparison.Ordinal) ||
                ParkIdentity.InResidentId(p.Id) is not null ||
                SourceOf(p) is not { } source || ParkIdentity.Of(source.Data, source.Format) is not { } identity) return p;
            learned++;
            return p with { Identity = identity };
        }).ToArray();
        if (learned > 0) SaveRoster(roster);
        return learned;
    }

    /// <summary>Whether <paramref name="resident"/> is that Pokémon of that save: by its identity, or, for one
    /// sent before identities were kept, by the Pokémon its slot still holds.</summary>
    private bool IsResident(ParkPokemon resident, string documentId, string identity) =>
        resident.Id.StartsWith($"save:{documentId}:", StringComparison.Ordinal) &&
        ((resident.Identity ?? ParkIdentity.InResidentId(resident.Id)) is { } known
            ? known == identity
            : SourceOf(resident) is { } source && ParkIdentity.Of(source.Data, source.Format) == identity);

    private const string DepartedKey = "pkforge.pokepark.departed.v1";
    private static IReadOnlyList<ParkPokemon> Departed => Read<ParkPokemon[]>(DepartedKey) ?? [];

    private string AddVisitor(ParkPokemon pokemon, bool returning = false)
    {
        var roster = LoadRoster();
        if (roster.Any(p => p.Id == pokemon.Id)) return $"{pokemon.Name} already lives in Poképark.";
        if (roster.Count >= MaximumResidents)
            return $"Poképark has {MaximumResidents} residents. Open the park and remove a resident before adding another.";
        Write(AutoFillSuppressedKey, false);
        SaveRoster(roster.Append(pokemon).ToArray());
        if (!returning) return $"{pokemon.Name} now lives in Poképark! Your original Pokémon stays in its save or bank.";
        Write(DepartedKey, ParkDepartures.Forget(Departed, pokemon.Id, p => p.Id));
        return $"{pokemon.Name} is back in Poképark, and remembers its time here! Your original Pokémon stays in its save or bank.";
    }

    private static void SaveRoster(IReadOnlyList<ParkPokemon> roster)
    {
        Write(RosterKey, roster);
        Write(InitializedKey, true);
    }

    private string Name(string name, int species) => string.IsNullOrWhiteSpace(name) ? SpeciesName(species) : name;
    private static PokeparkSettings Normalize(PokeparkSettings settings) => settings with
    {
        Source = Enum.IsDefined(settings.Source) ? settings.Source : PokeparkSource.Both,
        DaycarePace = Enum.IsDefined(settings.DaycarePace) ? settings.DaycarePace : DaycarePace.Gentle,
        Count = Math.Clamp(settings.Count, 1, MaximumResidents),
        SelectedIds = (settings.SelectedIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Take(MaximumResidents).ToArray()
    };
    private static T? Read<T>(string key)
    {
        try { return JsonSerializer.Deserialize<T>(Preferences.Default.Get(key, "null")); }
        catch (JsonException) { return default; }
    }
    private static void Write<T>(string key, T value) => Preferences.Default.Set(key, JsonSerializer.Serialize(value));
}
