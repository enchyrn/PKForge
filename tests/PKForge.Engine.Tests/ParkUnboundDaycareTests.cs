using System.Buffers.Binary;
using PKForge.Engine;
using PKForge.Engine.RadicalRed;
using PKForge.Engine.Unbound;
using Xunit;

namespace PKForge.Engine.Tests;

/// <summary>The daycare on Unbound's own terms: its growth curves, EXP only, written through its save code.</summary>
public sealed class ParkUnboundDaycareTests
{
    private const int Larvitar = 246; // Medium Slow in Unbound (Slow in the official games)

    private static byte[] Record(uint exp, int species = Larvitar, bool egg = false)
    {
        var bytes = new byte[RadicalRedFormat.PcMonSize];
        var mon = new UnboundMon(bytes, 0, party: false) { Pid = 0xA7AF64E9, Otid = 0x1234ABCD, Species = species, Experience = exp };
        mon.IVs = [10, 10, 10, 10, 10, 10];
        if (egg) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x36), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x36)) | 0x4000_0000u);
        return bytes;
    }

    [Fact]
    public void Unbound_has_a_daycare_that_trains_but_no_eggs_from_the_park()
    {
        var rules = ParkDaycareService.RulesFor(Record(807), CfruEntity.Unbound);
        Assert.True(rules.Grows);
        Assert.False(rules.Breeds);
    }

    [Fact]
    public void A_stay_is_reckoned_on_Unbound_s_own_curve() =>
        // 807 EXP is level 11 on Medium Slow; 17 h at Gentle (8 h a level) = 166 + 288 + 142.
        Assert.Equal(596u, ParkDaycareService.ExpForStay(Record(807), CfruEntity.Unbound, TimeSpan.FromHours(17), DaycarePace.Gentle));

    [Fact]
    public void The_preview_reads_levels_on_Unbound_s_curve()
    {
        var preview = ParkDaycareService.Preview(Record(807), CfruEntity.Unbound, 596);
        Assert.Equal((11, 13, 596u), (preview.LevelBefore, preview.LevelAfter, preview.ExpGained));
    }

    [Fact]
    public void Growing_stops_at_level_100()
    {
        var max = UnboundData.ExperienceAtLevel(UnboundData.GrowthRateFor(Larvitar), 100);
        var grown = ParkDaycareService.GrowBytes(Record(max - 10), CfruEntity.Unbound, 500)!.Value;
        Assert.Equal(10u, grown.Growth.ExpGained);
        Assert.Equal(max, new UnboundMon(grown.Data, 0, party: false).Experience);
    }

    [Fact]
    public void An_egg_earns_nothing() =>
        Assert.Equal(0u, ParkDaycareService.ExpForStay(Record(807, egg: true), CfruEntity.Unbound, TimeSpan.FromHours(17), DaycarePace.Gentle));

    [Fact]
    public void A_Bank_record_grows_its_EXP_and_stays_the_same_Pokemon()
    {
        var before = Record(807);
        var grown = ParkDaycareService.GrowBytes(before, CfruEntity.Unbound, 596)!.Value;
        Assert.Equal(1403u, new UnboundMon(grown.Data, 0, party: false).Experience);
        Assert.True(ParkIdentity.IsSameIndividual(ParkIdentity.Of(before, CfruEntity.Unbound), grown.Data, CfruEntity.Unbound));
    }
}

/// <summary>Against a real Unbound save, kept out of the repo (.local-testdata/unbound-fork-tyran.srm, the owner's
/// own: Tyran, a Larvitar, leads the party at 807 EXP). Skipped where the file isn't there, as on CI.</summary>
public sealed class ParkUnboundWriteBackTests
{
    private static byte[]? Save()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PKForge.sln"))) directory = directory.Parent;
        var path = directory is null ? null : Path.Combine(directory.FullName, ".local-testdata", "unbound-fork-tyran.srm");
        return path is not null && File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static ushort Checksum(ReadOnlySpan<byte> sector)
    {
        uint sum = 0;
        for (var i = 0; i < 0xFF0; i += 4) sum += BinaryPrimitives.ReadUInt32LittleEndian(sector[i..]);
        return (ushort)((sum >> 16) + (sum & 0xFFFF));
    }

    /// <summary>Every sector holding section 1 (the party): its offset, save counter and whether its checksum holds.</summary>
    private static List<(int Offset, uint Counter, bool Valid)> PartySectors(byte[] save)
    {
        var found = new List<(int, uint, bool)>();
        for (var o = 0; o + 0x1000 <= 0x1C000; o += 0x1000)
            if (BinaryPrimitives.ReadUInt16LittleEndian(save.AsSpan(o + 0xFF4)) == 1)
                found.Add((o, BinaryPrimitives.ReadUInt32LittleEndian(save.AsSpan(o + 0xFFC)),
                    Checksum(save.AsSpan(o, 0x1000)) == BinaryPrimitives.ReadUInt16LittleEndian(save.AsSpan(o + 0xFF6))));
        return found;
    }

    [Fact]
    public void The_daycare_writes_EXP_only_and_the_game_s_checks_still_pass()
    {
        if (Save() is not { } bytes) return;
        using var session = new UnboundEngineSession(bytes);
        var others = Enumerable.Range(1, 5).Select(s => session.TryExport(-1, s)).ToArray();

        var growth = ParkDaycareService.GrowSlot(session, -1, 0, 596);

        Assert.Equal((11, 13, 596u), (growth.LevelBefore, growth.LevelAfter, growth.ExpGained));
        var written = session.Serialize().ToArray();
        using var reopened = new UnboundEngineSession(written);
        var tyran = new UnboundMon(reopened.ExportSlot(-1, 0).Data.ToArray(), 0, party: false);
        Assert.Equal(("Tyran", 1403u), (tyran.Nickname, tyran.Experience));
        Assert.Equal(others, Enumerable.Range(1, 5).Select(s => reopened.TryExport(-1, s)).ToArray());
        Assert.All(PartySectors(written), s => Assert.True(s.Valid));
    }

    [Fact]
    public void A_party_edit_leaves_the_older_save_slot_s_counter_as_the_game_wrote_it()
    {
        if (Save() is not { } bytes) return;
        var counters = PartySectors(bytes).Select(s => (s.Offset, s.Counter)).ToArray();
        using var session = new UnboundEngineSession(bytes);
        ParkDaycareService.GrowSlot(session, -1, 0, 596);
        Assert.Equal(counters, PartySectors(session.Serialize().ToArray()).Select(s => (s.Offset, s.Counter)).ToArray());
    }
}

internal static class UnboundSessionTestExtensions
{
    public static byte[]? TryExport(this UnboundEngineSession session, int box, int slot)
    {
        try { return session.ExportSlot(box, slot).Data.ToArray(); }
        catch (InvalidOperationException) { return null; }
    }
}

public sealed class ParkExpOnlyTests
{
    [Fact]
    public void Only_Unbound_gets_EXP_only()
    {
        Assert.True(ParkDaycareService.WritesExpOnly(PKForge.Engine.RadicalRed.CfruEntity.Unbound));
        Assert.False(ParkDaycareService.WritesExpOnly("PK3"));
        Assert.False(ParkDaycareService.WritesExpOnly(null));
    }
}
