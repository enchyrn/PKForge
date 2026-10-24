using PKForge.Domain;
using PKHeX.Core;

namespace PKForge.Engine;

/// <summary>What the in-game daycare of a Pokémon's game allows: gaining EXP, laying eggs.</summary>
public sealed record ParkDaycareRules(bool Grows, bool Breeds);

/// <summary>EXP gained at the Poképark daycare, and the levels it was worth.</summary>
public sealed record ParkDaycareGrowth(int LevelBefore, int LevelAfter, uint ExpGained);

/// <summary>A legal egg from the Poképark daycare, ready for the Bank.</summary>
public sealed record ParkDaycareEgg(byte[] Data, string Format, int Species, int Form);

/// <summary>How fast the Poképark daycare grows a Pokémon: not at all, a level per 24 h or per 8 h, or a flat 500 EXP an hour.</summary>
public enum DaycarePace { Off, Slow, Gentle, Original }

/// <summary>
/// The Poképark daycare against real Pokémon data, following each game's own daycare: Gen 1–7
/// daycares give EXP (one point a step in the games, here a level or a flat amount per hour depending on the <see cref="DaycarePace"/>), the
/// Nurseries of Sword/Shield, Brilliant Diamond/Shining Pearl and Scarlet/Violet only give eggs,
/// and Let's Go and Legends have neither. Levels are written as exact EXP, stats recalculated,
/// moves and evolution left alone (as in the games). Eggs are PKHeX egg encounters, made for the
/// trainer who left the mother and checked for legality.
/// </summary>
public static class ParkDaycareService
{
    public static ParkDaycareRules RulesFor(PKM pk) => pk.Context switch
    {
        EntityContext.Gen1 => new(true, false),
        EntityContext.Gen2 or EntityContext.Gen3 or EntityContext.Gen4 or EntityContext.Gen5 or EntityContext.Gen6 or EntityContext.Gen7 => new(true, true),
        EntityContext.Gen8 or EntityContext.Gen8b or EntityContext.Gen9 => new(false, true),
        _ => new(false, false)
    };

    public static ParkDaycareRules RulesFor(byte[] data, string? format) =>
        EntityBytes.Parse(data, format) is { } pk ? RulesFor(pk) : new(false, false);

    /// <summary>The EXP a stay of <paramref name="stay"/> is worth at <paramref name="pace"/>: 0 for an egg, level 100, a
    /// daycare that doesn't grow, or a negative stay. <see cref="DaycarePace.Original"/> is a flat 500 EXP an hour; Slow and
    /// Gentle spend the time a level at a time (24 h or 8 h each), the part-way level at its own rate, until level 100.</summary>
    public static uint ExpForStay(PKM pk, TimeSpan stay, DaycarePace pace)
    {
        if (pace == DaycarePace.Off || stay <= TimeSpan.Zero) return 0;
        if (!RulesFor(pk).Grows || pk.IsEgg) return 0;
        var growth = pk.PersonalInfo.EXPGrowth;
        var level = Experience.GetLevel(pk.EXP, growth);
        if (level >= Experience.MaxLevel) return 0;
        if (pace == DaycarePace.Original) return (uint)Math.Min(uint.MaxValue / 4, stay.TotalHours * 500);
        var hoursPerLevel = pace == DaycarePace.Slow ? 24.0 : 8.0;
        double hours = stay.TotalHours, total = 0, at = pk.EXP;
        for (; level < Experience.MaxLevel && hours > 0; level++)
        {
            double start = Experience.GetEXP((byte)level, growth), next = Experience.GetEXP((byte)(level + 1), growth);
            var left = next - at;
            var hoursToFinish = hoursPerLevel * left / (next - start);
            if (hours < hoursToFinish) { total += hours * (next - start) / hoursPerLevel; break; }
            total += left;
            at = next;
            hours -= hoursToFinish;
        }
        return (uint)Math.Floor(total);
    }

    public static uint ExpForStay(byte[] data, string? format, TimeSpan stay, DaycarePace pace) =>
        EntityBytes.Parse(data, format) is { } pk ? ExpForStay(pk, stay, pace) : 0;

    /// <summary>The growth <paramref name="exp"/> EXP would bring, capped at level 100.</summary>
    public static ParkDaycareGrowth Preview(PKM pk, uint exp)
    {
        if (!RulesFor(pk).Grows || pk.IsEgg) return new(pk.CurrentLevel, pk.CurrentLevel, 0);
        var growth = pk.PersonalInfo.EXPGrowth;
        var max = Experience.GetEXP(Experience.MaxLevel, growth);
        var after = Math.Min(max, pk.EXP + exp);
        return new(pk.CurrentLevel, Experience.GetLevel(after, growth), after - pk.EXP);
    }

    public static ParkDaycareGrowth Preview(byte[] data, string? format, uint exp) =>
        EntityBytes.Parse(data, format) is { } pk ? Preview(pk, exp) : new(0, 0, 0);

    /// <summary>Adds the EXP to a save slot, surgically (no dex or record side effects).</summary>
    public static ParkDaycareGrowth GrowSlot(ISaveEngineSession session, int box, int slot, uint exp)
    {
        if (session is not SaveEngineSession engine) throw new NotSupportedException("The daycare can only write to PKHeX-backed saves.");
        var pk = engine.GetEntity(box, slot);
        if (pk.Species == 0) throw new InvalidOperationException("That slot is empty now.");
        var growth = Grow(pk, exp);
        if (growth.ExpGained == 0) return growth;
        pk.RefreshChecksum();
        if (box == -1) engine.SaveFile.SetPartySlotAtIndex(pk, slot, EntityImportSettings.None);
        else engine.SaveFile.SetBoxSlotAtIndex(pk, box, slot, EntityImportSettings.None);
        return growth;
    }

    /// <summary>Adds the EXP to a Bank Pokémon's bytes.</summary>
    public static (byte[] Data, ParkDaycareGrowth Growth)? GrowBytes(byte[] data, string? format, uint exp)
    {
        if (EntityBytes.Parse(data, format) is not { } pk) return null;
        var growth = Grow(pk, exp);
        pk.RefreshChecksum();
        return (Bytes(pk), growth);
    }

    private static byte[] Bytes(PKM pk)
    {
        var data = new byte[pk.SIZE_PARTY];
        pk.WriteDecryptedDataParty(data);
        return data;
    }

    private static ParkDaycareGrowth Grow(PKM pk, uint exp)
    {
        var preview = Preview(pk, exp);
        if (preview.ExpGained == 0) return preview;
        pk.EXP += preview.ExpGained;
        pk.Stat_Level = (byte)preview.LevelAfter;
        pk.ResetPartyStats();
        return preview;
    }

    /// <summary>Whether two Pokémon could have an egg at a daycare: no eggs, no Undiscovered group,
    /// Ditto with anyone but Ditto, otherwise a male and a female sharing an egg group, and a game
    /// whose daycare gives eggs.</summary>
    public static bool CanBreed(PKM a, PKM b)
    {
        if (a.IsEgg || b.IsEgg || !RulesFor(a).Breeds || !RulesFor(b).Breeds) return false;
        if (a.PersonalInfo is not IPersonalEgg ea || b.PersonalInfo is not IPersonalEgg eb) return false;
        const int undiscovered = 15, ditto = 132;
        if (ea.EggGroup1 == undiscovered || eb.EggGroup1 == undiscovered) return false;
        if (a.Species == ditto || b.Species == ditto) return a.Species != b.Species;
        if (a.Gender == 2 || b.Gender == 2 || a.Gender == b.Gender) return false;
        return ea.EggGroup1 == eb.EggGroup1 || ea.EggGroup1 == eb.EggGroup2 || ea.EggGroup2 == eb.EggGroup1 || ea.EggGroup2 == eb.EggGroup2;
    }

    public static bool CanBreed(byte[] a, string? formatA, byte[] b, string? formatB) =>
        EntityBytes.Parse(a, formatA) is { } x && EntityBytes.Parse(b, formatB) is { } y && CanBreed(x, y);

    /// <summary>The egg two parents leave: the base of the mother's line (the non-Ditto parent, or
    /// the female), in the mother's game, for the trainer who left her. Null when no legal egg
    /// encounter fits, with the reason.</summary>
    public static ParkDaycareEgg? MakeEgg(PKM a, PKM b, out string why)
    {
        why = "";
        if (!CanBreed(a, b)) { why = "These two can't have an egg together."; return null; }
        var mother = a.Species == 132 ? b : b.Species == 132 ? a : a.Gender == 1 ? a : b;
        var context = mother.Context;
        var version = new SimpleTrainerInfo(mother.Version).Context == context ? mother.Version : context.GetSingleGameVersion();
        var trainer = new SimpleTrainerInfo(version)
        {
            OT = mother.OriginalTrainerName, TID16 = mother.TID16, SID16 = mother.SID16,
            Gender = mother.OriginalTrainerGender, Language = mother.Language > 0 ? mother.Language : (int)LanguageID.English
        };
        var (species, form) = EvolutionTree.GetEvolutionTree(context).GetBaseSpeciesForm(mother.Species, mother.Form);
        if (!Breeding.CanHatchAsEgg(species, form, context)) { why = "Nothing can hatch from this line in its game."; return null; }
        var template = EntityBlank.GetBlank(trainer);
        template.Species = species;
        template.Form = form;
        template.CurrentLevel = 1;
        template.Version = version;
        template.Language = trainer.Language;
        var report = "";
        foreach (var encounter in EncounterMovesetGenerator.GenerateEncounters(template, trainer, ReadOnlyMemory<ushort>.Empty, version).Where(e => e.IsEgg).Take(4))
        {
            var egg = encounter.ConvertToPKM(trainer, EncounterCriteria.Unrestricted);
            Unhatched(egg);
            var legality = new LegalityAnalysis(egg);
            if (!legality.Valid) { report = legality.Report(); continue; }
            egg.RefreshChecksum();
            return new ParkDaycareEgg(Bytes(egg), EntityBytes.FormatOf(egg), egg.Species, egg.Form);
        }
        why = report.Length == 0 ? "No egg of this line can be found in its game." : "No legal egg could be made for this pair: " + report;
        return null;
    }

    // An egg still in the bag, as the daycare hands it over (the same steps as AutoMod's egg generator).
    private static void Unhatched(PKM egg)
    {
        egg.IsEgg = true;
        egg.CurrentFriendship = (byte)EggStateLegality.GetMinimumEggHatchCycles(egg);
        if (egg.Format >= 4) egg.MetLocation = LocationEdits.GetNoneLocation(egg);
        egg.IsNicknamed = EggStateLegality.IsNicknameFlagSet(egg);
        egg.Nickname = SpeciesName.GetEggName(egg.Language, egg.Format);
        // Not met yet: no met date (from Gen 4 on, a met date goes with a met place).
        if (egg.Format >= 4) egg.MetDate = null;
        if (egg.Format >= 6) egg.ClearMemories();
        if (egg is PK9) egg.Version = 0;
    }

    public static ParkDaycareEgg? MakeEgg(byte[] a, string? formatA, byte[] b, string? formatB, out string why)
    {
        why = "These Pokémon could not be read.";
        return EntityBytes.Parse(a, formatA) is { } x && EntityBytes.Parse(b, formatB) is { } y ? MakeEgg(x, y, out why) : null;
    }
}
