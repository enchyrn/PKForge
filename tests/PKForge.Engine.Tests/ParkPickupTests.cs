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
