using KiCadSharp.Documents;
using KiCadSharp.Schematics;

namespace KiCadSharp.Fluent.Tests;

/// <summary>
/// <c>With</c> on a live node list — the fluent form of <c>board.Zones.Add()</c> and of
/// <c>list.Add(item)</c>, which is how most board and schematic items are appended.
/// </summary>
public class NodeListFluentTests
{
    [Fact]
    public void With_ReturnsTheList_AppendsANewElement_AndHandsItOver()
    {
        var board = new KiCadBoard();
        var zones = board.Zones;
        KiCadZone? received = null;
        var attached = false;

        var returned = zones.With(z =>
        {
            received = z;
            attached = board.Zones.Contains(z);
            z.WithPoint(0, 0).WithPoint(10, 0).WithPoint(10, 10);
        });

        Assert.Same(zones, returned);
        var zone = Assert.Single(board.Zones);
        Assert.Equal(3, zone.Points.Count);
        Assert.Same(zone.Node, received!.Node);
        Assert.True(attached);

        var expected = new KiCadBoard();
        var core = expected.Zones.Add();
        core.AddPoint(0, 0);
        core.AddPoint(10, 0);
        core.AddPoint(10, 10);
        Assert.Equal(expected.ToText(), board.ToText());
    }

    [Fact]
    public void With_GivenAnElement_ReturnsTheList_AppendsIt_AndHandsItOver()
    {
        var board = new KiCadBoard();
        var groups = board.Groups;
        var group = new KiCadGroup("Power");
        KiCadGroup? received = null;
        var attached = false;

        var returned = groups.With(group, g =>
        {
            received = g;
            attached = board.Groups.Contains(g);
        });

        Assert.Same(groups, returned);
        Assert.Same(group.Node, Assert.Single(board.Groups).Node);
        Assert.Same(group, received);
        Assert.True(attached);

        var expected = new KiCadBoard();
        expected.Groups.Add(new KiCadGroup("Power"));
        Assert.Equal(expected.ToText(), board.ToText());
    }

    [Fact]
    public void With_GivenAnElementOfAnotherFile_MovesIt_ExactlyAsAddDoes()
    {
        var from = new KiCadBoard();
        from.Zones.With(z => z.WithPoint(0, 0).WithPoint(5, 0).WithPoint(5, 5));
        var to = new KiCadBoard();

        to.Zones.With(from.Zones[0]);

        Assert.Empty(from.Zones);
        Assert.Equal(3, Assert.Single(to.Zones).Points.Count);

        var addFrom = new KiCadBoard();
        addFrom.Zones.With(z => z.WithPoint(0, 0).WithPoint(5, 0).WithPoint(5, 5));
        var addTo = new KiCadBoard();
        addTo.Zones.Add(addFrom.Zones[0]);
        Assert.Equal(addFrom.ToText(), from.ToText());
        Assert.Equal(addTo.ToText(), to.ToText());
    }

    [Fact]
    public void With_GivenAnElementOfTheWrongToken_ThrowsWhatAddThrows_AndNeverCallsBack()
    {
        var board = new KiCadBoard();
        var called = false;

        // A (zone …) node wrapped as a group: the list refuses it by token.
        var impostor = new KiCadGroup(new KiCadZone().Node);

        Assert.Throws<ArgumentException>(() => board.Groups.With(impostor, _ => called = true));
        Assert.Throws<ArgumentException>(() => board.Groups.Add(impostor));
        Assert.False(called);
        Assert.Empty(board.Groups);
    }

    [Fact]
    public void With_OnAListOverAFormTheFileDoesNotHave_ThrowsWhatAddThrows()
    {
        var schematic = KiCadSchematic.Parse("(kicad_sch (version 20250114) (generator \"eeschema\"))");
        var called = false;

        Assert.Throws<InvalidOperationException>(() => schematic.LibrarySymbols.With(_ => called = true));
        Assert.Throws<InvalidOperationException>(() => schematic.LibrarySymbols.Add());
        Assert.False(called);

        // The Require… accessor is the deliberate way in, and With works on it like Add does.
        Assert.Single(schematic.RequireLibrarySymbols().With(s => s.Id = "Device:R"));
        Assert.Equal("Device:R", Assert.Single(schematic.LibrarySymbols).Id);
    }
}
