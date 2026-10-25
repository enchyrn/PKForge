using PKHeX.Core;
using PKForge.Domain;
using PKForge.Engine;
using Xunit;

namespace PKForge.Engine.Tests;

public sealed class ParkDaycareServiceTests
{
    private static PKM Make(int generation, int species, int level)
    {
        var engine = new SaveEngine();
        using var session = engine.OpenBlankSession(generation);
        var made = new LegalizerService().GenerateData(session, new GenerationRequest(species, level, false, null, null, null, null))
            ?? throw new InvalidOperationException($"Could not generate #{species}.");
        return EntityBytes.Parse(made.Data, made.Info.Format) ?? throw new InvalidOperationException("Could not read the generated Pokémon.");
    }

    [Theory]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    [InlineData(6, 4)]
    [InlineData(8, 4)]
    [InlineData(7, 4)]
    [InlineData(9, 909)]
    public void Ditto_and_a_starter_leave_a_legal_egg_of_the_line(int generation, int species)
    {
        var ditto = Make(generation, 132, 30);
        var parent = Make(generation, species, 30);
        Assert.True(ParkDaycareService.CanBreed(ditto, parent));
        var egg = ParkDaycareService.MakeEgg(ditto, parent, out var why);
        Assert.True(egg is not null, why);
        var pk = EntityBytes.Parse(egg!.Data, egg.Format)!;
        Assert.True(pk.IsEgg);
        Assert.Equal(species, pk.Species);
        if (pk.Format > 3) Assert.Equal(parent.OriginalTrainerName, pk.OriginalTrainerName); // Gen 3 eggs take their trainer at hatching
        Assert.True(new LegalityAnalysis(pk).Valid, new LegalityAnalysis(pk).Report());
    }

    [Fact]
    public void An_evolved_mother_leaves_the_base_of_her_line()
    {
        var egg = ParkDaycareService.MakeEgg(Make(4, 132, 40), Make(4, 6, 40), out var why);
        Assert.True(egg is not null, why);
        Assert.Equal(4, egg!.Species);
    }

    [Fact]
    public void Who_cannot_breed()
    {
        Assert.False(ParkDaycareService.CanBreed(Make(4, 132, 30), Make(4, 132, 30)));
        Assert.False(ParkDaycareService.CanBreed(Make(4, 132, 50), Make(4, 150, 70)));
        Assert.False(ParkDaycareService.CanBreed(Make(1, 132, 30), Make(1, 4, 30)));
    }

    [Fact]
    public void Each_game_keeps_its_own_daycare_rules()
    {
        Assert.Equal(new ParkDaycareRules(true, false), ParkDaycareService.RulesFor(Make(1, 25, 10)));
        Assert.Equal(new ParkDaycareRules(true, true), ParkDaycareService.RulesFor(Make(4, 25, 10)));
        Assert.Equal(new ParkDaycareRules(false, true), ParkDaycareService.RulesFor(Make(9, 25, 10)));
    }

    [Fact]
    public void EXP_is_added_exactly_and_stays_legal()
    {
        var pikachu = Make(4, 25, 10);
        var before = pikachu.EXP;
        var data = new byte[pikachu.SIZE_PARTY];
        pikachu.WriteDecryptedDataParty(data);
        var grown = ParkDaycareService.GrowBytes(data, EntityBytes.FormatOf(pikachu), 5000);
        Assert.NotNull(grown);
        var after = EntityBytes.Parse(grown!.Value.Data, EntityBytes.FormatOf(pikachu))!;
        Assert.Equal(before + 5000, after.EXP);
        Assert.Equal(grown.Value.Growth.LevelAfter, after.CurrentLevel);
        Assert.True(after.CurrentLevel > 10);
        Assert.Equal(after.CurrentLevel, after.Stat_Level);
        Assert.True(new LegalityAnalysis(after).Valid, new LegalityAnalysis(after).Report());
    }

    [Fact]
    public void Growth_stops_at_level_100_and_never_in_nurseries()
    {
        var old = Make(4, 25, 99);
        Assert.Equal(100, ParkDaycareService.Preview(old, uint.MaxValue / 2).LevelAfter);
        Assert.Equal(0u, ParkDaycareService.Preview(Make(9, 25, 10), 5000).ExpGained);
    }

    // A Gen 3 Torchic (Medium Slow) set to the exact start of a level.
    private static PKM TorchicAt(int level)
    {
        var pk = Make(3, 255, level);
        pk.EXP = Experience.GetEXP((byte)level, pk.PersonalInfo.EXPGrowth);
        return pk;
    }

    private static uint LevelSpan(PKM pk, int level) =>
        Experience.GetEXP((byte)(level + 1), pk.PersonalInfo.EXPGrowth) - Experience.GetEXP((byte)level, pk.PersonalInfo.EXPGrowth);

    [Fact]
    public void Gentle_takes_8_hours_a_level()
    {
        var pk = TorchicAt(5);
        Assert.Equal(6, ParkDaycareService.Preview(pk, ParkDaycareService.ExpForStay(pk, TimeSpan.FromHours(8), DaycarePace.Gentle)).LevelAfter);
        Assert.Equal(7, ParkDaycareService.Preview(pk, ParkDaycareService.ExpForStay(pk, TimeSpan.FromHours(16), DaycarePace.Gentle)).LevelAfter);
    }

    [Fact]
    public void Gentle_part_way_through_a_level_gives_part_of_its_EXP()
    {
        var pk = TorchicAt(5);
        var exp = ParkDaycareService.ExpForStay(pk, TimeSpan.FromHours(4), DaycarePace.Gentle);
        Assert.Equal((uint)Math.Floor(LevelSpan(pk, 5) / 2.0), exp);
        Assert.Equal(5, ParkDaycareService.Preview(pk, exp).LevelAfter);
    }

    [Fact]
    public void Slow_takes_24_hours_a_level()
    {
        var pk = TorchicAt(5);
        Assert.Equal(6, ParkDaycareService.Preview(pk, ParkDaycareService.ExpForStay(pk, TimeSpan.FromHours(24), DaycarePace.Slow)).LevelAfter);
    }

    [Fact]
    public void Off_gives_nothing_and_Original_gives_500_an_hour()
    {
        var pk = TorchicAt(5);
        Assert.Equal(0u, ParkDaycareService.ExpForStay(pk, TimeSpan.FromHours(100), DaycarePace.Off));
        Assert.Equal(1000u, ParkDaycareService.ExpForStay(pk, TimeSpan.FromHours(2), DaycarePace.Original));
    }

    [Theory]
    [InlineData(DaycarePace.Off)]
    [InlineData(DaycarePace.Slow)]
    [InlineData(DaycarePace.Gentle)]
    [InlineData(DaycarePace.Original)]
    public void Level_100_and_negative_stays_give_nothing(DaycarePace pace)
    {
        Assert.Equal(0u, ParkDaycareService.ExpForStay(Make(3, 255, 100), TimeSpan.FromHours(100), pace));
        Assert.Equal(0u, ParkDaycareService.ExpForStay(TorchicAt(5), TimeSpan.FromHours(-5), pace));
    }

    [Fact]
    public void A_nursery_game_gives_no_EXP_at_any_pace()
    {
        var pk = Make(8, 25, 10);
        Assert.Equal(0u, ParkDaycareService.ExpForStay(pk, TimeSpan.FromHours(100), DaycarePace.Gentle));
    }

    [Fact]
    public void A_long_stay_stops_at_level_100_and_bytes_overload_reads_the_data()
    {
        var pk = TorchicAt(98);
        var exp = ParkDaycareService.ExpForStay(pk, TimeSpan.FromHours(10_000), DaycarePace.Gentle);
        Assert.Equal(100, ParkDaycareService.Preview(pk, exp).LevelAfter);
        var data = new byte[pk.SIZE_PARTY];
        pk.WriteDecryptedDataParty(data);
        Assert.Equal(exp, ParkDaycareService.ExpForStay(data, EntityBytes.FormatOf(pk), TimeSpan.FromHours(10_000), DaycarePace.Gentle));
        Assert.Equal(0u, ParkDaycareService.ExpForStay(new byte[3], null, TimeSpan.FromHours(8), DaycarePace.Gentle));
    }
}
