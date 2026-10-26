using PKForge.Engine;
using Xunit;

namespace PKForge.Engine.Tests;

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
    [InlineData(PKForge.Engine.RadicalRed.CfruEntity.RadicalRed)]
    [InlineData(PKForge.Engine.RadicalRed.CfruEntity.GsChronicles)]
    public void Radical_Red_and_GS_Chronicles_aren_t_written_back_yet(string format) =>
        Assert.False(ParkDaycareService.WritesBackTo(format));

    [Theory]
    [InlineData("PK3")]
    [InlineData(PKForge.Engine.RadicalRed.CfruEntity.Unbound)]
    public void Official_games_and_Unbound_are_written_back(string format) =>
        Assert.True(ParkDaycareService.WritesBackTo(format));

    [Theory]
    [InlineData(DaycarePickup.Written, true)]
    [InlineData(DaycarePickup.NothingToWrite, true)]
    [InlineData(DaycarePickup.ParkMemory, true)]
    [InlineData(DaycarePickup.Owed, true)]
    [InlineData(DaycarePickup.NotTheSame, false)]   // the time isn't lost: picked up again once it's back in its save
    [InlineData(DaycarePickup.WriteFailed, false)]
    public void Only_a_refused_or_failed_write_keeps_the_stay(DaycarePickup outcome, bool ends) =>
        Assert.Equal(ends, ParkDaycareService.EndsStay(outcome));
}
