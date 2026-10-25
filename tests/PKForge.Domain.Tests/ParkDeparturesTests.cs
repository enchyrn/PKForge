using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class ParkDeparturesTests
{
    private sealed record Resident(string Id, string Name);

    [Fact]
    public void Leaving_again_keeps_one_memory_with_its_latest_details()
    {
        IReadOnlyList<Resident> departed = [];
        departed = ParkDepartures.Remember(departed, new Resident("save:a:-1:0:x", "Treecko"), r => r.Id);
        departed = ParkDepartures.Remember(departed, new Resident("save:a:-1:0:x", "Grovyle"), r => r.Id);
        Assert.Equal([new Resident("save:a:-1:0:x", "Grovyle")], departed);
    }

    [Fact]
    public void Only_the_newest_departures_are_kept()
    {
        IReadOnlyList<Resident> departed = [];
        for (var i = 0; i < ParkDepartures.MostRemembered + 5; i++)
            departed = ParkDepartures.Remember(departed, new Resident($"bank:{i}", "Ralts"), r => r.Id);
        Assert.Equal(ParkDepartures.MostRemembered, departed.Count);
        Assert.Equal("bank:5", departed[0].Id);
        Assert.Equal($"bank:{ParkDepartures.MostRemembered + 4}", departed[^1].Id);
    }

    [Fact]
    public void A_returning_resident_is_no_longer_departed()
    {
        IReadOnlyList<Resident> departed = [new("bank:1", "Ralts"), new("bank:2", "Zigzagoon")];
        Assert.Equal([new Resident("bank:2", "Zigzagoon")], ParkDepartures.Forget(departed, "bank:1", r => r.Id));
    }
}
