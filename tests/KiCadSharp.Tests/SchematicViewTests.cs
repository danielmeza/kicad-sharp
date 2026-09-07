using KiCadSharp.Documents;
using KiCadSharp.Schematics;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// The typed views over a <c>.kicad_sch</c>, against real KiCad 10 sheets.
/// </summary>
/// <remarks>
/// <para>
/// Every count here is taken twice: once off the raw s-expression tree, which has no model in
/// between to lose anything, and once through the views. A view that quietly skipped a form would
/// show up as the two numbers disagreeing rather than as a plausible-looking smaller number.
/// </para>
/// <para>
/// The tokens the vendored corpus happens not to contain — buses, no-connects, global and
/// hierarchical labels, images, Béziers, sheet pins — are covered by <see cref="RicherSheet"/>,
/// which is KiCad's own spelling of each form. It is parsed and written back unchanged as well, so
/// it also proves the views do not disturb what they read.
/// </para>
/// </remarks>
public class SchematicViewTests
{
    // ------------------------------------------------------------------ the fixture itself

    [Fact]
    public void Rs485Fixture_IsTheMeasuredFile()
    {
        Assert.Equal(170_001, new FileInfo(TestData.Rs485Bridge).Length);

        var root = SExpression.Load(TestData.Rs485Bridge);
        Assert.Equal("kicad_sch", root.Token);
        Assert.Equal(161, root.GetChildren("wire").Count());
        Assert.Equal(22, root.GetChildren("label").Count());
        Assert.Equal(18, root.GetChildren("junction").Count());
        Assert.Equal(8, root.GetChildren("text").Count());
        Assert.Equal(8, root.GetChildren("rectangle").Count());
        Assert.Equal(4, root.GetChildren("bus_alias").Count());

        // 74 placements at the root, and the 19 definitions they were cut from one level down.
        Assert.Equal(74, root.GetChildren("symbol").Count());
        Assert.Equal(19, root.GetChild("lib_symbols")!.GetChildren("symbol").Count());
    }

    [Fact]
    public void Rs485Fixture_ReadsTheSameCountsThroughTheViews()
    {
        var schematic = KiCadSchematic.Load(TestData.Rs485Bridge);

        Assert.Equal(161, schematic.Wires.Count);
        Assert.Equal(22, schematic.Labels.Count);
        Assert.Equal(18, schematic.Junctions.Count);
        Assert.Equal(8, schematic.TextItems.Count);
        Assert.Equal(8, schematic.Rectangles.Count);
        Assert.Equal(4, schematic.BusAliases.Count);
        Assert.Equal(74, schematic.Symbols.Count);
        Assert.Equal(19, schematic.LibrarySymbols.Count);
        Assert.Single(schematic.SheetInstances);

        // A flat sheet: the tokens that would carry a hierarchy are absent, not unread.
        Assert.Empty(schematic.Sheets);
        Assert.Empty(schematic.Buses);
        Assert.Empty(schematic.BusEntries);
        Assert.Empty(schematic.NoConnects);
        Assert.Empty(schematic.GlobalLabels);
        Assert.Empty(schematic.HierarchicalLabels);
        Assert.Empty(schematic.NetClassFlags);
        Assert.Empty(schematic.TextBoxes);
        Assert.Empty(schematic.Images);
    }

    [Fact]
    public void Rs485Fixture_ReadsItsHeader()
    {
        var schematic = KiCadSchematic.Load(TestData.Rs485Bridge);

        Assert.Equal("20250114", schematic.Version);
        Assert.Equal("eeschema", schematic.Generator);
        Assert.Equal("10.0", schematic.GeneratorVersion);
        Assert.Equal("A3", schematic.Paper);
        Assert.Equal("989f999f-e12c-57f6-847d-6c04a4834161", schematic.Uuid);
        Assert.False(schematic.EmbeddedFonts);
    }

    // -------------------------------------------------------------- an untouched save is a copy

    [Theory]
    [InlineData("orbion-rs485-bridge.kicad_sch", 170_001)]
    [InlineData("duplicate-refs.kicad_sch", 1_423)]
    [InlineData("power-input.kicad_sch", 45_693)]
    public void Schematic_Save_AfterOnlyReading_IsByteIdentical(string name, long length)
    {
        var source = TestData.Schematics.Single(p => Path.GetFileName(p) == name);
        Assert.Equal(length, new FileInfo(source).Length);

        var schematic = KiCadSchematic.Load(source);
        ReadEverything(schematic);

        // Reading is not writing: nothing above touched a setter, so nothing is marked modified.
        Assert.False(schematic.IsModified);

        var output = Path.Combine(TestData.NewScratchDirectory(), name);
        schematic.Save(output);

        Assert.Equal(length, new FileInfo(output).Length);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(output));
    }

    // ---------------------------------------------------------- one edit moves exactly one thing

    [Fact]
    public void Schematic_Save_AfterOneEdit_ChangesOnlyThatPropertysBytes()
    {
        var before = File.ReadAllText(TestData.Rs485Bridge);
        Assert.Single(Occurrences(before, "\"Orbion RS485 field bus node\""));

        var schematic = KiCadSchematic.Load(TestData.Rs485Bridge);
        schematic.TitleBlock!.Title = "Orbion RS485 field bus node, rev B";

        var output = Path.Combine(TestData.NewScratchDirectory(), "orbion-rs485-bridge.kicad_sch");
        schematic.Save(output);
        var after = File.ReadAllText(output);

        // ", rev B" is seven characters, and they are the only seven the file gained.
        Assert.Equal(170_008, new FileInfo(output).Length);
        Assert.Equal(before.Length + 7, after.Length);
        Assert.Equal(
            before.Replace(
                "\"Orbion RS485 field bus node\"",
                "\"Orbion RS485 field bus node, rev B\"",
                StringComparison.Ordinal),
            after);
    }

    [Fact]
    public void Schematic_Save_AfterMovingOneWire_LeavesEveryOtherWireWhereItWas()
    {
        var schematic = KiCadSchematic.Load(TestData.Rs485Bridge);
        var wire = schematic.Wires[0];
        Assert.Equal(new KiCadPosition(45.72, 43.18), wire.Start);

        wire.Start = new KiCadPosition(45.72, 43.19);

        var output = Path.Combine(TestData.NewScratchDirectory(), "orbion-rs485-bridge.kicad_sch");
        schematic.Save(output);

        // "43.18" became "43.19": one digit, so the file is the same length.
        Assert.Equal(170_001, new FileInfo(output).Length);

        var reloaded = KiCadSchematic.Load(output);
        Assert.Equal(161, reloaded.Wires.Count);
        Assert.Equal(new KiCadPosition(45.72, 43.19), reloaded.Wires[0].Start);
        Assert.Equal(new KiCadPosition(48.26, 43.18), reloaded.Wires[0].End);
        Assert.Equal(new KiCadPosition(45.72, 45.72), reloaded.Wires[1].Start);
    }

    // ------------------------------------------------------------------------- reading detail

    [Fact]
    public void Wire_ReadsItsGeometryAndStroke()
    {
        var wire = KiCadSchematic.Load(TestData.Rs485Bridge).Wires[0];

        Assert.Equal(2, wire.Points.Count);
        Assert.Equal(new KiCadPosition(45.72, 43.18), wire.Start);
        Assert.Equal(new KiCadPosition(48.26, 43.18), wire.End);
        Assert.Equal(0, wire.Stroke!.Width);
        Assert.Equal("default", wire.Stroke!.Type);
        Assert.Equal("d56dec2a-7edc-584a-8176-2a565fbedb6a", wire.Uuid);
    }

    [Fact]
    public void Label_ReadsItsTextPositionAndEffects()
    {
        var label = KiCadSchematic.Load(TestData.Rs485Bridge).Labels[0];

        Assert.Equal("JTAG_TMS", label.Text);
        Assert.Equal(new KiCadPosition(321.31, 38.1, 0), label.Position);
        Assert.Equal(new KiCadSize(1.27, 1.27), label.FontEffects!.Size);
        Assert.False(label.FontEffects!.Bold);
        Assert.Equal("196c28e7-92d5-5472-bb70-a7838bb30945", label.Uuid);
    }

    [Fact]
    public void Junction_ReadsItsPositionDiameterAndColour()
    {
        var junction = KiCadSchematic.Load(TestData.Rs485Bridge).Junctions[0];

        Assert.Equal(new KiCadPosition(55.88, 43.18), junction.Position);

        // 0 and an all-zero colour are what KiCad writes for "use the theme", not missing values.
        Assert.Equal(0, junction.Diameter);
        Assert.Equal(new[] { "0", "0", "0", "0" }, junction.Color);
        Assert.Equal("b5b97eac-a03f-5693-9381-1de7d67a9160", junction.Uuid);
    }

    [Fact]
    public void SchematicText_ReadsItsTextAndTheFlagsASheetAdds()
    {
        var text = KiCadSchematic.Load(TestData.Rs485Bridge).TextItems[0];

        Assert.Equal("+3V3 LDO", text.Text);
        Assert.Equal(new KiCadPosition(228.6, 24.77, 0), text.Position);
        Assert.Equal(new KiCadSize(2.54, 2.54), text.FontEffects!.Size);
        Assert.True(text.FontEffects!.Bold);
        Assert.True(text.ExcludeFromSim);
        Assert.Equal("3d151429-9a7a-5a94-8de1-e061cc744a42", text.Uuid);
    }

    [Fact]
    public void Rectangle_OnASheet_IsTheSameViewASymbolUses()
    {
        var rectangle = KiCadSchematic.Load(TestData.Rs485Bridge).Rectangles[0];

        Assert.Equal(new KiCadPosition(226.06, 20.32), rectangle.Start);
        Assert.Equal(new KiCadPosition(287.02, 63.5), rectangle.End);
        Assert.Equal(0.254, rectangle.Stroke!.Width);
        Assert.Equal("dash", rectangle.Stroke!.Type);
        Assert.Equal("none", rectangle.Fill!.Type);

        // The shared view does not model the uuid a sheet shape carries; it is still in the file.
        Assert.Equal("35a1bd8c-8357-5e11-9db6-0d9d1f6170ba", rectangle.Node.GetChildValue("uuid"));
    }

    [Fact]
    public void TitleBlock_ReadsTheOneFieldTheFileSetsAndNullsTheRest()
    {
        var titleBlock = KiCadSchematic.Load(TestData.Rs485Bridge).TitleBlock!;

        Assert.Equal("Orbion RS485 field bus node", titleBlock.Title);
        Assert.Null(titleBlock.Date);
        Assert.Null(titleBlock.Revision);
        Assert.Null(titleBlock.Company);
        Assert.Empty(titleBlock.CommentNumbers);
        Assert.Null(titleBlock.GetComment(1));
    }

    [Fact]
    public void BusAlias_ReadsItsMembers()
    {
        var alias = KiCadSchematic.Load(TestData.Rs485Bridge).BusAliases[0];

        Assert.Equal("I2C0", alias.Name);
        Assert.Equal(new[] { "I2C0_SCL", "I2C0_SDA" }, alias.Members);
    }

    [Fact]
    public void SheetInstances_ReadsThePageMap()
    {
        var instance = KiCadSchematic.Load(TestData.Rs485Bridge).SheetInstances[0];

        Assert.Equal("/", instance.Path);
        Assert.Equal("1", instance.Page);
    }

    [Fact]
    public void LibrarySymbols_AreTheSameViewAKicadSymFileUses()
    {
        var schematic = KiCadSchematic.Load(TestData.Rs485Bridge);

        Assert.Equal(19, schematic.LibrarySymbols.Count);
        Assert.Equal("orbion:+3V3", schematic.LibrarySymbols[0].Id);

        var resistor = schematic.LibrarySymbols.Single(s => s.Id == "orbion:R");
        Assert.Equal(2, resistor.Units.Count);
        Assert.Equal(2, resistor.Pins.Count);
        Assert.Equal(new[] { "1", "2" }, resistor.Pins.Select(p => p.Number));
        Assert.Equal("R", resistor.GetPropertyValue("Reference"));
        Assert.Equal("Resistor", resistor.GetPropertyValue("Description"));
        Assert.Single(resistor.Units[0].GraphicalItems.OfType<KiCadRectangle>());

        // The cache is definitions; the sheet's own (symbol ...) children are the placements.
        Assert.Equal(74, schematic.Symbols.Count);
        Assert.Equal("orbion:Conn_01x02", schematic.Symbols[0].LibId);
        Assert.Equal("J90", schematic.Symbols[0].ReferenceProperty);
    }

    [Fact]
    public void ChildSheetFixture_ReadsTheSameCountsThroughTheViews()
    {
        var root = SExpression.Load(TestData.DuplicateRefsChild);
        Assert.Equal(35, root.GetChildren("wire").Count());
        Assert.Equal(6, root.GetChildren("junction").Count());
        Assert.Equal(4, root.GetChildren("label").Count());
        Assert.Equal(14, root.GetChildren("symbol").Count());
        Assert.Equal(9, root.GetChild("lib_symbols")!.GetChildren("symbol").Count());

        var schematic = KiCadSchematic.Load(TestData.DuplicateRefsChild);
        Assert.Equal(35, schematic.Wires.Count);
        Assert.Equal(6, schematic.Junctions.Count);
        Assert.Equal(4, schematic.Labels.Count);
        Assert.Equal(14, schematic.Symbols.Count);
        Assert.Equal(9, schematic.LibrarySymbols.Count);
        Assert.Equal("VIN", schematic.Labels[0].Text);
        Assert.Equal("Protected DC power input", schematic.TitleBlock!.Title);
        Assert.Equal(new KiCadPosition(27.94, 50.8), schematic.Wires[0].Start);
    }

    // --------------------------------------------------------------------------- the round trip

    [Fact]
    public void Schematic_ModifyThroughTheViews_SaveAndReload_ReadsBackTheChanges()
    {
        var schematic = KiCadSchematic.Load(TestData.Rs485Bridge);

        schematic.Labels[0].Text = "JTAG_TMS_RENAMED";
        schematic.Junctions[0].Diameter = 0.9144;
        schematic.TitleBlock!.Revision = "B";
        schematic.TitleBlock!.Company = "Gasoleo Technology";
        schematic.TitleBlock!.SetComment(1, "Round-tripped by KiCadSharp");
        schematic.SheetInstances[0].Page = "7";
        schematic.Symbols[0].ReferenceProperty = "J91";

        var output = Path.Combine(TestData.NewScratchDirectory(), "orbion-rs485-bridge.kicad_sch");
        schematic.Save(output);

        var reloaded = KiCadSchematic.Load(output);

        Assert.Equal("JTAG_TMS_RENAMED", reloaded.Labels[0].Text);
        Assert.Equal(0.9144, reloaded.Junctions[0].Diameter);
        Assert.Equal("B", reloaded.TitleBlock!.Revision);
        Assert.Equal("Gasoleo Technology", reloaded.TitleBlock!.Company);
        Assert.Equal("Orbion RS485 field bus node", reloaded.TitleBlock!.Title);
        Assert.Equal("Round-tripped by KiCadSharp", reloaded.TitleBlock!.GetComment(1));
        Assert.Equal(new[] { 1 }, reloaded.TitleBlock!.CommentNumbers);
        Assert.Equal("7", reloaded.SheetInstances[0].Page);
        Assert.Equal("J91", reloaded.Symbols[0].ReferenceProperty);

        // Everything that was not touched is still there, in the same numbers.
        Assert.Equal(161, reloaded.Wires.Count);
        Assert.Equal(22, reloaded.Labels.Count);
        Assert.Equal(18, reloaded.Junctions.Count);
        Assert.Equal(8, reloaded.TextItems.Count);
        Assert.Equal(8, reloaded.Rectangles.Count);
        Assert.Equal(4, reloaded.BusAliases.Count);
        Assert.Equal(74, reloaded.Symbols.Count);
        Assert.Equal(19, reloaded.LibrarySymbols.Count);
    }

    [Fact]
    public void SetComment_AddsRemovesAndRewritesOneNumberedSlot()
    {
        var titleBlock = KiCadSchematic.Load(TestData.Rs485Bridge).TitleBlock!;

        titleBlock.SetComment(4, "fourth");
        titleBlock.SetComment(1, "first");
        Assert.Equal("fourth", titleBlock.GetComment(4));
        Assert.Equal("first", titleBlock.GetComment(1));

        // The slots are numbered, not ordered: 4 was written first and stays first.
        Assert.Equal(new[] { 4, 1 }, titleBlock.CommentNumbers);

        titleBlock.SetComment(4, "rewritten");
        Assert.Equal("rewritten", titleBlock.GetComment(4));
        Assert.Equal(new[] { 4, 1 }, titleBlock.CommentNumbers);

        titleBlock.SetComment(4, null);
        Assert.Null(titleBlock.GetComment(4));
        Assert.Equal(new[] { 1 }, titleBlock.CommentNumbers);
    }

    // ------------------------------------------------- the tokens the corpus does not happen to hold

    [Fact]
    public void RicherSheet_Parses_AndWritesBackUnchanged()
    {
        var schematic = KiCadSchematic.Parse(RicherSheet);

        Assert.False(schematic.IsModified);
        ReadEverything(schematic);
        Assert.False(schematic.IsModified);
        Assert.Equal(RicherSheet, schematic.ToText());
    }

    [Fact]
    public void RicherSheet_ReadsTheConnectivityTokens()
    {
        var schematic = KiCadSchematic.Parse(RicherSheet);

        var bus = Assert.Single(schematic.Buses);
        Assert.Equal(new KiCadPosition(101.6, 50.8), bus.Start);
        Assert.Equal(new KiCadPosition(127, 50.8), bus.End);
        Assert.Equal(0.4, bus.Stroke!.Width);
        Assert.Equal("2a0f4a13-0000-4000-8000-000000000001", bus.Uuid);

        var entry = Assert.Single(schematic.BusEntries);
        Assert.Equal(new KiCadPosition(127, 50.8), entry.Position);
        Assert.Equal(new KiCadSize(2.54, 2.54), entry.Size);
        Assert.Equal("default", entry.Stroke!.Type);

        var noConnect = Assert.Single(schematic.NoConnects);
        Assert.Equal(new KiCadPosition(60.96, 30.48), noConnect.Position);
        Assert.Equal("2a0f4a13-0000-4000-8000-000000000003", noConnect.Uuid);
    }

    [Fact]
    public void RicherSheet_ReadsTheLabelFamily()
    {
        var schematic = KiCadSchematic.Parse(RicherSheet);

        var global = Assert.Single(schematic.GlobalLabels);
        Assert.Equal("RESET", global.Text);
        Assert.Equal("input", global.Shape);
        Assert.Equal(new KiCadPosition(50.8, 25.4, 0), global.Position);
        Assert.Single(global.Properties);
        Assert.Equal("${INTERSHEET_REFS}", global.GetPropertyValue("Intersheetrefs"));

        var hierarchical = Assert.Single(schematic.HierarchicalLabels);
        Assert.Equal("SPI_CS", hierarchical.Text);
        Assert.Equal("output", hierarchical.Shape);
        Assert.Equal(new KiCadPosition(76.2, 38.1, 180), hierarchical.Position);

        var flag = Assert.Single(schematic.NetClassFlags);
        Assert.Equal("HighVoltage", flag.Text);
        Assert.Equal("round", flag.Shape);
        Assert.Equal(2.54, flag.Length);
        Assert.Empty(flag.Properties);
    }

    [Fact]
    public void RicherSheet_ReadsTheDrawnItems()
    {
        var schematic = KiCadSchematic.Parse(RicherSheet);

        var box = Assert.Single(schematic.TextBoxes);
        Assert.Equal("Bench notes", box.Text);
        Assert.Equal(new KiCadPosition(20.32, 20.32, 0), box.Position);
        Assert.Equal(new KiCadSize(40.64, 15.24), box.Size);
        Assert.Equal("solid", box.Stroke!.Type);
        Assert.Equal("none", box.Fill!.Type);
        Assert.Equal(new KiCadSize(1.27, 1.27), box.FontEffects!.Size);

        var bezier = Assert.Single(schematic.Beziers);
        Assert.Equal(4, bezier.ControlPoints.Count);
        Assert.Equal(new KiCadPosition(0, 0), bezier.ControlPoints[0]);
        Assert.Equal(new KiCadPosition(10.16, 0), bezier.ControlPoints[3]);

        var polyline = Assert.Single(schematic.Polylines);
        Assert.Equal(3, polyline.Points.Count);

        var circle = Assert.Single(schematic.Circles);
        Assert.Equal(new KiCadPosition(25.4, 25.4), circle.Center);
        Assert.Equal(5.08, circle.Radius);

        var arc = Assert.Single(schematic.Arcs);
        Assert.Equal(new KiCadPosition(0, 0), arc.Start);
        Assert.Equal(new KiCadPosition(5.08, 5.08), arc.Mid);
        Assert.Equal(new KiCadPosition(10.16, 0), arc.End);
    }

    [Fact]
    public void RicherSheet_ReadsAnImageWithoutDecodingIt()
    {
        var image = Assert.Single(KiCadSchematic.Parse(RicherSheet).Images);

        Assert.Equal(new KiCadPosition(30.48, 60.96), image.Position);
        Assert.Equal(2, image.Scale);
        Assert.Equal("2a0f4a13-0000-4000-8000-000000000010", image.Uuid);

        // The payload is joined back into one string, exactly as spelled, and never decoded.
        Assert.Equal(new[] { "iVBORw0KGgoAAAANSUhEUg", "AAAAEAAAABCAYAAAAfFcSJ" }, image.DataAtoms);
        Assert.Equal("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ", image.Data);
    }

    [Fact]
    public void SheetPins_ReadTheParentHalfOfAHierarchicalConnection()
    {
        var sheet = Assert.Single(KiCadSchematic.Parse(RicherSheet).Sheets);

        Assert.Equal("IO", sheet.SheetName);
        Assert.Equal("io.kicad_sch", sheet.SheetFile);
        Assert.Equal(2, sheet.Pins.Count);

        Assert.Equal("SPI_CS", sheet.Pins[0].Name);
        Assert.Equal("input", sheet.Pins[0].Shape);
        Assert.Equal(new KiCadPosition(190.5, 38.1, 0), sheet.Pins[0].Position);
        Assert.Equal(new KiCadSize(1.27, 1.27), sheet.Pins[0].FontEffects!.Size);
        Assert.Equal("2a0f4a13-0000-4000-8000-000000000021", sheet.Pins[0].Uuid);

        Assert.Equal("IRQ", sheet.Pins[1].Name);
        Assert.Equal("output", sheet.Pins[1].Shape);
    }

    [Fact]
    public void RicherSheet_ReadsATitleBlockThatSetsEveryField()
    {
        var titleBlock = KiCadSchematic.Parse(RicherSheet).TitleBlock!;

        Assert.Equal("Everything sheet", titleBlock.Title);
        Assert.Equal("2026-09-07", titleBlock.Date);
        Assert.Equal("A", titleBlock.Revision);
        Assert.Equal("Gasoleo Technology", titleBlock.Company);
        Assert.Equal(new[] { 1, 3 }, titleBlock.CommentNumbers);
        Assert.Equal("first comment", titleBlock.GetComment(1));
        Assert.Equal("third comment", titleBlock.GetComment(3));

        // Slot 2 is skipped in the file, which is why comments are addressed by number.
        Assert.Null(titleBlock.GetComment(2));
    }

    [Fact]
    public void RicherSheet_OneEditKeepsEveryOtherToken()
    {
        var schematic = KiCadSchematic.Parse(RicherSheet);
        schematic.GlobalLabels[0].Shape = "bidirectional";

        var after = schematic.ToText();

        Assert.Equal(
            RicherSheet.Replace("(shape input)", "(shape bidirectional)", StringComparison.Ordinal),
            after);

        var reloaded = KiCadSchematic.Parse(after);
        Assert.Equal("bidirectional", reloaded.GlobalLabels[0].Shape);
        Assert.Single(reloaded.Buses);
        Assert.Single(reloaded.Images);
        Assert.Equal(2, reloaded.Sheets[0].Pins.Count);
    }

    // ------------------------------------------------------------------------------- the helpers

    /// <summary>
    /// Touches every view a schematic exposes, so a round-trip test is a statement about reading the
    /// whole file rather than about reading the two properties the test happened to name.
    /// </summary>
    private static void ReadEverything(KiCadSchematic schematic)
    {
        _ = schematic.Version;
        _ = schematic.Generator;
        _ = schematic.GeneratorVersion;
        _ = schematic.Paper;
        _ = schematic.Uuid;
        _ = schematic.EmbeddedFonts;

        var titleBlock = schematic.TitleBlock;
        _ = titleBlock?.Title;
        _ = titleBlock?.Date;
        _ = titleBlock?.Revision;
        _ = titleBlock?.Company;
        _ = titleBlock?.CommentNumbers;
        _ = titleBlock?.GetComment(1);

        foreach (var line in schematic.Wires.Cast<KiCadSchematicLine>().Concat(schematic.Buses))
        {
            _ = line.Points;
            _ = line.Start;
            _ = line.End;
            _ = line.Stroke?.Width;
            _ = line.Uuid;
        }

        foreach (var entry in schematic.BusEntries)
        {
            _ = entry.Position;
            _ = entry.Size;
            _ = entry.Stroke?.Type;
            _ = entry.Uuid;
        }

        foreach (var alias in schematic.BusAliases)
        {
            _ = alias.Name;
            _ = alias.Members;
        }

        foreach (var junction in schematic.Junctions)
        {
            _ = junction.Position;
            _ = junction.Diameter;
            _ = junction.Color;
            _ = junction.Uuid;
        }

        foreach (var noConnect in schematic.NoConnects)
        {
            _ = noConnect.Position;
            _ = noConnect.Uuid;
        }

        foreach (var label in schematic.Labels.Cast<KiCadSchematicLabel>()
                     .Concat(schematic.GlobalLabels)
                     .Concat(schematic.HierarchicalLabels)
                     .Concat(schematic.NetClassFlags))
        {
            _ = label.Text;
            _ = label.Position;
            _ = label.FontEffects?.Size;
            _ = label.Uuid;
        }

        foreach (var text in schematic.TextItems)
        {
            _ = text.Text;
            _ = text.Position;
            _ = text.FontEffects?.Bold;
            _ = text.ExcludeFromSim;
            _ = text.Uuid;
        }

        foreach (var box in schematic.TextBoxes)
        {
            _ = box.Text;
            _ = box.Position;
            _ = box.Size;
            _ = box.Stroke?.Width;
            _ = box.Fill?.Type;
            _ = box.FontEffects?.Size;
            _ = box.Uuid;
        }

        foreach (var polyline in schematic.Polylines)
        {
            _ = polyline.Points;
            _ = polyline.Stroke?.Type;
            _ = polyline.Fill?.Type;
        }

        foreach (var rectangle in schematic.Rectangles)
        {
            _ = rectangle.Start;
            _ = rectangle.End;
            _ = rectangle.Stroke?.Width;
            _ = rectangle.Fill?.Type;
        }

        foreach (var circle in schematic.Circles)
        {
            _ = circle.Center;
            _ = circle.Radius;
            _ = circle.Stroke?.Width;
            _ = circle.Fill?.Type;
        }

        foreach (var arc in schematic.Arcs)
        {
            _ = arc.Start;
            _ = arc.Mid;
            _ = arc.End;
            _ = arc.Stroke?.Width;
            _ = arc.Fill?.Type;
        }

        foreach (var bezier in schematic.Beziers)
        {
            _ = bezier.ControlPoints;
            _ = bezier.Stroke?.Width;
            _ = bezier.Fill?.Type;
            _ = bezier.Uuid;
        }

        foreach (var image in schematic.Images)
        {
            _ = image.Position;
            _ = image.Scale;
            _ = image.Data;
            _ = image.Uuid;
        }

        foreach (var symbol in schematic.Symbols)
        {
            _ = symbol.LibId;
            _ = symbol.Unit;
            _ = symbol.Uuid;
            _ = symbol.ReferenceProperty;
            _ = symbol.IsPowerSymbol;
            foreach (var property in symbol.Properties)
            {
                _ = property.Key;
                _ = property.Value;
                _ = property.Position;
                _ = property.FontEffects?.Size;
            }
        }

        foreach (var librarySymbol in schematic.LibrarySymbols)
        {
            _ = librarySymbol.Id;
            _ = librarySymbol.Units.Count;
            _ = librarySymbol.Pins.Count;
            _ = librarySymbol.GraphicalItems.Count;
            _ = librarySymbol.GetPropertyValue("Reference");
            foreach (var item in librarySymbol.GraphicalItems)
            {
                _ = item.Stroke?.Width;
                _ = item.Fill?.Type;
            }
        }

        foreach (var sheet in schematic.Sheets)
        {
            _ = sheet.Uuid;
            _ = sheet.SheetName;
            _ = sheet.SheetFile;
            foreach (var pin in sheet.Pins)
            {
                _ = pin.Name;
                _ = pin.Shape;
                _ = pin.Position;
                _ = pin.FontEffects?.Size;
                _ = pin.Uuid;
            }
        }

        foreach (var instance in schematic.SheetInstances)
        {
            _ = instance.Path;
            _ = instance.Page;
        }
    }

    private static IEnumerable<int> Occurrences(string text, string needle)
    {
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, StringComparison.Ordinal))
        {
            yield return i;
        }
    }

    /// <summary>
    /// A sheet in KiCad 10's own spelling holding one of every form the vendored corpus lacks. It is
    /// written as lines rather than as one literal so the tabs are unmistakable: the writer
    /// reproduces an unmodified form byte for byte, and a stray space would make that assertion pass
    /// for the wrong reason.
    /// </summary>
    private static readonly string RicherSheet = string.Join('\n',
        "(kicad_sch",
        "\t(version 20250114)",
        "\t(generator \"eeschema\")",
        "\t(generator_version \"10.0\")",
        "\t(uuid \"2a0f4a13-0000-4000-8000-000000000000\")",
        "\t(paper \"A4\")",
        "\t(title_block",
        "\t\t(title \"Everything sheet\")",
        "\t\t(date \"2026-09-07\")",
        "\t\t(rev \"A\")",
        "\t\t(company \"Gasoleo Technology\")",
        "\t\t(comment 1 \"first comment\")",
        "\t\t(comment 3 \"third comment\")",
        "\t)",
        "\t(lib_symbols)",
        "\t(bus",
        "\t\t(pts",
        "\t\t\t(xy 101.6 50.8) (xy 127 50.8)",
        "\t\t)",
        "\t\t(stroke (width 0.4) (type default))",
        "\t\t(uuid \"2a0f4a13-0000-4000-8000-000000000001\")",
        "\t)",
        "\t(bus_entry",
        "\t\t(at 127 50.8)",
        "\t\t(size 2.54 2.54)",
        "\t\t(stroke (width 0) (type default))",
        "\t\t(uuid \"2a0f4a13-0000-4000-8000-000000000002\")",
        "\t)",
        "\t(no_connect",
        "\t\t(at 60.96 30.48)",
        "\t\t(uuid \"2a0f4a13-0000-4000-8000-000000000003\")",
        "\t)",
        "\t(global_label \"RESET\"",
        "\t\t(shape input)",
        "\t\t(at 50.8 25.4 0)",
        "\t\t(fields_autoplaced yes)",
        "\t\t(effects (font (size 1.27 1.27)) (justify left))",
        "\t\t(uuid \"2a0f4a13-0000-4000-8000-000000000004\")",
        "\t\t(property \"Intersheetrefs\" \"${INTERSHEET_REFS}\"",
        "\t\t\t(at 45.72 25.4 0)",
        "\t\t\t(effects (font (size 1.27 1.27)) (hide yes))",
        "\t\t)",
        "\t)",
        "\t(hierarchical_label \"SPI_CS\"",
        "\t\t(shape output)",
        "\t\t(at 76.2 38.1 180)",
        "\t\t(effects (font (size 1.27 1.27)) (justify right))",
        "\t\t(uuid \"2a0f4a13-0000-4000-8000-000000000005\")",
        "\t)",
        "\t(netclass_flag \"HighVoltage\"",
        "\t\t(length 2.54)",
        "\t\t(shape round)",
        "\t\t(at 88.9 44.45 0)",
        "\t\t(effects (font (size 1.27 1.27)) (justify left bottom))",
        "\t\t(uuid \"2a0f4a13-0000-4000-8000-000000000006\")",
        "\t)",
        "\t(text_box \"Bench notes\"",
        "\t\t(exclude_from_sim no)",
        "\t\t(at 20.32 20.32 0)",
        "\t\t(size 40.64 15.24)",
        "\t\t(stroke (width 0.1) (type solid))",
        "\t\t(fill (type none))",
        "\t\t(effects (font (size 1.27 1.27)) (justify left top))",
        "\t\t(uuid \"2a0f4a13-0000-4000-8000-000000000007\")",
        "\t)",
        "\t(polyline",
        "\t\t(pts",
        "\t\t\t(xy 0 0) (xy 10.16 0) (xy 10.16 10.16)",
        "\t\t)",
        "\t\t(stroke (width 0.1) (type solid))",
        "\t\t(fill (type none))",
        "\t\t(uuid \"2a0f4a13-0000-4000-8000-000000000008\")",
        "\t)",
        "\t(bezier",
        "\t\t(pts",
        "\t\t\t(xy 0 0) (xy 2.54 5.08) (xy 7.62 5.08) (xy 10.16 0)",
        "\t\t)",
        "\t\t(stroke (width 0.1) (type default))",
        "\t\t(fill (type none))",
        "\t\t(uuid \"2a0f4a13-0000-4000-8000-000000000009\")",
        "\t)",
        "\t(circle",
        "\t\t(center 25.4 25.4)",
        "\t\t(radius 5.08)",
        "\t\t(stroke (width 0.2) (type default))",
        "\t\t(fill (type none))",
        "\t\t(uuid \"2a0f4a13-0000-4000-8000-00000000000a\")",
        "\t)",
        "\t(arc",
        "\t\t(start 0 0)",
        "\t\t(mid 5.08 5.08)",
        "\t\t(end 10.16 0)",
        "\t\t(stroke (width 0.2) (type default))",
        "\t\t(fill (type none))",
        "\t\t(uuid \"2a0f4a13-0000-4000-8000-00000000000b\")",
        "\t)",
        "\t(image",
        "\t\t(at 30.48 60.96)",
        "\t\t(scale 2)",
        "\t\t(uuid \"2a0f4a13-0000-4000-8000-000000000010\")",
        "\t\t(data",
        "\t\t\t\"iVBORw0KGgoAAAANSUhEUg\"",
        "\t\t\t\"AAAAEAAAABCAYAAAAfFcSJ\"",
        "\t\t)",
        "\t)",
        "\t(sheet",
        "\t\t(at 190.5 30.48)",
        "\t\t(size 40.64 25.4)",
        "\t\t(stroke (width 0.1524) (type solid))",
        "\t\t(fill (color 0 0 0 0.0000))",
        "\t\t(uuid \"2a0f4a13-0000-4000-8000-000000000020\")",
        "\t\t(property \"Sheetname\" \"IO\"",
        "\t\t\t(at 190.5 29.7684 0)",
        "\t\t\t(effects (font (size 1.27 1.27)) (justify left bottom))",
        "\t\t)",
        "\t\t(property \"Sheetfile\" \"io.kicad_sch\"",
        "\t\t\t(at 190.5 56.3626 0)",
        "\t\t\t(effects (font (size 1.27 1.27)) (justify left top))",
        "\t\t)",
        "\t\t(pin \"SPI_CS\" input",
        "\t\t\t(at 190.5 38.1 0)",
        "\t\t\t(effects (font (size 1.27 1.27)) (justify right))",
        "\t\t\t(uuid \"2a0f4a13-0000-4000-8000-000000000021\")",
        "\t\t)",
        "\t\t(pin \"IRQ\" output",
        "\t\t\t(at 190.5 40.64 0)",
        "\t\t\t(effects (font (size 1.27 1.27)) (justify right))",
        "\t\t\t(uuid \"2a0f4a13-0000-4000-8000-000000000022\")",
        "\t\t)",
        "\t)",
        "\t(sheet_instances",
        "\t\t(path \"/\"",
        "\t\t\t(page \"1\")",
        "\t\t)",
        "\t)",
        "\t(embedded_fonts no)",
        ")",
        string.Empty);
}
