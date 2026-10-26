using PKForge.Engine;
using Xunit;

namespace PKForge.Engine.Tests;

/// <summary>Blissey's notebook: stays whose EXP couldn't be written when they ended, settled once their saves are open.</summary>
public sealed class ParkNotebookTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);

    private static ParkNote Note(string resident = "save:F5643CBB7A574312:-1:0:idA", double hours = 17) =>
        new(resident, "idA", "Enchyrn", "PK3", Morning.AddHours(-hours), Morning, hours, DaycarePace.Gentle);

    [Fact]
    public void Only_stays_that_could_not_be_written_go_in_the_notebook()
    {
        Assert.True(ParkDaycareService.GoesInNotebook(DaycarePickup.Owed));
        foreach (var other in Enum.GetValues<DaycarePickup>().Where(o => o != DaycarePickup.Owed))
            Assert.False(ParkDaycareService.GoesInNotebook(other));
    }

    [Fact]
    public void An_owed_stay_ends_the_stay_since_the_notebook_keeps_its_time() =>
        Assert.True(ParkDaycareService.EndsStay(DaycarePickup.Owed));

    [Fact]
    public void Notes_wait_until_settled()
    {
        var notes = ParkNotebook.Add([], Note());
        Assert.Single(ParkNotebook.Owed(notes));

        var settled = ParkNotebook.Settle(notes, notes[0], Morning.AddHours(1), 209);

        Assert.Empty(ParkNotebook.Owed(settled));
        Assert.Equal((Morning.AddHours(1), 209u), (settled[0].Settled, settled[0].ExpWritten));
    }

    [Fact]
    public void Settling_one_note_leaves_the_others_owed()
    {
        var notes = ParkNotebook.Add(ParkNotebook.Add([], Note(hours: 2)), Note("bank:b", 5));
        var settled = ParkNotebook.Settle(notes, notes[0], Morning, 17);
        Assert.Equal(["bank:b"], ParkNotebook.Owed(settled).Select(n => n.ResidentId));
    }

    [Fact]
    public void The_oldest_settled_notes_make_room_but_owed_ones_are_never_dropped()
    {
        var notes = ParkNotebook.Add([], Note("owed"));
        for (var i = 0; i < ParkNotebook.MostKept + 5; i++)
        {
            notes = ParkNotebook.Add(notes, Note($"r{i}"));
            notes = ParkNotebook.Settle(notes, notes[^1], Morning, 1);
        }
        Assert.Equal(ParkNotebook.MostKept, notes.Count);
        Assert.Equal("owed", Assert.Single(ParkNotebook.Owed(notes)).ResidentId);
    }
}
