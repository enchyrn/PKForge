using PKForge.Engine;
using Xunit;

namespace PKForge.Engine.Tests;

public sealed class ParkPickupTests
{
    [Fact]
    public void A_stay_that_would_grow_waits_for_its_save_to_be_opened() =>
        Assert.True(ParkDaycareService.WaitsForSave(fromSave: true, saveOpen: false, writesBack: true, DaycarePace.Gentle));

    [Theory]
    [InlineData(false, false, true, DaycarePace.Gentle)] // a Bank resident needs no save
    [InlineData(true, true, true, DaycarePace.Gentle)]   // its save is open: it can be written
    [InlineData(true, false, false, DaycarePace.Gentle)] // writing back is off: nothing to wait for
    [InlineData(true, false, true, DaycarePace.Off)]     // pace Off: it earns nothing
    public void Otherwise_the_pickup_goes_ahead(bool fromSave, bool saveOpen, bool writesBack, DaycarePace pace) =>
        Assert.False(ParkDaycareService.WaitsForSave(fromSave, saveOpen, writesBack, pace));
}

public sealed class ParkStayIdentityTests
{
    private const string Sent = "id1A2B3C4DA7AF64E9A7AF64E9";

    [Fact]
    public void A_stay_checked_in_with_its_save_open_keeps_what_the_save_said() =>
        Assert.Equal("idFFFFFFFF00000001000000001", ParkIdentity.ForStay("idFFFFFFFF00000001000000001", Sent, $"save:F5643CBB7A574312:-1:0:{Sent}"));

    [Fact]
    public void A_stay_checked_in_with_its_save_closed_falls_back_to_the_resident() =>
        Assert.Equal(Sent, ParkIdentity.ForStay(null, Sent, "bank:0f8fad5bd9cb469fa165708677289f8e"));

    [Fact]
    public void Without_a_recorded_identity_the_resident_id_still_carries_one() =>
        Assert.Equal(Sent, ParkIdentity.ForStay(null, null, $"save:F5643CBB7A574312:-1:0:{Sent}"));

    [Fact]
    public void A_resident_sent_before_identities_were_kept_has_none() =>
        Assert.Null(ParkIdentity.ForStay(null, null, "save:F5643CBB7A574312:3:12"));
}

public sealed class ParkPickupOutcomeTests
{
    [Theory]
    [InlineData(PKForge.Engine.RadicalRed.CfruEntity.Unbound)]
    [InlineData(PKForge.Engine.RadicalRed.CfruEntity.RadicalRed)]
    [InlineData(PKForge.Engine.RadicalRed.CfruEntity.GsChronicles)]
    public void A_CFRU_hack_s_Pokemon_is_not_written_back_yet(string format) =>
        Assert.False(ParkDaycareService.WritesBackTo(format));

    [Fact]
    public void An_official_game_s_Pokemon_is_written_back() =>
        Assert.True(ParkDaycareService.WritesBackTo("PK3"));

    [Theory]
    [InlineData(DaycarePickup.Written, true)]
    [InlineData(DaycarePickup.NothingToWrite, true)]
    [InlineData(DaycarePickup.ParkMemory, true)]
    [InlineData(DaycarePickup.NotTheSame, false)]   // the time isn't lost: picked up again once it's back in its save
    [InlineData(DaycarePickup.WriteFailed, false)]
    public void Only_a_refused_or_failed_write_keeps_the_stay(DaycarePickup outcome, bool ends) =>
        Assert.Equal(ends, ParkDaycareService.EndsStay(outcome));
}
