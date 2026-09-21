using KiCadSharp.Documents;

using static KiCadSharp.Fluent.Tests.TestSupport;

namespace KiCadSharp.Fluent.Tests;

/// <summary>
/// Every <c>With*</c> on a board and on the board items that have an <c>Add*</c>. Only
/// <c>AddNet</c> returns a child, so it is the one with a callback to test; the rest return the
/// item they were called on and append what they were given.
/// </summary>
public class BoardFluentTests
{
    [Fact]
    public void WithNet_ReturnsTheBoard_AppendsTheNet_AndHandsItOver()
    {
        var board = new KiCadBoard();
        KiCadNet? received = null;
        var attached = false;

        var returned = board.WithNet(1, "GND", n =>
        {
            received = n;
            attached = board.Nets.Contains(n);
        });

        Assert.Same(board, returned);
        var net = Assert.Single(board.Nets);
        Assert.Equal(1, net.Code);
        Assert.Equal("GND", net.Name);
        Assert.Same(net.Node, received!.Node);
        Assert.True(attached);

        var expected = new KiCadBoard();
        expected.AddNet(1, "GND");
        Assert.Equal(expected.ToText(), board.ToText());
    }

    [Fact]
    public void WithPoint_OnAZone_ReturnsTheZone_AndAppendsToTheOutline()
    {
        var zone = new KiCadZone();

        var returned = zone.WithPoint(0, 0).WithPoint(10, 0).WithPoint(10, 5);

        Assert.Same(zone, returned);
        Assert.Equal([new KiCadPosition(0, 0), new KiCadPosition(10, 0), new KiCadPosition(10, 5)], zone.Points);

        var expected = new KiCadZone();
        expected.AddPoint(0, 0);
        expected.AddPoint(10, 0);
        expected.AddPoint(10, 5);
        SameText(expected, zone);
    }

    [Fact]
    public void WithPoint_OnABoardPolygon_ReturnsThePolygon_AndAppendsTheVertex()
    {
        var polygon = new KiCadGrPoly();

        var returned = polygon.WithPoint(1, 2).WithPoint(3, 4);

        Assert.Same(polygon, returned);
        Assert.Equal([new KiCadPosition(1, 2), new KiCadPosition(3, 4)], polygon.Points);

        var expected = new KiCadGrPoly();
        expected.AddPoint(1, 2);
        expected.AddPoint(3, 4);
        SameText(expected, polygon);
    }

    [Fact]
    public void WithPoint_OnACurve_ReturnsTheCurve_AndAppendsTheControlPoint()
    {
        var curve = new KiCadGrCurve();

        var returned = curve.WithPoint(0, 0).WithPoint(1, 1).WithPoint(2, 1).WithPoint(3, 0);

        Assert.Same(curve, returned);
        Assert.Equal(
            [new KiCadPosition(0, 0), new KiCadPosition(1, 1), new KiCadPosition(2, 1), new KiCadPosition(3, 0)],
            curve.Points);

        var expected = new KiCadGrCurve();
        expected.AddPoint(0, 0);
        expected.AddPoint(1, 1);
        expected.AddPoint(2, 1);
        expected.AddPoint(3, 0);
        SameText(expected, curve);
    }

    [Fact]
    public void WithPoint_OnADimension_ReturnsTheDimension_AndAppendsTheMeasuredPoint()
    {
        var dimension = new KiCadDimension();

        var returned = dimension.WithPoint(0, 0).WithPoint(25.4, 0);

        Assert.Same(dimension, returned);
        Assert.Equal([new KiCadPosition(0, 0), new KiCadPosition(25.4, 0)], dimension.Points);

        var expected = new KiCadDimension();
        expected.AddPoint(0, 0);
        expected.AddPoint(25.4, 0);
        SameText(expected, dimension);
    }

    [Fact]
    public void WithMember_OnAGroup_ReturnsTheGroup_AndAddsTheUuid()
    {
        var group = new KiCadGroup("Power");
        const string first = "0b2c7f7e-7a57-4f7c-9a0e-3c1f1a2b3c4d";
        const string second = "5e6f7a8b-9c0d-4e1f-8a2b-3c4d5e6f7a8b";

        var returned = group.WithMember(first).WithMember(second);

        Assert.Same(group, returned);
        Assert.Equal([first, second], group.Members);

        var expected = new KiCadGroup("Power");
        expected.AddMember(first);
        expected.AddMember(second);
        SameText(expected, group);
    }
}
