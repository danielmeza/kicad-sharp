using KiCadSharp.Documents;
using KiCadSharp.Schematics;

using SExpressions;

using static KiCadSharp.Fluent.Tests.TestSupport;

namespace KiCadSharp.Fluent.Tests;

/// <summary>
/// The <c>With*</c> on the schematic items that have an <c>Add*</c>: a wire or bus, and a bus alias.
/// </summary>
public class SchematicFluentTests
{
    [Fact]
    public void WithPoint_OnAWire_ReturnsTheWire_TypedAsAWire()
    {
        var wire = new KiCadWire(new SExpression(KiCadTokens.Schematic.Wire));

        // Declared on the shared base, but the chain stays a KiCadWire: this line is the assertion
        // that it compiles without a cast.
        KiCadWire returned = wire.WithPoint(0, 0).WithPoint(12.7, 0);

        Assert.Same(wire, returned);
        Assert.Equal([new KiCadPosition(0, 0), new KiCadPosition(12.7, 0)], wire.Points);

        var expected = new KiCadWire(new SExpression(KiCadTokens.Schematic.Wire));
        expected.AddPoint(0, 0);
        expected.AddPoint(12.7, 0);
        SameText(expected, wire);
    }

    [Fact]
    public void WithPoint_OnABus_ReturnsTheBus_TypedAsABus()
    {
        var bus = new KiCadBus(new SExpression(KiCadTokens.Schematic.Bus));

        KiCadBus returned = bus.WithPoint(2.54, 2.54).WithPoint(2.54, 20.32);

        Assert.Same(bus, returned);
        Assert.Equal([new KiCadPosition(2.54, 2.54), new KiCadPosition(2.54, 20.32)], bus.Points);

        var expected = new KiCadBus(new SExpression(KiCadTokens.Schematic.Bus));
        expected.AddPoint(2.54, 2.54);
        expected.AddPoint(2.54, 20.32);
        SameText(expected, bus);
    }

    [Fact]
    public void WithMember_OnABusAlias_ReturnsTheAlias_AndAppendsTheNet()
    {
        var alias = new KiCadBusAlias(new SExpression(KiCadTokens.Schematic.BusAlias)) { Name = "I2C0" };

        var returned = alias.WithMember("I2C0_SCL").WithMember("I2C0_SDA");

        Assert.Same(alias, returned);
        Assert.Equal(["I2C0_SCL", "I2C0_SDA"], alias.Members);

        var expected = new KiCadBusAlias(new SExpression(KiCadTokens.Schematic.BusAlias)) { Name = "I2C0" };
        expected.AddMember("I2C0_SCL");
        expected.AddMember("I2C0_SDA");
        SameText(expected, alias);
    }
}
