using System.Text;
using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>The park keepsake: the Poképark and the Bank, kept outside the app so a reinstall or a new build can bring them back.</summary>
public sealed class ParkKeepsakeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static ParkKeepsake Sample(DateTimeOffset? at = null) => new(
        ParkKeepsakeFormat.CurrentVersion, at ?? Noon, "3.1.1-fork.7",
        [new("pkforge.pokepark.residents.v1", "string", "[{\"id\":\"bank:1\"}]"), new("pkforge.pokepark.party.save:a", "int", "2026")],
        [new("index.json", Convert.ToBase64String("{}"u8.ToArray())), new("data/1.bin", Convert.ToBase64String([1, 2, 3]))],
        Residents: 2, BankEntries: 1);

    [Fact]
    public void A_keepsake_reads_back_as_it_was_written()
    {
        var read = ParkKeepsakeFormat.Read(ParkKeepsakeFormat.Write(Sample()));
        Assert.NotNull(read);
        Assert.Equal(Sample().Preferences, read!.Preferences);
        Assert.Equal(Sample().Bank, read.Bank);
        Assert.Equal((Noon, 2, 1), (read.SavedAt, read.Residents, read.BankEntries));
    }

    [Fact]
    public void A_copy_cut_off_or_changed_is_ignored()
    {
        var bytes = ParkKeepsakeFormat.Write(Sample());
        Assert.Null(ParkKeepsakeFormat.Read(bytes.AsSpan(0, bytes.Length / 2).ToArray()));
        var tampered = Encoding.UTF8.GetString(bytes).Replace("2026", "2027");
        Assert.Null(ParkKeepsakeFormat.Read(Encoding.UTF8.GetBytes(tampered)));
        Assert.Null(ParkKeepsakeFormat.Read([]));
    }

    [Fact]
    public void A_keepsake_from_a_newer_format_isn_t_guessed_at()
    {
        var newer = Encoding.UTF8.GetString(ParkKeepsakeFormat.Write(Sample())).Replace("\"version\":1", "\"version\":99");
        Assert.Null(ParkKeepsakeFormat.Read(Encoding.UTF8.GetBytes(newer)));
    }

    [Fact]
    public void Only_the_park_s_own_preferences_are_kept() =>
        Assert.Equal(["pkforge.pokepark.care.v1", "pkforge.pokepark.party.save:x"],
            ParkKeepsakeFormat.ParkKeys(["pkforge.pokepark.care.v1", "pkforge.log", "pkforge.pokepark.party.save:x", "pkforge.protection.doc"]));

    [Fact]
    public void Writes_fill_the_free_slots_first_then_replace_the_oldest()
    {
        Assert.Equal("pkforge-keepsake-1.json", ParkKeepsakeFormat.NextSlot([]));
        Assert.Equal("pkforge-keepsake-3.json", ParkKeepsakeFormat.NextSlot([("pkforge-keepsake-1.json", Noon), ("pkforge-keepsake-2.json", Noon.AddMinutes(1))]));
        var full = Enumerable.Range(1, ParkKeepsakeFormat.Slots).Select(i => ($"pkforge-keepsake-{i}.json", (DateTimeOffset?)Noon.AddMinutes(i == 4 ? -5 : i))).ToArray();
        Assert.Equal("pkforge-keepsake-4.json", ParkKeepsakeFormat.NextSlot(full));
        // An unreadable copy is the first to go.
        Assert.Equal("pkforge-keepsake-2.json", ParkKeepsakeFormat.NextSlot([.. full.Select(f => f.Item1 == "pkforge-keepsake-2.json" ? (f.Item1, (DateTimeOffset?)null) : f)]));
    }

    [Fact]
    public void Only_the_slot_files_count_as_keepsakes()
    {
        Assert.True(ParkKeepsakeFormat.IsKeepsake("pkforge-keepsake-5.json"));
        Assert.False(ParkKeepsakeFormat.IsKeepsake("pkforge-keepsake-6.json"));
        Assert.False(ParkKeepsakeFormat.IsKeepsake("Pokemon - Emerald.sav"));
    }

    [Fact]
    public void The_newest_readable_copy_wins()
    {
        var newest = ParkKeepsakeFormat.Newest([Sample(Noon), null, Sample(Noon.AddHours(2)), Sample(Noon.AddHours(1))]);
        Assert.Equal(Noon.AddHours(2), newest!.SavedAt);
        Assert.Null(ParkKeepsakeFormat.Newest([null, null]));
    }

    [Fact]
    public void Writing_waits_a_minute_between_copies()
    {
        Assert.True(ParkKeepsakeFormat.IsDue(null, Noon));
        Assert.False(ParkKeepsakeFormat.IsDue(Noon, Noon.AddSeconds(59)));
        Assert.True(ParkKeepsakeFormat.IsDue(Noon, Noon.AddMinutes(1)));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    [InlineData(0, 1, false)]
    public void A_keepsake_only_ever_fills_an_empty_app(int residents, int bankEntries, bool offered) =>
        Assert.Equal(offered, ParkKeepsakeFormat.CanRestore(residents, bankEntries));

    [Fact]
    public void An_empty_park_isn_t_kept_over_a_real_one() =>
        // Leaving a fresh install's empty park must not push a real park's copies out of the rotation.
        Assert.False(ParkKeepsakeFormat.WorthKeeping(residents: 0, bankEntries: 0));

    [Fact]
    public void The_save_scanner_never_takes_a_keepsake_for_a_save() =>
        Assert.False(PKForge.Infrastructure.EmulatorSaveHeuristics.IsCandidateFileName(ParkKeepsakeFormat.SlotName(1)));
}
