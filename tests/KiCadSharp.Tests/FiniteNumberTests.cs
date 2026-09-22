using KiCadSharp.Documents;
using KiCadSharp.Schematics;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// A setter refuses a number KiCad cannot read, and writes nothing (#101).
/// </summary>
/// <remarks>
/// <para>
/// Every number this library writes goes through one formatter, and <c>double.NaN</c>,
/// <c>double.PositiveInfinity</c> and <c>double.NegativeInfinity</c> used to come out of it as
/// <c>NaN</c>, <c>Infinity</c> and <c>-Infinity</c>. MEASURED against kicad-cli 10.0.6
/// (<c>ghcr.io/danielmeza/orbion-kicad-release:10.0.6</c>): <c>pcb export svg</c> refuses the whole
/// board over each of them, exit 3, "Failed to load board: need a number for 'hatch pitch'",
/// "… for 'zone clearance'", "… for 'min_thickness'", "… for 'X coordinate'". The message is the
/// lexer's, <c>DSNLEXER::NeedNUMBER</c> (<c>common/dsnlexer.cpp</c>, lines 427–439), so it does not
/// matter which token the number is in.
/// </para>
/// <para>
/// So a set throws <see cref="ArgumentOutOfRangeException"/> and leaves the document as it was —
/// not just the value, but every child a set would have created on the way to it: the
/// <c>(connect_pads …)</c> a clearance lives in, the whole <c>(hatch style pitch)</c> a pitch does,
/// the <c>(polygon (pts …))</c> a vertex does.
/// </para>
/// </remarks>
public class FiniteNumberTests
{
    public static TheoryData<double> NonFinite => new() { double.NaN, double.PositiveInfinity, double.NegativeInfinity };

    // ------------------------------------------------------------------ a zone on a loaded board

    [Theory]
    [MemberData(nameof(NonFinite))]
    public void AZoneSetter_GivenANumberKiCadCannotRead_Throws_AndTheBoardKeepsItsBytes(double value)
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        var before = board.ToText();
        var zone = board.Zones[0];
        var fill = zone.Fill!;

        Assert.Throws<ArgumentOutOfRangeException>(() => zone.HatchPitch = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => zone.ConnectPadsClearance = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => zone.MinThickness = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => fill.ThermalGap = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => fill.ThermalBridgeWidth = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => zone.AddPoint(value, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => zone.AddPoint(0, value));

        Assert.Equal(before, board.ToText());
    }

    // --------------------------------------------------------------- the children a set creates

    /// <summary>
    /// The setters that create a whole form before writing into it: a pitch creates the
    /// <c>(hatch …)</c>, a clearance the <c>(connect_pads …)</c>, a vertex the <c>(polygon (pts …))</c>.
    /// None of that may be left behind when the number is refused.
    /// </summary>
    [Theory]
    [MemberData(nameof(NonFinite))]
    public void OnAnEmptyZone_ANumberKiCadCannotRead_Throws_AndCreatesNothing(double value)
    {
        var zone = new KiCadZone();

        Assert.Throws<ArgumentOutOfRangeException>(() => zone.HatchPitch = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => zone.ConnectPadsClearance = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => zone.MinThickness = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => zone.AddPoint(value, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => zone.AddPoint(0, value));

        Assert.Equal("(zone)", zone.Node.ToText().Trim());
    }

    [Theory]
    [MemberData(nameof(NonFinite))]
    public void OnAnEmptyText_ANumberKiCadCannotRead_Throws_AndCreatesNoFont(double value)
    {
        var text = new KiCadGrText();
        var before = text.Node.ToText();
        var effects = text.RequireFontEffects();
        var withEffects = text.Node.ToText();
        Assert.NotEqual(before, withEffects);

        Assert.Throws<ArgumentOutOfRangeException>(() => effects.Thickness = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => effects.Size = new KiCadSize(value, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => effects.Size = new KiCadSize(1, value));

        Assert.Equal(withEffects, text.Node.ToText());
        Assert.Null(effects.Node.GetChild("font"));
    }

    [Theory]
    [MemberData(nameof(NonFinite))]
    public void OnAnEmptyPolyline_ANumberKiCadCannotRead_Throws_AndCreatesNoPoints(double value)
    {
        var symbolPolyline = new KiCadPolyline();
        var wire = new KiCadWire(new SExpression("wire"));

        Assert.Throws<ArgumentOutOfRangeException>(() => symbolPolyline.AddPoint(value, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => symbolPolyline.AddPoint(0, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => wire.AddPoint(value, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => wire.AddPoint(0, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => wire.Start = new KiCadPosition(value, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => wire.End = new KiCadPosition(0, value));

        Assert.Null(symbolPolyline.Node.GetChild("pts"));
        Assert.Null(wire.Node.GetChild("pts"));
    }

    // ------------------------------------------------------------------- values written as a whole

    /// <summary>
    /// A position is written coordinate by coordinate. One that KiCad cannot read must not leave the
    /// ones before it written: the whole value is refused, or the whole value is written.
    /// </summary>
    [Theory]
    [MemberData(nameof(NonFinite))]
    public void APositionWithOneCoordinateKiCadCannotRead_Throws_AndMovesNothing(double value)
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        var before = board.ToText();
        var segment = board.Segments[0];
        var footprint = board.Footprints[0];

        Assert.Throws<ArgumentOutOfRangeException>(() => segment.Start = new KiCadPosition(value, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => segment.End = new KiCadPosition(0, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => segment.Width = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => footprint.Position = new KiCadPosition(1, 2, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => footprint.Position = new KiCadPosition(value, 2, 3));

        Assert.Equal(before, board.ToText());
    }

    /// <summary>
    /// The value types are values, not views: they hold whatever they are given, and describe it,
    /// so a debugger can show the position that is about to be refused.
    /// </summary>
    [Fact]
    public void AValueHoldsANumberKiCadCannotRead_AndDescribesIt()
    {
        Assert.Equal("(NaN, 0)", new KiCadPosition(double.NaN, 0).ToString());
        Assert.Equal("(0, 1, Infinity°)", new KiCadPosition(0, 1, double.PositiveInfinity).ToString());
    }

    /// <summary>The exception names the value, so a caller with fifty setters knows which one.</summary>
    [Fact]
    public void TheExceptionNamesTheValueAndWhatKiCadNeeds()
    {
        var zone = new KiCadZone();

        var pitch = Assert.Throws<ArgumentOutOfRangeException>(() => zone.HatchPitch = double.NaN);
        var x = Assert.Throws<ArgumentOutOfRangeException>(() => zone.AddPoint(double.NegativeInfinity, 0));

        Assert.Equal("value", pitch.ParamName);
        Assert.Equal(double.NaN, pitch.ActualValue);
        Assert.Equal("x", x.ParamName);
        Assert.Equal(double.NegativeInfinity, x.ActualValue);
        Assert.Contains("KiCad reads no number", pitch.Message, StringComparison.Ordinal);
    }

    /// <summary>Every finite double still writes, the largest and smallest included.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-0.0)]
    [InlineData(double.Epsilon)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    public void AFiniteNumber_StillWrites(double value)
    {
        var zone = new KiCadZone();

        zone.MinThickness = value;

        Assert.NotNull(zone.Node.GetChild("min_thickness"));
    }
}
