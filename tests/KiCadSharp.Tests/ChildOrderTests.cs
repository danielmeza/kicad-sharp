using System.Diagnostics;

using KiCadSharp.Documents;
using KiCadSharp.Schematics;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// The forms whose children KiCad reads by position (#59). A child this library creates goes where
/// KiCad's parser looks for it, whatever order the caller sets properties in; a form that is already
/// in a file keeps the order it has.
/// </summary>
/// <remarks>
/// The rules are KiCad 10.0.6's, and <c>KiCadChildOrder</c> cites the parser line for each.
/// <see cref="KiCadLoadsEveryFormBuiltInTheWrongOrder"/> hands the result to KiCad itself. It is
/// opt-in, as every test that needs KiCad is: set <c>KICADSHARP_KICAD_CLI</c> to a
/// <c>kicad-cli</c>. Without it the test returns early — CI has no KiCad.
/// </remarks>
public class ChildOrderTests
{
    private const string Pts = "pts";
    private const string Start = "start";
    private const string Mid = "mid";
    private const string End = "end";
    private const string Center = "center";
    private const string Angle = "angle";

    // ------------------------------------------------ every order of the calls, one form at a time

    [Fact]
    public void FpPoly_PointsGoFirst_WhateverOrderThePropertiesAreSetIn() =>
        AssertEveryOrder(
            () => new KiCadFpPoly(),
            [Pts],
            ("layer", p => p.Layer = KiCadLayerNames.FSilkS),
            ("stroke", p => p.Width = 0.12),
            (Pts, p => AddPoints(p.AddPoint, 3)));

    [Fact]
    public void GrPoly_PointsGoFirst_WhateverOrderThePropertiesAreSetIn() =>
        AssertEveryOrder(
            () => new KiCadGrPoly(),
            [Pts],
            ("layer", p => p.Layer = KiCadLayerNames.FSilkS),
            ("stroke", p => p.RequireStroke().Width = 0.12),
            ("uuid", p => p.Uuid = "00000000-0000-4000-8000-000000000001"),
            (Pts, p => AddPoints(p.AddPoint, 3)));

    [Fact]
    public void GrCurve_PointsGoFirst_WhateverOrderThePropertiesAreSetIn() =>
        AssertEveryOrder(
            () => new KiCadGrCurve(),
            [Pts],
            ("layer", c => c.Layer = KiCadLayerNames.FSilkS),
            ("stroke", c => c.RequireStroke().Width = 0.12),
            (Pts, c => AddPoints(c.AddPoint, 4)));

    [Fact]
    public void FpLine_StartThenEndGoFirst_WhateverOrderThePropertiesAreSetIn() =>
        AssertEveryOrder(
            () => new KiCadFpLine(),
            [Start, End],
            (Start, l => l.Start = new KiCadPosition(0, 0)),
            (End, l => l.End = new KiCadPosition(1, 1)),
            ("layer", l => l.Layer = KiCadLayerNames.FSilkS),
            ("stroke", l => l.Width = 0.12));

    [Fact]
    public void GrLine_StartThenEndGoFirst_WhateverOrderThePropertiesAreSetIn() =>
        AssertEveryOrder(
            () => new KiCadGrLine(),
            [Start, End],
            (Start, l => l.Start = new KiCadPosition(0, 0)),
            (End, l => l.End = new KiCadPosition(10, 0)),
            ("layer", l => l.Layer = KiCadLayerNames.EdgeCuts),
            ("stroke", l => l.RequireStroke().Width = 0.1),
            ("uuid", l => l.Uuid = "00000000-0000-4000-8000-000000000002"));

    [Fact]
    public void FpRect_StartThenEndGoFirst_WhateverOrderThePropertiesAreSetIn() =>
        AssertEveryOrder(
            () => new KiCadFpRect(),
            [Start, End],
            (Start, r => r.Start = new KiCadPosition(0, 0)),
            (End, r => r.End = new KiCadPosition(1, 1)),
            ("layer", r => r.Layer = KiCadLayerNames.FSilkS),
            ("stroke", r => r.Width = 0.12));

    [Fact]
    public void GrRect_StartThenEndGoFirst_WhateverOrderThePropertiesAreSetIn() =>
        AssertEveryOrder(
            () => new KiCadGrRect(),
            [Start, End],
            (Start, r => r.Start = new KiCadPosition(0, 0)),
            (End, r => r.End = new KiCadPosition(10, 10)),
            ("layer", r => r.Layer = KiCadLayerNames.EdgeCuts),
            ("stroke", r => r.RequireStroke().Width = 0.1));

    [Fact]
    public void FpCircle_CenterThenEndGoFirst_WhateverOrderThePropertiesAreSetIn() =>
        AssertEveryOrder(
            () => new KiCadFpCircle(),
            [Center, End],
            (Center, c => c.Center = new KiCadPosition(0, 0)),
            (End, c => c.End = new KiCadPosition(1, 0)),
            ("layer", c => c.Layer = KiCadLayerNames.FSilkS),
            ("stroke", c => c.Width = 0.12));

    [Fact]
    public void GrCircle_CenterThenEndGoFirst_WhateverOrderThePropertiesAreSetIn() =>
        AssertEveryOrder(
            () => new KiCadGrCircle(),
            [Center, End],
            (Center, c => c.Center = new KiCadPosition(0, 0)),
            (End, c => c.End = new KiCadPosition(5, 0)),
            ("layer", c => c.Layer = KiCadLayerNames.FSilkS),
            ("stroke", c => c.RequireStroke().Width = 0.12));

    [Fact]
    public void FpArc_StartMidEndGoFirst_WhateverOrderThePropertiesAreSetIn() =>
        AssertEveryOrder(
            () => new KiCadFpArc(),
            [Start, Mid, End],
            (Start, a => a.Start = new KiCadPosition(0, 0)),
            (Mid, a => a.Mid = new KiCadPosition(0.2929, 0.7071)),
            (End, a => a.End = new KiCadPosition(1, 1)),
            ("layer", a => a.Layer = KiCadLayerNames.FSilkS),
            ("stroke", a => a.Width = 0.12));

    [Fact]
    public void GrArc_StartMidEndGoFirst_WhateverOrderThePropertiesAreSetIn() =>
        AssertEveryOrder(
            () => new KiCadGrArc(),
            [Start, Mid, End],
            (Start, a => a.Start = new KiCadPosition(0, 0)),
            (Mid, a => a.Mid = new KiCadPosition(2.929, 7.071)),
            (End, a => a.End = new KiCadPosition(10, 10)),
            ("layer", a => a.Layer = KiCadLayerNames.FSilkS),
            ("stroke", a => a.RequireStroke().Width = 0.12),
            ("uuid", a => a.Uuid = "00000000-0000-4000-8000-000000000003"));

    /// <summary>
    /// A KiCad 5 arc, <c>(start centre) (end first-point) (angle sweep)</c>: those three are read by
    /// position too, in that order, in a file stamped 20210925 or older.
    /// </summary>
    [Fact]
    public void LegacyArc_StartEndAngleGoFirst_WhateverOrderThePropertiesAreSetIn()
    {
        AssertEveryOrder(
            () => new KiCadFpArc(),
            [Start, End, Angle],
            (Start, a => a.Start = new KiCadPosition(0, 0)),
            (End, a => a.End = new KiCadPosition(1, 0)),
            (Angle, a => a.Angle = 90),
            ("layer", a => a.Layer = KiCadLayerNames.FSilkS),
            ("stroke", a => a.Width = 0.12));

        AssertEveryOrder(
            () => new KiCadGrArc(),
            [Start, End, Angle],
            (Start, a => a.Start = new KiCadPosition(0, 0)),
            (End, a => a.End = new KiCadPosition(1, 0)),
            (Angle, a => a.Angle = 90),
            ("layer", a => a.Layer = KiCadLayerNames.FSilkS));
    }

    [Fact]
    public void Dimension_TypeGoesFirst_WhateverOrderThePropertiesAreSetIn() =>
        AssertEveryOrder(
            () => new KiCadDimension(),
            ["type"],
            ("type", d => d.Type = "aligned"),
            ("layer", d => d.Layer = KiCadLayerNames.DwgsUser),
            ("uuid", d => d.Uuid = "00000000-0000-4000-8000-000000000004"),
            (Pts, d => AddPoints(d.AddPoint, 2)),
            ("height", d => d.Height = 2));

    // --------------------------------------------------------------------------- the zone's polygons

    [Fact]
    public void Zone_Outline_IsAPolygonHoldingOnlyItsPoints()
    {
        var zone = new KiCadZone { NetName = string.Empty, Layers = [KiCadLayerNames.FCu] };
        zone.Priority = 1;
        AddPoints(zone.AddPoint, 4);
        zone.Name = "pour";

        var polygon = Saved(zone.Node).GetChild("polygon")!;
        Assert.Equal([Pts], polygon.Children.Select(c => c.Token));
        Assert.Equal(4, zone.Points.Count);
    }

    /// <summary>
    /// A filled polygon reads <c>(layer …)</c>, then <c>(island)</c>, then <c>(pts …)</c>, and
    /// closes. A layer added to the legacy single-layer spelling, which has no <c>layer</c>, goes in
    /// front of the points rather than after them, where KiCad would expect the form to end.
    /// </summary>
    [Fact]
    public void FilledPolygon_ALayerAddedToTheLegacySpelling_GoesBeforeIslandAndPoints()
    {
        var zone = new KiCadZone(SExpression.Parse(
            "(zone (net 0) (net_name \"\") (layer \"F.Cu\")"
            + " (polygon (pts (xy 0 0) (xy 10 0) (xy 10 10)))"
            + " (filled_polygon (pts (xy 1 1) (xy 9 1) (xy 9 9)))"
            + " (filled_polygon (island) (pts (xy 2 2) (xy 3 2) (xy 3 3))))"));

        foreach (var filled in zone.FilledPolygons)
        {
            filled.Layer = KiCadLayerNames.FCu;
        }

        var saved = Saved(zone.Node).GetChildren("filled_polygon").ToArray();
        Assert.Equal(["layer", Pts], saved[0].Children.Select(c => c.Token));
        Assert.Equal(["layer", "island", Pts], saved[1].Children.Select(c => c.Token));
        Assert.Equal(KiCadLayerNames.FCu, zone.FilledPolygons[1].Layer);
    }

    // ------------------------------------------------------------------- a bare atom stays in front

    /// <summary>
    /// KiCad 6 and 7 wrote <c>locked</c> as a bare atom straight after the token, and KiCad reads it
    /// there, before the positional child. A child created in front goes after the atom.
    /// </summary>
    [Fact]
    public void AChildCreatedFirst_GoesAfterTheFormsBareAtoms()
    {
        var polygon = new KiCadGrPoly(SExpression.Parse("(gr_poly locked (layer \"Edge.Cuts\") (width 0.1))"));
        polygon.AddPoint(0, 0);

        Assert.Equal("(gr_poly locked (pts (xy 0 0)) (layer \"Edge.Cuts\") (width 0.1))", Flatten(polygon.Node.ToText()));
    }

    // ----------------------------------------------------------------------------- the file version

    /// <summary>
    /// KiCad reads a board's, a schematic's and a symbol library's <c>version</c> only as the first
    /// child, and a footprint's before anything its value changes the meaning of — an arc among
    /// them. A version the file did not have goes first.
    /// </summary>
    [Fact]
    public void AVersionAddedToAFileThatHadNone_GoesFirst()
    {
        var footprint = new KiCadFootprint("F") { Version = null }; // a new footprint has one (#63)
        footprint.Arcs.Add().Start = new KiCadPosition(0, 0);
        new KiCadFootprintLibrary(footprint.Node).Version = "20260206";
        Assert.Equal("version", footprint.Node.Children[0].Token);

        var board = KiCadBoard.Parse("(kicad_pcb (generator \"pcbnew\") (general (thickness 1.6)) (paper \"A4\"))");
        board.Version = "20260206";
        Assert.Equal(["version", "generator", "general", "paper"], board.Node.Children.Select(c => c.Token));

        var symbols = KiCadSymbolLibrary.Parse("(kicad_symbol_lib (generator \"kicad_symbol_editor\") (symbol \"R\"))");
        symbols.Version = "20251024";
        Assert.Equal(["version", "generator", "symbol"], symbols.Node.Children.Select(c => c.Token));

        var schematic = KiCadSchematic.Parse("(kicad_sch (generator \"eeschema\") (uuid \"00000000-0000-4000-8000-000000000005\") (paper \"A4\"))");
        schematic.Version = "20250114";
        Assert.Equal(["version", "generator", "uuid", "paper"], schematic.Node.Children.Select(c => c.Token));
    }

    // ------------------------------------------------------------ a file KiCad wrote keeps its order

    public static TheoryData<string> KiCadWrittenBoards => new() { TestData.Kicad10Board, TestData.SNEdgeBoard };

    /// <summary>
    /// Written by pcbnew 10.0.6, a board saves byte for byte, and still does after every positional
    /// property of every form this change touches is written back with the value it already has:
    /// each write lands in the child that is there, and nothing moves.
    /// </summary>
    [Theory]
    [MemberData(nameof(KiCadWrittenBoards))]
    public void AKiCadWrittenBoard_SavesByteForByte_AfterItsPositionalChildrenAreWrittenBack(string path)
    {
        var board = KiCadBoard.Load(path);
        var written = 0;

        foreach (var line in board.GraphicLines)
        {
            (line.Start, line.End) = (line.Start, line.End);
            written++;
        }

        foreach (var rectangle in board.GraphicRectangles)
        {
            (rectangle.Start, rectangle.End) = (rectangle.Start, rectangle.End);
            written++;
        }

        foreach (var circle in board.GraphicCircles)
        {
            (circle.Center, circle.End) = (circle.Center, circle.End);
            written++;
        }

        foreach (var arc in board.GraphicArcs)
        {
            (arc.Start, arc.Mid, arc.End) = (arc.Start, arc.Mid, arc.End);
            written++;
        }

        foreach (var polygon in board.GraphicPolygons)
        {
            polygon.Layer = polygon.Layer;
            written++;
        }

        foreach (var dimension in board.Dimensions)
        {
            dimension.Type = dimension.Type;
            written++;
        }

        foreach (var filled in board.Zones.SelectMany(z => z.FilledPolygons))
        {
            filled.Layer = filled.Layer;
            written++;
        }

        foreach (var footprint in board.Footprints)
        {
            foreach (var line in footprint.Lines)
            {
                (line.Start, line.End) = (line.Start, line.End);
                written++;
            }

            foreach (var rectangle in footprint.Rectangles)
            {
                (rectangle.Start, rectangle.End) = (rectangle.Start, rectangle.End);
                written++;
            }

            foreach (var circle in footprint.Circles)
            {
                (circle.Center, circle.End) = (circle.Center, circle.End);
                written++;
            }

            foreach (var arc in footprint.Arcs)
            {
                (arc.Start, arc.Mid, arc.End) = (arc.Start, arc.Mid, arc.End);
                written++;
            }

            foreach (var polygon in footprint.Polygons)
            {
                polygon.Layer = polygon.Layer;
                written++;
            }
        }

        Assert.True(written > 0, $"{path} has none of the forms this test is about");

        var output = Path.Combine(TestData.NewScratchDirectory(), Path.GetFileName(path));
        board.Save(output);

        Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(output));
    }

    /// <summary>
    /// A point added to a polygon KiCad wrote goes into the <c>pts</c> that is already first, and
    /// every polygon keeps exactly the children it had, in the order it had them.
    /// </summary>
    [Fact]
    public void APointAddedToAKiCadWrittenPolygon_LeavesItsChildrenWhereTheyWere()
    {
        var board = KiCadBoard.Load(TestData.SNEdgeBoard);
        var polygons = new List<(SExpression Node, Action<double, double> AddPoint, int Count)>();
        polygons.AddRange(board.GraphicPolygons.Select(p => (p.Node, (Action<double, double>)p.AddPoint, p.Points.Count)));
        polygons.AddRange(board.Footprints.SelectMany(f => f.Polygons).Select(p => (p.Node, (Action<double, double>)p.AddPoint, p.Points.Count)));
        var before = polygons.Select(p => p.Node.Children.Select(c => c.Token).ToArray()).ToArray();
        Assert.Equal(11, polygons.Count); // MEASURED: 2 gr_poly and 9 fp_poly in SNEdge

        foreach (var polygon in polygons)
        {
            polygon.AddPoint(1, 2);
        }

        var saved = Saved(board.Node);
        var after = saved.GetChildren("gr_poly")
            .Concat(saved.GetChildren("footprint").SelectMany(f => f.GetChildren("fp_poly")))
            .ToArray();
        Assert.Equal(before, after.Select(p => p.Children.Select(c => c.Token).ToArray()));
        Assert.Equal(polygons.Select(p => p.Count + 1), after.Select(p => p.GetChild(Pts)!.GetChildren("xy").Count()));
    }

    // --------------------------------------------------------------------------------- KiCad reads it

    /// <summary>
    /// Every affected form, built through the public API with its geometry set last, then handed to
    /// kicad-cli 10.0.6. Before #59 each of these made KiCad refuse the whole file.
    /// </summary>
    /// <remarks>
    /// The board carries the version pcbnew 10.0.6 stamps, taken from the board it wrote. The
    /// footprint carries the one it was built with, which is the same (#63); before that it had
    /// none, and KiCad read it by KiCad 5 rules, which reject a modern arc outright.
    /// </remarks>
    [Fact]
    public void KiCadLoadsEveryFormBuiltInTheWrongOrder()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        var version = KiCadBoard.Load(TestData.Kicad10Board).Version;
        var scratch = TestData.NewScratchDirectory();

        // ── a footprint ──────────────────────────────────────────────────────────────────────
        var footprint = new KiCadFootprint("WrongOrder");
        var poly = footprint.Polygons.Add();
        poly.Layer = KiCadLayerNames.FSilkS;
        poly.Width = 0.12;
        AddPoints(poly.AddPoint, 3);
        var line = footprint.Lines.Add();
        line.Layer = KiCadLayerNames.FSilkS;
        line.End = new KiCadPosition(1, 1);
        line.Start = new KiCadPosition(0, 0);
        var rectangle = footprint.Rectangles.Add();
        rectangle.Layer = KiCadLayerNames.FCrtYd;
        rectangle.End = new KiCadPosition(2, 2);
        rectangle.Start = new KiCadPosition(-2, -2);
        var circle = footprint.Circles.Add();
        circle.Layer = KiCadLayerNames.FFab;
        circle.End = new KiCadPosition(1, 0);
        circle.Center = new KiCadPosition(0, 0);
        var arc = footprint.Arcs.Add();
        arc.Layer = KiCadLayerNames.FSilkS;
        arc.End = new KiCadPosition(1, 1);
        arc.Mid = new KiCadPosition(0.292893, 0.707107);
        arc.Start = new KiCadPosition(0, 0);

        var pretty = Directory.CreateDirectory(Path.Combine(scratch, "WrongOrder.pretty")).FullName;
        KiCadFootprintLibrary.SaveFootprint(footprint, Path.Combine(pretty, "WrongOrder.kicad_mod"));
        var prettyOut = Path.Combine(scratch, "WrongOrderOut.pretty");
        Run(cli, scratch, "fp", "upgrade", "--force", "-o", prettyOut, pretty);

        var reread = Assert.Single(KiCadFootprintLibrary.Load(Path.Combine(prettyOut, "WrongOrder.kicad_mod")).Footprints);
        Assert.Equal(3, Assert.Single(reread.Polygons).Points.Count);
        Assert.Equal(new KiCadPosition(1, 1), Assert.Single(reread.Lines).End);
        Assert.Equal(new KiCadPosition(-2, -2), Assert.Single(reread.Rectangles).Start);
        Assert.Equal(new KiCadPosition(1, 0), Assert.Single(reread.Circles).End);
        Assert.Equal(new KiCadPosition(0.292893, 0.707107), Assert.Single(reread.Arcs).Mid);

        // ── a board ──────────────────────────────────────────────────────────────────────────
        var board = new KiCadBoard(version: version);
        var grPoly = board.GraphicPolygons.Add();
        grPoly.Layer = KiCadLayerNames.FSilkS;
        grPoly.RequireStroke().Width = 0.12;
        AddPoints(grPoly.AddPoint, 3);
        var curve = board.GraphicCurves.Add();
        curve.Layer = KiCadLayerNames.FSilkS;
        curve.RequireStroke().Width = 0.12;
        AddPoints(curve.AddPoint, 4);
        var grLine = board.GraphicLines.Add();
        grLine.Layer = KiCadLayerNames.EdgeCuts;
        grLine.RequireStroke().Width = 0.1;
        grLine.End = new KiCadPosition(10, 0);
        grLine.Start = new KiCadPosition(0, 0);
        var grRect = board.GraphicRectangles.Add();
        grRect.Layer = KiCadLayerNames.EdgeCuts;
        grRect.RequireStroke().Width = 0.1;
        grRect.End = new KiCadPosition(30, 20);
        grRect.Start = new KiCadPosition(-10, -10);
        var grCircle = board.GraphicCircles.Add();
        grCircle.Layer = KiCadLayerNames.FSilkS;
        grCircle.RequireStroke().Width = 0.12;
        grCircle.End = new KiCadPosition(5, 0);
        grCircle.Center = new KiCadPosition(0, 0);
        var grArc = board.GraphicArcs.Add();
        grArc.Layer = KiCadLayerNames.FSilkS;
        grArc.RequireStroke().Width = 0.12;
        grArc.End = new KiCadPosition(10, 10);
        grArc.Mid = new KiCadPosition(2.928932, 7.071068);
        grArc.Start = new KiCadPosition(0, 0);
        var dimension = board.Dimensions.Add();
        dimension.Layer = KiCadLayerNames.DwgsUser;
        dimension.AddPoint(0, 0);
        dimension.AddPoint(10, 0);
        dimension.Height = 2;
        dimension.Type = "aligned";

        var zone = board.Zones.Add();
        zone.NetName = string.Empty;
        zone.Layers = [KiCadLayerNames.FCu];
        AddPoints(zone.AddPoint, 4);
        var filled = zone.FilledPolygons.Add();
        var filledPoints = filled.Node.CreateChild(Pts); // no public writer for filled points
        filledPoints.CreateChild("xy", "1", "1");
        filledPoints.CreateChild("xy", "2", "1");
        filledPoints.CreateChild("xy", "2", "2");
        filled.Layer = KiCadLayerNames.FCu;

        var boardPath = Path.Combine(scratch, "wrong-order.kicad_pcb");
        board.Save(boardPath);
        Run(cli, scratch, "pcb", "upgrade", "--force", boardPath);

        var upgraded = KiCadBoard.Load(boardPath);
        Assert.Equal("pcbnew", upgraded.Generator); // KiCad loaded it and wrote it back itself
        Assert.Equal(3, Assert.Single(upgraded.GraphicPolygons).Points.Count);
        Assert.Equal(4, Assert.Single(upgraded.GraphicCurves).Points.Count);
        Assert.Equal(new KiCadPosition(10, 0), Assert.Single(upgraded.GraphicLines).End);
        Assert.Equal(new KiCadPosition(-10, -10), Assert.Single(upgraded.GraphicRectangles).Start);
        Assert.Equal(5, Assert.Single(upgraded.GraphicCircles).Radius, 6);
        Assert.Equal(new KiCadPosition(2.928932, 7.071068), Assert.Single(upgraded.GraphicArcs).Mid);
        Assert.Equal("aligned", Assert.Single(upgraded.Dimensions).Type);
        Assert.Equal(4, Assert.Single(upgraded.Zones).Points.Count);
    }

    // ------------------------------------------------------------------------------------- helpers

    /// <summary>
    /// Builds the form once for every order of <paramref name="steps"/>, saves it, reads the saved
    /// text back, and asserts its children: the <paramref name="positional"/> ones first, in KiCad's
    /// order, then the rest in the order they were set.
    /// </summary>
    private static void AssertEveryOrder<T>(Func<T> create, string[] positional, params (string Token, Action<T> Apply)[] steps)
        where T : KiCadNode
    {
        var orders = 0;
        foreach (var order in Permutations(steps))
        {
            var view = create();
            foreach (var step in order)
            {
                step.Apply(view);
            }

            var expected = positional.Where(t => steps.Any(s => s.Token == t))
                .Concat(order.Select(s => s.Token).Where(t => !positional.Contains(t)));
            var actual = Saved(view.Node).Children.Select(c => c.Token);
            Assert.True(
                expected.SequenceEqual(actual),
                $"calls {string.Join(", ", order.Select(s => s.Token))} saved {view.Node.ToText()}");
            orders++;
        }

        Assert.Equal(Enumerable.Range(1, steps.Length).Aggregate(1, (a, b) => a * b), orders);
    }

    private static IEnumerable<T[]> Permutations<T>(IReadOnlyList<T> items)
    {
        if (items.Count <= 1)
        {
            yield return items.ToArray();
            yield break;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, j) => j != i).ToArray();
            foreach (var tail in Permutations(rest))
            {
                yield return [items[i], .. tail];
            }
        }
    }

    /// <summary>The form as its saved text reads back, not the node in memory.</summary>
    private static SExpression Saved(SExpression node) => SExpression.Parse(node.ToText());

    private static void AddPoints(Action<double, double> add, int count)
    {
        for (var i = 0; i < count; i++)
        {
            add(i, i % 2);
        }
    }

    private static string Flatten(string text) =>
        string.Join(' ', text.Split(['\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries))
            .Replace("( ", "(", StringComparison.Ordinal)
            .Replace(" )", ")", StringComparison.Ordinal);

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
