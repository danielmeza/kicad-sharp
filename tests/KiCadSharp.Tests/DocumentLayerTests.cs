using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// The document layer, over real KiCad 10 files.
/// </summary>
/// <remarks>
/// <para>
/// These were characterisation tests: they asserted 0 pins read, 38,055 bytes written and 0
/// footprints found, because that is what the library did. The numbers here are the same
/// measurements taken again after the document layer became a view over the s-expression tree
/// rather than a parallel model that had to be serialised back.
/// </para>
/// <para>
/// The property they all really test is one property: after a save, the file keeps its original
/// content and only the part that was meant to change has changed.
/// </para>
/// </remarks>
public class DocumentLayerTests
{
    // --------------------------------------------------------------------- the fixtures themselves

    [Fact]
    public void SymbolLibraryFixture_IsTheMeasuredFile()
    {
        Assert.Equal(108_583, new FileInfo(TestData.SymbolLibrary).Length);

        // Counted from the s-expression tree, which has no model in between to lose anything.
        var root = SExpression.Load(TestData.SymbolLibrary);
        Assert.Equal("kicad_symbol_lib", root.Token);
        Assert.Equal(35, root.GetChildren("symbol").Count());
        Assert.Equal(67, root.GetChildren("symbol").SelectMany(s => s.GetChildren("symbol")).Count());
        Assert.Equal(112, root.Descendants("pin").Count());
    }

    // -------------------------------------------------------------------------- sub-units are read

    [Fact]
    public void SymbolLibrary_ReadsEveryPin_ThroughTheSubUnits()
    {
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);

        Assert.Equal(35, library.Symbols.Count);
        Assert.Equal(67, library.Symbols.Sum(s => s.Units.Count));

        // Was 0: KiCad 6+ puts the pins inside a nested (symbol "R_0_1" ...) unit.
        Assert.Equal(112, library.Symbols.Sum(s => s.Pins.Count));
    }

    [Fact]
    public void SymbolLibrary_ReadsPinDetail()
    {
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var connector = library.GetSymbol("Conn_01x02");

        Assert.NotNull(connector);
        Assert.Single(connector.Units);
        Assert.Equal("Conn_01x02_1_1", connector.Units[0].Id);
        Assert.Equal(1, connector.Units[0].Unit);
        Assert.Equal(1, connector.Units[0].BodyStyle);

        Assert.Equal(new[] { "1", "2" }, connector.Pins.Select(p => p.Number));
        Assert.Equal(new[] { "Pin_1", "Pin_2" }, connector.Pins.Select(p => p.Name));
        Assert.Equal("passive", connector.Pins[0].Type);
        Assert.Equal("line", connector.Pins[0].Style);
        Assert.Equal(3.81, connector.Pins[0].Length);
        Assert.Equal(new KiCadPosition(-5.08, 0, 0), connector.Pins[0].Position);

        Assert.Equal("J", connector.GetPropertyValue("Reference"));
        Assert.Equal(3, connector.Units[0].GraphicalItems.OfType<KiCadRectangle>().Count());
    }

    // ------------------------------------------------------------------- an untouched save is a copy

    [Fact]
    public void SymbolLibrary_Save_WithoutChanges_IsByteIdentical()
    {
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var output = Path.Combine(TestData.NewScratchDirectory(), "orbion.kicad_sym");

        library.Save(output);

        // Was 38,055 bytes: the 35 symbols kept their names and properties and every sub-unit, pin
        // and graphic was gone.
        Assert.Equal(108_583, new FileInfo(output).Length);
        Assert.Equal(File.ReadAllBytes(TestData.SymbolLibrary), File.ReadAllBytes(output));
    }

    [Fact]
    public void SymbolLibrary_Save_AfterOneEdit_ChangesOnlyThatEdit()
    {
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var connector = library.GetSymbol("Conn_01x02")!;
        connector.AddProperty("Reference", "P");

        var output = Path.Combine(TestData.NewScratchDirectory(), "orbion.kicad_sym");
        library.Save(output);

        var before = File.ReadAllText(TestData.SymbolLibrary);
        var after = File.ReadAllText(output);

        // One quoted "J" became a quoted "P": same length, one byte different.
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(1, before.Zip(after).Count(pair => pair.First != pair.Second));

        // And it still reads back as a library, with everything else intact.
        var reloaded = KiCadSymbolLibrary.Load(output);
        Assert.Equal(35, reloaded.Symbols.Count);
        Assert.Equal(112, reloaded.Symbols.Sum(s => s.Pins.Count));
        Assert.Equal("P", reloaded.GetSymbol("Conn_01x02")!.GetPropertyValue("Reference"));
    }

    // ----------------------------------------------- a standalone .kicad_mod is a footprint at root

    [Fact]
    public void FootprintLibrary_Load_FindsTheFootprintInAStandaloneKicadMod()
    {
        // The old fallback only fired when the root token was `module`, the KiCad 5 spelling.
        Assert.Equal("footprint", SExpression.Load(TestData.Footprint).Token);

        var library = KiCadFootprintLibrary.Load(TestData.Footprint);

        Assert.True(library.IsSingleFootprint);
        Assert.Single(library.Footprints);
        Assert.Equal("LED_0603_1608Metric", library.Footprints[0].Id);
        Assert.NotNull(library.GetFootprint("LED_0603_1608Metric"));
    }

    [Fact]
    public void FootprintLibrary_Load_RecognisesTheKiCad5ModuleRootToo()
    {
        var library = KiCadFootprintLibrary.Parse("(module TEST (layer F.Cu) (fp_line (start 0 0) (end 1 1)))");

        Assert.True(library.IsSingleFootprint);
        Assert.Single(library.Footprints);
        Assert.Equal("TEST", library.Footprints[0].Id);
    }

    [Fact]
    public void Footprint_ReadsTheTokensTheOldModelDidNotHave()
    {
        var footprint = KiCadFootprintLibrary.Load(TestData.Footprint).Footprints[0];

        Assert.StartsWith("LED SMD 0603 (1608 Metric)", footprint.Description);
        Assert.Equal("LED", footprint.Tags);
        Assert.Equal("F.Cu", footprint.Layer);
        Assert.Equal(new[] { "smd" }, footprint.Attributes);
        Assert.Equal(3, footprint.Properties.Count);
        Assert.Equal("REF**", footprint.GetPropertyValue("Reference"));
        Assert.Single(footprint.Rectangles);
        Assert.Equal(8, footprint.Lines.Count);
        Assert.Equal(2, footprint.Pads.Count);
        Assert.Single(footprint.TextItems);
        Assert.Single(footprint.Models);

        var pad = footprint.Pads[0];
        Assert.Equal("1", pad.Number);
        Assert.Equal("smd", pad.Type);
        Assert.Equal("roundrect", pad.Shape);
        Assert.Equal(new KiCadPosition(-0.7875, 0), pad.Position);
        Assert.Equal(new KiCadSize(0.875, 0.95), pad.Size);
        Assert.Equal(new[] { "F.Cu", "F.Mask", "F.Paste" }, pad.Layers);
        Assert.Null(pad.Drill);

        Assert.Equal(new KiCadXyz(1, 1, 1), footprint.Models[0].Scale);
    }

    // ------------------------------------------------------ a footprint round trip drops nothing

    [Fact]
    public void Footprint_Save_KeepsEveryTokenTheModelDoesNotName()
    {
        var library = KiCadFootprintLibrary.Load(TestData.Footprint);
        var output = Path.Combine(TestData.NewScratchDirectory(), "LED_0603_1608Metric.kicad_mod");

        library.Save(output);

        Assert.Equal(2_437, new FileInfo(output).Length);
        Assert.Equal(File.ReadAllBytes(TestData.Footprint), File.ReadAllBytes(output));

        // Four of the tokens a rebuild-from-model Save used to drop, plus two nobody modelled at all.
        var saved = SExpression.Load(output);
        Assert.NotEmpty(saved.GetChildren("fp_rect"));
        Assert.Equal(3, saved.GetChildren("property").Count());
        Assert.NotNull(saved.GetChild("descr"));
        Assert.NotNull(saved.GetChild("tags"));
        Assert.NotNull(saved.GetChild("embedded_fonts"));
        Assert.NotNull(saved.GetChild("duplicate_pad_numbers_are_jumpers"));
    }

    [Fact]
    public void Footprint_Save_AfterOneEdit_KeepsEverythingElse()
    {
        var library = KiCadFootprintLibrary.Load(TestData.Footprint);
        library.Footprints[0].Tags = "LED SMD";

        var output = Path.Combine(TestData.NewScratchDirectory(), "LED_0603_1608Metric.kicad_mod");
        library.Save(output);

        // 2,437 bytes plus the four characters the tag grew by.
        Assert.Equal(2_441, new FileInfo(output).Length);

        var reloaded = KiCadFootprintLibrary.Load(output).Footprints[0];
        Assert.Equal("LED SMD", reloaded.Tags);
        Assert.Single(reloaded.Rectangles);
        Assert.Equal(8, reloaded.Lines.Count);
        Assert.Equal(2, reloaded.Pads.Count);
        Assert.Equal(3, reloaded.Properties.Count);
        Assert.NotNull(reloaded.Description);
    }

    [Fact]
    public void SaveFootprint_WritesOneFootprintOutOfABoard()
    {
        var footprint = KiCadFootprintLibrary.Load(TestData.Footprint).Footprints[0];
        var output = Path.Combine(TestData.NewScratchDirectory(), "extracted.kicad_mod");

        KiCadFootprintLibrary.SaveFootprint(footprint, output);

        Assert.Equal(File.ReadAllBytes(TestData.Footprint), File.ReadAllBytes(output));
    }

    // -------------------------------------------------------------------------- the view semantics

    [Fact]
    public void AViewIsAHandleOnTheNode_NotACopyOfIt()
    {
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);

        var first = library.GetSymbol("Conn_01x02")!;
        var second = library.GetSymbol("Conn_01x02")!;

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.Same(first.Node, second.Node);

        first.Id = "Renamed";
        Assert.Equal("Renamed", second.Id);
    }

    [Fact]
    public void CloneAs_DoesNotTouchTheOriginal()
    {
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var original = library.GetSymbol("Conn_01x02")!;

        var copy = original.CloneAs("Conn_01x02_Copy");

        Assert.Equal("Conn_01x02", original.Id);
        Assert.Equal("Conn_01x02", original.GetPropertyValue("Value"));
        Assert.Equal("Conn_01x02_Copy", copy.Id);
        Assert.Equal("Conn_01x02_Copy", copy.GetPropertyValue("Value"));
        Assert.Equal("Conn_01x02_Copy_1_1", copy.Units[0].Id);
        Assert.Equal(2, copy.Pins.Count);
    }

    [Fact]
    public void RemoveSymbol_TakesItOutOfTheFile()
    {
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);

        Assert.True(library.RemoveSymbol("Conn_01x02"));
        Assert.False(library.RemoveSymbol("Conn_01x02"));
        Assert.Equal(34, library.Symbols.Count);
        Assert.Null(library.GetSymbol("Conn_01x02"));

        var output = Path.Combine(TestData.NewScratchDirectory(), "orbion.kicad_sym");
        library.Save(output);
        Assert.Equal(34, KiCadSymbolLibrary.Load(output).Symbols.Count);
    }

    [Fact]
    public void ABuiltInMemoryLibrary_StillSaves()
    {
        var library = new KiCadSymbolLibrary("kicad-sharp-tests", "20241209");
        var symbol = library.AddSymbol("TEST_R");
        var unit = symbol.AddUnit("TEST_R_1_1");
        unit.AddPin(new KiCadPin("passive", "line", new KiCadPosition(0, 2.54, 270), 1.27, "~", "1"));
        unit.AddPin(new KiCadPin("passive", "line", new KiCadPosition(0, -2.54, 90), 1.27, "~", "2"));

        var output = Path.Combine(TestData.NewScratchDirectory(), "built.kicad_sym");
        library.Save(output);

        var reloaded = KiCadSymbolLibrary.Load(output);
        Assert.Equal("kicad-sharp-tests", reloaded.Generator);
        Assert.Single(reloaded.Symbols);
        Assert.Equal(2, reloaded.Symbols[0].Pins.Count);
        Assert.Equal("TEST_R", reloaded.Symbols[0].GetPropertyValue("Value"));
        Assert.Equal(new KiCadPosition(0, 2.54, 270), reloaded.Symbols[0].Pins[0].Position);
    }
}
