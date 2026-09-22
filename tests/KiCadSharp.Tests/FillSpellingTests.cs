using System.Diagnostics;

using KiCadSharp.Documents;
using KiCadSharp.Schematics;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// A fill is spelled the way the parser of its owner's file reads it (#64). KiCad 10.0.6's board
/// parser, which also reads footprints, takes a bare <c>(fill no)</c> and rejects
/// <c>(fill (type no))</c> with "Expecting yes, no, solid, none, hatch, reverse_hatch or
/// cross_hatch. Got 'type'". Its schematic parser, which also reads symbol libraries, takes only
/// <c>(fill (type none))</c>. A fill that <c>RequireFill()</c> creates has no spelling of its own, so
/// it takes its owner's. A fill loaded from a file keeps the spelling it has.
/// </summary>
/// <remarks>
/// <see cref="KiCadReadsEveryNewFill"/> hands the result to KiCad itself. It is opt-in, as every test
/// that needs KiCad is: set <c>KICADSHARP_KICAD_CLI</c> to a <c>kicad-cli</c>. Without it the test
/// returns early, because CI has no KiCad.
/// </remarks>
public class FillSpellingTests
{
    /// <summary>The shapes KiCad's board parser reads, on a board and in a footprint.</summary>
    public static TheoryData<string> BoardShapes => ["gr_rect", "gr_circle", "gr_poly", "fp_rect", "fp_circle", "fp_poly"];

    /// <summary>The shapes KiCad's schematic parser reads, in a symbol and on a schematic sheet.</summary>
    public static TheoryData<string> SchematicShapes =>
    [
        "symbol rectangle", "symbol circle", "symbol arc", "symbol polyline",
        "schematic rectangle", "schematic text_box", "schematic bezier", "schematic sheet",
    ];

    // ------------------------------------------------------------------------- a new fill, per owner

    [Theory]
    [MemberData(nameof(BoardShapes))]
    public void ANewFillOnABoardOrFootprintShape_IsABareValue(string owner)
    {
        var shape = Shape(owner);

        shape.RequireFill().Type = "no";

        var fill = SavedFill(shape);
        Assert.Equal(["no"], fill.Values.ToArray());
        Assert.Empty(fill.Children);
        Assert.Equal("no", shape.Fill!.Type);
        Assert.False(shape.Fill!.IsFilled);

        shape.RequireFill().Type = "yes";

        Assert.Equal(["yes"], SavedFill(shape).Values.ToArray());
        Assert.True(shape.Fill!.IsFilled);
    }

    [Theory]
    [MemberData(nameof(SchematicShapes))]
    public void ANewFillInASymbolOrASchematic_HasATypeChild(string owner)
    {
        var shape = Shape(owner);

        shape.RequireFill().Type = "none";

        var fill = SavedFill(shape);
        Assert.Empty(fill.Values);
        Assert.Equal("type", Assert.Single(fill.Children).Token);
        Assert.Equal("none", fill.GetChildValue("type"));
        Assert.False(shape.Fill!.IsFilled);

        shape.RequireFill().Type = "outline";

        Assert.Equal("outline", SavedFill(shape).GetChildValue("type"));
        Assert.True(shape.Fill!.IsFilled);
    }

    [Theory]
    [MemberData(nameof(BoardShapes))]
    [MemberData(nameof(SchematicShapes))]
    public void RequireFillAlone_AddsAnEmptyFill_ThatBothParsersRead(string owner)
    {
        var shape = Shape(owner);

        _ = shape.RequireFill();

        var fill = SavedFill(shape);
        Assert.Empty(fill.Values);
        Assert.Empty(fill.Children);
        Assert.Equal("none", shape.Fill!.Type);
    }

    [Theory]
    [MemberData(nameof(BoardShapes))]
    [MemberData(nameof(SchematicShapes))]
    public void ReadingFill_OnAShapeWithout_AddsNothing(string owner)
    {
        var shape = Shape(owner);
        var before = shape.Node.ToText();

        Assert.Null(shape.Fill);
        Assert.Equal(before, shape.Node.ToText());
    }

    // ------------------------------------------------------------------ the words, per spelling

    [Theory]
    [InlineData("yes", "yes")]
    [InlineData("no", "no")]
    [InlineData("solid", "solid")]
    [InlineData("none", "none")]
    [InlineData("hatch", "hatch")]
    [InlineData("reverse_hatch", "reverse_hatch")]
    [InlineData("cross_hatch", "cross_hatch")]
    [InlineData("outline", "yes")]
    public void OnABoard_EveryWordKiCadReads_IsWritten_AndTheSchematicsOutlineIsYes(string word, string written)
    {
        var rectangle = new KiCadGrRect();

        rectangle.RequireFill().Type = word;

        Assert.Equal([written], SavedFill(rectangle.Node).Values.ToArray());
        Assert.Equal(written, rectangle.Fill!.Type);
    }

    [Theory]
    [InlineData("none", "none")]
    [InlineData("outline", "outline")]
    [InlineData("background", "background")]
    [InlineData("color", "color")]
    [InlineData("hatch", "hatch")]
    [InlineData("reverse_hatch", "reverse_hatch")]
    [InlineData("cross_hatch", "cross_hatch")]
    [InlineData("no", "none")]
    [InlineData("yes", "outline")]
    [InlineData("solid", "outline")]
    public void InASymbol_EveryWordKiCadReads_IsWritten_AndTheBoardsWordsAreMapped(string word, string written)
    {
        var rectangle = new KiCadRectangle(0, 0, 1, 1);

        rectangle.RequireFill().Type = word;

        Assert.Equal(written, SavedFill(rectangle.Node).GetChildValue("type"));
        Assert.Equal(written, rectangle.Fill!.Type);
    }

    [Theory]
    [InlineData("background")]
    [InlineData("color")]
    [InlineData("type")]
    [InlineData("No")]
    [InlineData("")]
    public void OnABoard_AWordKiCadRejects_Throws_AndWritesNothing(string word)
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        var before = board.ToText();
        var loaded = board.GraphicRectangles[0].Fill!;
        var added = new KiCadGrCircle().RequireFill();

        Assert.Throws<ArgumentException>(() => loaded.Type = word);
        Assert.Throws<ArgumentException>(() => added.Type = word);

        Assert.Equal(before, board.ToText());
        Assert.Equal("(fill)", added.Node.ToText().Trim());
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("None")]
    [InlineData("")]
    public void InASymbol_AWordKiCadRejects_Throws_AndWritesNothing(string word)
    {
        var added = new KiCadRectangle(0, 0, 1, 1).RequireFill();

        Assert.Throws<ArgumentException>(() => added.Type = word);

        Assert.Equal("(fill)", added.Node.ToText().Trim());
    }

    [Fact]
    public void ANullType_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new KiCadGrRect().RequireFill().Type = null!);
        Assert.Throws<ArgumentNullException>(() => new KiCadRectangle(0, 0, 1, 1).RequireFill().Type = null!);
    }

    // ------------------------------------------------------- a loaded fill keeps the form it has

    [Fact]
    public void ALoadedBoardFill_ChangesOnlyItsValue()
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        var before = board.ToText();
        Assert.Equal(1, Occurrences(before, "(fill no)"));

        board.GraphicRectangles[0].Fill!.Type = "yes";

        Assert.Equal(before.Replace("(fill no)", "(fill yes)", StringComparison.Ordinal), board.ToText());
    }

    [Fact]
    public void ALoadedFootprintFill_ChangesOnlyItsValue()
    {
        var library = KiCadFootprintLibrary.Load(Path.Combine(TestData.Root, "LED_0603_1608Metric.kicad_mod"));
        var before = library.ToText();
        Assert.Equal(1, Occurrences(before, "(fill no)"));

        library.Footprints[0].Rectangles[0].RequireFill().Type = "solid";

        Assert.Equal(before.Replace("(fill no)", "(fill solid)", StringComparison.Ordinal), library.ToText());
    }

    [Fact]
    public void ALoadedSymbolFill_ChangesOnlyItsTypeChild()
    {
        const string Text = "(kicad_symbol_lib\n\t(version 20251024)\n\t(symbol \"A\"\n\t\t(symbol \"A_0_1\"\n\t\t\t(rectangle\n\t\t\t\t(start 0 0)\n\t\t\t\t(end 1 1)\n\t\t\t\t(fill\n\t\t\t\t\t(type none)\n\t\t\t\t)\n\t\t\t)\n\t\t)\n\t)\n)\n";
        var library = KiCadSymbolLibrary.Parse(Text);

        library.Symbols[0].Units[0].GraphicalItems[0].Fill!.Type = "background";

        Assert.Equal(Text.Replace("(type none)", "(type background)", StringComparison.Ordinal), library.ToText());
    }

    [Fact]
    public void ALoadedEmptyFill_OnABoardShape_TakesTheBoardSpelling()
    {
        var board = KiCadBoard.Parse("(kicad_pcb (version 20260206) (gr_rect (start 0 0) (end 1 1) (layer \"Edge.Cuts\") (fill)))");

        board.GraphicRectangles[0].Fill!.Type = "no";

        Assert.Equal(["no"], SavedFill(board.GraphicRectangles[0].Node).Values.ToArray());
    }

    /// <summary>
    /// A fill that is already in the other parser's spelling is not this library's to rewrite, so it
    /// stays in that spelling, and the word is mapped into it.
    /// </summary>
    [Fact]
    public void AFillAlreadyInTheOtherSpelling_KeepsIt()
    {
        var board = KiCadBoard.Parse("(kicad_pcb (version 20260206) (gr_rect (start 0 0) (end 1 1) (layer \"Edge.Cuts\") (fill (type none))))");
        var symbol = KiCadSymbolLibrary.Parse("(kicad_symbol_lib (version 20251024) (symbol \"A\" (rectangle (start 0 0) (end 1 1) (fill no))))");

        board.GraphicRectangles[0].Fill!.Type = "yes";
        symbol.Symbols[0].GraphicalItems[0].Fill!.Type = "outline";

        Assert.Equal("outline", SavedFill(board.GraphicRectangles[0].Node).GetChildValue("type"));
        Assert.Equal(["yes"], SavedFill(symbol.Symbols[0].GraphicalItems[0].Node).Values.ToArray());
    }

    /// <summary>
    /// Every fill in two files KiCad 10 wrote, written back with its own value: the board's
    /// <c>gr_rect</c> and <c>gr_poly</c> and its footprints' <c>fp_rect</c> and <c>fp_poly</c> in
    /// SNEdge, and every drawing in a symbol library. Each file still saves byte for byte.
    /// </summary>
    [Fact]
    public void EveryLoadedFill_WrittenBackWithItsOwnValue_SavesByteForByte()
    {
        var board = KiCadBoard.Load(TestData.SNEdgeBoard);
        var boardBefore = board.ToText();
        var fills = board.GraphicRectangles.Select(s => s.Fill)
            .Concat(board.GraphicPolygons.Select(s => s.Fill))
            .Concat(board.Footprints.SelectMany(f => f.Rectangles.Select(s => s.Fill)
                .Concat(f.Circles.Select(s => s.Fill))
                .Concat(f.Polygons.Select(s => s.Fill))))
            .OfType<KiCadFill>()
            .ToList();
        Assert.Equal(64, fills.Count);
        foreach (var fill in fills)
        {
            fill.Type = fill.Type;
        }

        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var libraryBefore = library.ToText();
        var symbolFills = library.Symbols.SelectMany(s => s.GraphicalItems).Select(i => i.Fill).OfType<KiCadFill>().ToList();
        Assert.Equal(182, symbolFills.Count);
        foreach (var fill in symbolFills)
        {
            fill.Type = fill.Type;
        }

        Assert.Equal(boardBefore, board.ToText());
        Assert.Equal(libraryBefore, library.ToText());
    }

    [Fact]
    public void ThePublicConstructors_TakeTheOwnersSpelling()
    {
        var schematicByDefault = new KiCadFill(new SExpression("fill"));
        var board = new KiCadFill(new SExpression("fill"), KiCadFillSpelling.Board);
        var schematic = new KiCadFill(new SExpression("fill"), KiCadFillSpelling.Schematic);

        schematicByDefault.Type = "none";
        board.Type = "none";
        schematic.Type = "none";

        Assert.Equal("none", schematicByDefault.Node.GetChildValue("type"));
        Assert.Equal(["none"], board.Node.Values.ToArray());
        Assert.Equal("none", schematic.Node.GetChildValue("type"));
    }

    // --------------------------------------------------------------------------- KiCad reads them

    /// <summary>
    /// A board, a footprint, a symbol library and a schematic, each with fills given in memory in
    /// every word the API takes, handed to <c>kicad-cli</c> 10: KiCad must load each file, and its own
    /// re-save must hold the fill that was meant.
    /// </summary>
    [Fact]
    public void KiCadReadsEveryNewFill()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        var version = KiCadBoard.Load(TestData.Kicad10Board).Version;
        var scratch = TestData.NewScratchDirectory();

        // ── a board: one gr_rect per word, and a gr_circle and gr_poly ─────────────────────────
        // KiCad saves each fill as yes, no or a hatch (pcb_io_kicad_sexpr.cpp, 1071–1098).
        (string Word, string Saved)[] boardWords =
        [
            ("no", "no"), ("none", "no"), ("yes", "yes"), ("solid", "yes"), ("outline", "yes"),
            ("hatch", "hatch"), ("reverse_hatch", "reverse_hatch"), ("cross_hatch", "cross_hatch"),
        ];
        var board = new KiCadBoard(version: version);
        for (var i = 0; i < boardWords.Length; i++)
        {
            var rectangle = board.GraphicRectangles.Add();
            rectangle.Start = new KiCadPosition(i * 10, 0);
            rectangle.End = new KiCadPosition((i * 10) + 5, 5);
            rectangle.Layer = KiCadLayerNames.FSilkS;
            rectangle.RequireStroke().Width = 0.12;
            rectangle.RequireFill().Type = boardWords[i].Word;
        }

        var circle = board.GraphicCircles.Add();
        circle.Center = new KiCadPosition(0, 20);
        circle.End = new KiCadPosition(2, 20);
        circle.Layer = KiCadLayerNames.FSilkS;
        circle.RequireStroke().Width = 0.12;
        circle.RequireFill().Type = "yes";
        var polygon = board.GraphicPolygons.Add();
        polygon.AddPoint(10, 20);
        polygon.AddPoint(15, 20);
        polygon.AddPoint(15, 25);
        polygon.Layer = KiCadLayerNames.FSilkS;
        polygon.RequireStroke().Width = 0.12;
        polygon.RequireFill().Type = "no";

        var boardPath = Path.Combine(scratch, "fills.kicad_pcb");
        board.Save(boardPath);
        Run(cli, scratch, "pcb", "upgrade", "--force", boardPath);

        var upgraded = KiCadBoard.Load(boardPath);
        Assert.Equal("pcbnew", upgraded.Generator); // KiCad loaded it and wrote it back itself
        var saved = upgraded.GraphicRectangles.ToDictionary(r => r.Start.X, r => r.Fill!.Type);
        for (var i = 0; i < boardWords.Length; i++)
        {
            Assert.Equal(boardWords[i].Saved, saved[i * 10]);
        }

        Assert.Equal("yes", Assert.Single(upgraded.GraphicCircles).Fill!.Type);
        Assert.Equal("no", Assert.Single(upgraded.GraphicPolygons).Fill!.Type);

        // ── a footprint: fp_rect, fp_circle, fp_poly ────────────────────────────────────────────
        var footprint = new KiCadFootprint("Filled");
        var fpRect = footprint.Rectangles.Add();
        fpRect.Start = new KiCadPosition(-1, -1);
        fpRect.End = new KiCadPosition(1, 1);
        fpRect.Layer = KiCadLayerNames.FCrtYd;
        fpRect.Width = 0.05;
        fpRect.RequireFill().Type = "no";
        var fpCircle = footprint.Circles.Add();
        fpCircle.Center = new KiCadPosition(0, 0);
        fpCircle.End = new KiCadPosition(0.5, 0);
        fpCircle.Layer = KiCadLayerNames.FFab;
        fpCircle.Width = 0.1;
        fpCircle.RequireFill().Type = "yes";
        var fpPoly = footprint.Polygons.Add();
        fpPoly.AddPoint(0, 0);
        fpPoly.AddPoint(0.5, 0);
        fpPoly.AddPoint(0.5, 0.5);
        fpPoly.Layer = KiCadLayerNames.FSilkS;
        fpPoly.Width = 0.12;
        fpPoly.RequireFill().Type = "solid";
        new KiCadFootprintLibrary(footprint.Node).Version = version; // a footprint built in memory has none (#63)

        var pretty = Directory.CreateDirectory(Path.Combine(scratch, "Filled.pretty")).FullName;
        KiCadFootprintLibrary.SaveFootprint(footprint, Path.Combine(pretty, "Filled.kicad_mod"));
        var prettyOut = Path.Combine(scratch, "FilledOut.pretty");
        Run(cli, scratch, "fp", "upgrade", "--force", "-o", prettyOut, pretty);

        var reread = Assert.Single(KiCadFootprintLibrary.Load(Path.Combine(prettyOut, "Filled.kicad_mod")).Footprints);
        Assert.Equal("no", Assert.Single(reread.Rectangles).Fill!.Type);
        Assert.Equal("yes", Assert.Single(reread.Circles).Fill!.Type);
        Assert.Equal("yes", Assert.Single(reread.Polygons).Fill!.Type);

        // ── a symbol library: one rectangle per word ───────────────────────────────────────────
        // KiCad saves each fill's type unchanged (sch_io_kicad_sexpr_common.cpp, formatFill, 33).
        (string Word, string Saved)[] symbolWords =
        [
            ("none", "none"), ("no", "none"), ("outline", "outline"), ("yes", "outline"), ("solid", "outline"),
            ("background", "background"), ("hatch", "hatch"),
        ];
        var library = new KiCadSymbolLibrary(version: "20251024");
        var symbol = library.AddSymbol("Filled");
        symbol.AddProperty("Reference", "U");
        var unit = symbol.AddUnit("Filled_0_1");
        for (var i = 0; i < symbolWords.Length; i++)
        {
            var rectangle = new KiCadRectangle(i * 10, 0, (i * 10) + 5, 5);
            rectangle.RequireFill().Type = symbolWords[i].Word;
            unit.Node.AddChild(rectangle.Node);
        }

        var symbolPath = Path.Combine(scratch, "fills.kicad_sym");
        library.Save(symbolPath);
        var symbolOut = Path.Combine(scratch, "fills.upgraded.kicad_sym");
        Run(cli, scratch, "sym", "upgrade", "--force", "-o", symbolOut, symbolPath);

        var rectangles = KiCadSymbolLibrary.Load(symbolOut).Symbols[0].GraphicalItems.OfType<KiCadRectangle>().ToDictionary(r => r.Start.X, r => r.Fill!.Type);
        for (var i = 0; i < symbolWords.Length; i++)
        {
            Assert.Equal(symbolWords[i].Saved, rectangles[i * 10]);
        }

        // ── a schematic: a rectangle ──────────────────────────────────────────────────────────
        var schematic = KiCadSchematic.Parse($"(kicad_sch (version {KiCadSchematic.Load(TestData.Rs485Bridge).Version}) (generator \"KiCadSharp\") (uuid \"11111111-2222-3333-4444-555555555555\") (paper \"A4\"))");
        var box = schematic.Rectangles.Add();
        box.Start = new KiCadPosition(10, 10);
        box.End = new KiCadPosition(20, 20);
        box.RequireFill().Type = "no";

        var schematicPath = Path.Combine(scratch, "fills.kicad_sch");
        schematic.Save(schematicPath);
        Run(cli, scratch, "sch", "upgrade", "--force", schematicPath);

        Assert.Equal("none", Assert.Single(KiCadSchematic.Load(schematicPath).Rectangles).Fill!.Type);
    }

    // ------------------------------------------------------------------------------------- helpers

    /// <summary>A shape and its two fill accessors, whichever class declares them.</summary>
    private sealed record Owned(SExpression Node, Func<KiCadFill?> ReadFill, Func<KiCadFill> Require)
    {
        public KiCadFill? Fill => ReadFill();

        public KiCadFill RequireFill() => Require();
    }

    private static Owned Shape(string owner) => owner switch
    {
        "gr_rect" => Of(new KiCadGrRect(), s => s.Fill, s => s.RequireFill()),
        "gr_circle" => Of(new KiCadGrCircle(), s => s.Fill, s => s.RequireFill()),
        "gr_poly" => Of(new KiCadGrPoly(), s => s.Fill, s => s.RequireFill()),
        "fp_rect" => Of(new KiCadFpRect(), s => s.Fill, s => s.RequireFill()),
        "fp_circle" => Of(new KiCadFpCircle(), s => s.Fill, s => s.RequireFill()),
        "fp_poly" => Of(new KiCadFpPoly(), s => s.Fill, s => s.RequireFill()),
        "symbol rectangle" => Of(new KiCadRectangle(0, 0, 1, 1), s => s.Fill, s => s.RequireFill()),
        "symbol circle" => Of(new KiCadCircle(0, 0, 1), s => s.Fill, s => s.RequireFill()),
        "symbol arc" => Of(new KiCadArc(0, 0, 0.29, 0.71, 1, 1), s => s.Fill, s => s.RequireFill()),
        "symbol polyline" => Of(new KiCadPolyline(), s => s.Fill, s => s.RequireFill()),
        "schematic rectangle" => Of(EmptySchematic().Rectangles.Add(), s => s.Fill, s => s.RequireFill()),
        "schematic text_box" => Of(EmptySchematic().TextBoxes.Add(), s => s.Fill, s => s.RequireFill()),
        "schematic bezier" => Of(EmptySchematic().Beziers.Add(), s => s.Fill, s => s.RequireFill()),
        "schematic sheet" => Of(EmptySchematic().Sheets.Add(), s => s.Fill, s => s.RequireFill()),
        _ => throw new ArgumentOutOfRangeException(nameof(owner), owner, null),
    };

    private static Owned Of<T>(T shape, Func<T, KiCadFill?> fill, Func<T, KiCadFill> require)
        where T : KiCadNode => new(shape.Node, () => fill(shape), () => require(shape));

    private static KiCadSchematic EmptySchematic() =>
        KiCadSchematic.Parse("(kicad_sch (version 20250114) (uuid \"11111111-2222-3333-4444-555555555555\"))");

    /// <summary>The shape's <c>(fill …)</c> as its saved text reads back, not the node in memory.</summary>
    private static SExpression SavedFill(Owned shape) => SavedFill(shape.Node);

    private static SExpression SavedFill(SExpression shape) =>
        SExpression.Parse(shape.ToText()).GetChild("fill") ?? throw new InvalidOperationException($"no fill in {shape.ToText()}");

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static void Run(string cli, string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo(cli) { WorkingDirectory = workingDirectory, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"kicad-cli {string.Join(' ', arguments)} exited {process.ExitCode}:\n{stdout.Result}\n{stderr}");
    }
}
