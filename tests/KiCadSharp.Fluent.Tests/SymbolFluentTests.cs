using KiCadSharp.Documents;

using static KiCadSharp.Fluent.Tests.TestSupport;

namespace KiCadSharp.Fluent.Tests;

/// <summary>
/// Every <c>With*</c> on a symbol library, a symbol, a sub-unit and a polyline: returns the parent,
/// the child is there with the values given, and the callback gets that child, already attached.
/// </summary>
public class SymbolFluentTests
{
    private static KiCadPin Pin(string number, double y) =>
        new("passive", "line", new KiCadPosition(0, y, 180), 2.54, "~", number);

    [Fact]
    public void WithSymbol_GivenASymbol_ReturnsTheLibrary_AppendsIt_AndHandsItOver()
    {
        var library = new KiCadSymbolLibrary();
        var symbol = new KiCadSymbol("R");
        KiCadSymbol? received = null;
        var attached = false;

        var returned = library.WithSymbol(symbol, s =>
        {
            received = s;
            attached = library.Symbols.Contains(s);
        });

        Assert.Same(library, returned);
        Assert.Same(symbol.Node, Assert.Single(library.Symbols).Node);
        Assert.Same(symbol, received);
        Assert.True(attached);

        var expected = new KiCadSymbolLibrary();
        expected.AddSymbol(new KiCadSymbol("R"));
        Assert.Equal(expected.ToText(), library.ToText());
    }

    [Fact]
    public void WithSymbol_GivenAName_ReturnsTheLibrary_CreatesTheSymbol_AndHandsItOver()
    {
        var library = new KiCadSymbolLibrary();
        KiCadSymbol? received = null;
        var attached = false;

        var returned = library.WithSymbol("C", s =>
        {
            received = s;
            attached = library.Symbols.Contains(s);
        });

        Assert.Same(library, returned);
        var symbol = Assert.Single(library.Symbols);
        Assert.Equal("C", symbol.Id);
        Assert.Equal("C", symbol.GetPropertyValue(KiCadPropertyNames.Value));
        Assert.Same(symbol.Node, received!.Node);
        Assert.True(attached);

        var expected = new KiCadSymbolLibrary();
        expected.AddSymbol("C");
        Assert.Equal(expected.ToText(), library.ToText());
    }

    [Fact]
    public void WithProperty_ReturnsTheSymbol_AppendsTheProperty_AndHandsItOver()
    {
        var symbol = new KiCadSymbol("R");
        KiCadProperty? received = null;
        var attached = false;

        var returned = symbol.WithProperty("Description", "Resistor", p =>
        {
            received = p;
            attached = symbol.Properties.Contains(p);
        });

        Assert.Same(symbol, returned);
        var property = symbol.Properties[^1];
        Assert.Equal("Description", property.Key);
        Assert.Equal("Resistor", property.Value);
        Assert.Same(property.Node, received!.Node);
        Assert.True(attached);

        var expected = new KiCadSymbol("R");
        expected.AddProperty("Description", "Resistor");
        SameText(expected, symbol);
    }

    [Fact]
    public void WithProperty_OnAKeyThatExists_UpdatesItInPlace_AsAddPropertyDoes()
    {
        var symbol = new KiCadSymbol("R");
        var before = symbol.Properties.Count;
        KiCadProperty? received = null;

        symbol.WithProperty(KiCadPropertyNames.Reference, "R", p => received = p);

        Assert.Equal(before, symbol.Properties.Count);
        Assert.Equal("R", symbol.GetPropertyValue(KiCadPropertyNames.Reference));
        Assert.Equal(KiCadPropertyNames.Reference, received!.Key);

        var expected = new KiCadSymbol("R");
        expected.AddProperty(KiCadPropertyNames.Reference, "R");
        SameText(expected, symbol);
    }

    [Fact]
    public void WithPin_OnASymbol_ReturnsTheSymbol_AppendsThePin_AndHandsItOver()
    {
        var symbol = new KiCadSymbol("R");
        var pin = Pin("1", 3.81);
        KiCadPin? received = null;
        var attached = false;

        var returned = symbol.WithPin(pin, p =>
        {
            received = p;
            attached = symbol.Pins.Contains(p);
        });

        Assert.Same(symbol, returned);
        var only = Assert.Single(symbol.Pins);
        Assert.Same(pin.Node, only.Node);
        Assert.Equal("1", only.Number);
        Assert.Equal(new KiCadPosition(0, 3.81, 180), only.Position);
        Assert.Same(pin, received);
        Assert.True(attached);

        var expected = new KiCadSymbol("R");
        expected.AddPin(Pin("1", 3.81));
        SameText(expected, symbol);
    }

    [Fact]
    public void WithGraphicalItem_ReturnsTheSymbol_AndTheCallbackSeesTheItemsOwnType()
    {
        var symbol = new KiCadSymbol("R");
        var polyline = new KiCadPolyline();
        KiCadPolyline? received = null;
        var attached = false;

        // The callback is typed KiCadPolyline, not KiCadGraphicalItem: WithPoint needs no cast.
        var returned = symbol.WithGraphicalItem(polyline, p =>
        {
            received = p;
            attached = symbol.GraphicalItems.Contains(p);
            p.WithPoint(-1.016, 0).WithPoint(1.016, 0);
        });

        Assert.Same(symbol, returned);
        var item = Assert.IsType<KiCadPolyline>(Assert.Single(symbol.GraphicalItems));
        Assert.Same(polyline.Node, item.Node);
        Assert.Equal([new KiCadPosition(-1.016, 0), new KiCadPosition(1.016, 0)], item.Points);
        Assert.Same(polyline, received);
        Assert.True(attached);

        var expected = new KiCadSymbol("R");
        var core = new KiCadPolyline();
        expected.AddGraphicalItem(core);
        core.AddPoint(-1.016, 0);
        core.AddPoint(1.016, 0);
        SameText(expected, symbol);
    }

    [Fact]
    public void WithUnit_ReturnsTheSymbol_CreatesTheSubUnit_AndHandsItOver()
    {
        var symbol = new KiCadSymbol("R");
        KiCadSymbolUnit? received = null;
        var attached = false;

        var returned = symbol.WithUnit("R_1_1", u =>
        {
            received = u;
            attached = symbol.Units.Contains(u);
        });

        Assert.Same(symbol, returned);
        var unit = Assert.Single(symbol.Units);
        Assert.Equal("R_1_1", unit.Id);
        Assert.Equal(1, unit.Unit);
        Assert.Same(unit.Node, received!.Node);
        Assert.True(attached);

        var expected = new KiCadSymbol("R");
        expected.AddUnit("R_1_1");
        SameText(expected, symbol);
    }

    [Fact]
    public void WithPin_OnASubUnit_ReturnsTheSubUnit_AppendsThePin_AndHandsItOver()
    {
        var unit = new KiCadSymbol("R").AddUnit("R_1_1");
        var pin = Pin("2", -3.81);
        KiCadPin? received = null;
        var attached = false;

        var returned = unit.WithPin(pin, p =>
        {
            received = p;
            attached = unit.Pins.Contains(p);
        });

        Assert.Same(unit, returned);
        var only = Assert.Single(unit.Pins);
        Assert.Same(pin.Node, only.Node);
        Assert.Equal("2", only.Number);
        Assert.Same(pin, received);
        Assert.True(attached);

        var expected = new KiCadSymbol("R").AddUnit("R_1_1");
        expected.AddPin(Pin("2", -3.81));
        SameText(expected, unit);
    }

    [Fact]
    public void WithPoint_OnAPolyline_ReturnsThePolyline_AndAppendsTheVertexInOrder()
    {
        var polyline = new KiCadPolyline();

        var returned = polyline.WithPoint(0, 1.27).WithPoint(0, -1.27);

        Assert.Same(polyline, returned);
        Assert.Equal([new KiCadPosition(0, 1.27), new KiCadPosition(0, -1.27)], polyline.Points);

        var expected = new KiCadPolyline();
        expected.AddPoint(0, 1.27);
        expected.AddPoint(0, -1.27);
        SameText(expected, polyline);
    }
}
