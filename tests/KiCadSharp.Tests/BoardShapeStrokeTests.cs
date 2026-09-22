using System.Diagnostics;
using System.Text.RegularExpressions;

using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// A width set on a board drawing that has no stroke yet is written as KiCad 10.0.6 writes it,
/// <c>(stroke (width w) (type solid))</c>, not as the bare <c>(width w)</c> of older files (#96). A
/// drawing loaded from a file keeps whichever form it has. <see cref="KiCadFpItem.Width"/> does the
/// same for footprint shapes (#75).
/// </summary>
/// <remarks>
/// KiCad 10.0.6's board writer formats every drawing's stroke through <c>STROKE_PARAMS::Format</c>
/// (<c>pcbnew/pcb_io/kicad_sexpr/pcb_io_kicad_sexpr.cpp</c>, line 1069;
/// <c>common/stroke_params.cpp</c>, line 362). Its parser starts a drawing's stroke as solid
/// (<c>pcb_io_kicad_sexpr_parser.cpp</c>, line 3241) and reads a bare <c>width</c> into it (line
/// 3549). <see cref="KiCadReadsTheNewSpellingAsItReadTheOld"/> checks that the two are the same
/// drawing to KiCad. It is opt-in, as every test that needs KiCad is: set
/// <c>KICADSHARP_KICAD_CLI</c> to a <c>kicad-cli</c>.
/// </remarks>
public class BoardShapeStrokeTests
{
    public static TheoryData<string> Drawings => ["gr_line", "gr_rect", "gr_circle", "gr_arc", "gr_poly", "gr_curve"];

    [Theory]
    [MemberData(nameof(Drawings))]
    public void ANewDrawing_WritesItsWidthAsAStroke(string token)
    {
        var drawing = Create(token);

        drawing.Width = 0.15;

        Assert.Equal($"({token} (stroke (width 0.15) (type solid)))", Flatten(drawing.Node.ToText()));
        Assert.Null(drawing.Node.GetChild(KiCadTokens.Common.Width));
        Assert.Equal(0.15, drawing.Width);
        Assert.Equal(KiCadTokens.Common.Solid, drawing.Stroke!.Type);

        drawing.Width = 0.2;

        Assert.Equal($"({token} (stroke (width 0.2) (type solid)))", Flatten(drawing.Node.ToText()));
    }

    [Fact]
    public void ADrawingBuiltOnABoard_HasKiCadsForm()
    {
        var board = new KiCadBoard();
        var line = board.GraphicLines.Add();
        line.Start = new KiCadPosition(0, 0);
        line.End = new KiCadPosition(10, 0);
        line.Width = 0.1;
        line.Layer = KiCadLayerNames.EdgeCuts;

        Assert.Equal(
            "(gr_line (start 0 0) (end 10 0) (stroke (width 0.1) (type solid)) (layer \"Edge.Cuts\"))",
            Flatten(line.Node.ToText()));
    }

    /// <summary>A drawing loaded with a bare <c>(width w)</c> keeps it: only the number changes.</summary>
    [Fact]
    public void ALoadedBareWidth_StaysBare()
    {
        const string Text = "(kicad_pcb\n\t(version 20211014)\n\t(gr_line\n\t\t(start 0 0)\n\t\t(end 10 0)\n\t\t(layer \"Edge.Cuts\")\n\t\t(width 0.1)\n\t)\n)\n";
        var board = KiCadBoard.Parse(Text);

        board.GraphicLines[0].Width = 0.25;

        Assert.Equal(Text.Replace("(width 0.1)", "(width 0.25)", StringComparison.Ordinal), board.ToText());
        Assert.Null(board.GraphicLines[0].Stroke);
    }

    /// <summary>A drawing loaded with a stroke keeps its type: only the width changes.</summary>
    [Fact]
    public void ALoadedStroke_KeepsItsType()
    {
        var line = new KiCadGrLine(SExpression.Parse("(gr_line (start 0 0) (end 1 0) (stroke (width 0.1) (type dash)) (layer \"F.SilkS\"))"));

        line.Width = 0.3;

        Assert.Equal("(gr_line (start 0 0) (end 1 0) (stroke (width 0.3) (type dash)) (layer \"F.SilkS\"))", Flatten(line.Node.ToText()));
    }

    /// <summary>A stroke added through <see cref="KiCadGraphicItem.RequireStroke"/> takes the width, and no type is added to it.</summary>
    [Fact]
    public void AStrokeThatIsAlreadyThere_TakesTheWidth()
    {
        var rectangle = new KiCadGrRect();
        _ = rectangle.RequireStroke();

        rectangle.Width = 0.12;

        Assert.Equal("(gr_rect (stroke (width 0.12)))", Flatten(rectangle.Node.ToText()));
    }

    /// <summary>
    /// Every board drawing in four files KiCad 9 and 10 wrote, 53 of them, each with a stroke, written
    /// back with its own width: every file still saves byte for byte.
    /// </summary>
    [Fact]
    public void EveryLoadedDrawing_WrittenBackWithItsOwnWidth_SavesByteForByte()
    {
        var count = 0;
        foreach (var path in new[] { TestData.Kicad10Board, TestData.SNEdgeBoard, TestData.BitaxeGammaBoard, TestData.M2PcieAdapterBoard })
        {
            var board = KiCadBoard.Load(path);
            var before = board.ToText();
            var drawings = board.GraphicLines.Cast<KiCadGraphicItem>()
                .Concat(board.GraphicRectangles)
                .Concat(board.GraphicCircles)
                .Concat(board.GraphicArcs)
                .Concat(board.GraphicPolygons)
                .Concat(board.GraphicCurves)
                .ToList();
            foreach (var drawing in drawings)
            {
                drawing.Width = drawing.Width;
            }

            count += drawings.Count;
            Assert.Equal(before, board.ToText());
        }

        Assert.Equal(53, count);
    }

    // --------------------------------------------------------------------------- KiCad reads it

    /// <summary>
    /// A board with one of each drawing, built through the API, and the same board with every stroke
    /// spelled as the bare <c>(width w)</c> this type wrote before #96, both handed to kicad-cli
    /// 10.0.6. KiCad's own re-save of the two is the same file, apart from the UUIDs it assigns, and
    /// each drawing in it carries the stroke this library now writes.
    /// </summary>
    [Fact]
    public void KiCadReadsTheNewSpellingAsItReadTheOld()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        var scratch = TestData.NewScratchDirectory();
        var board = new KiCadBoard(version: KiCadBoard.Load(TestData.Kicad10Board).Version);
        var line = board.GraphicLines.Add();
        line.Start = new KiCadPosition(0, 0);
        line.End = new KiCadPosition(10, 0);
        line.Width = 0.1;
        line.Layer = KiCadLayerNames.EdgeCuts;
        var rectangle = board.GraphicRectangles.Add();
        rectangle.Start = new KiCadPosition(0, 5);
        rectangle.End = new KiCadPosition(10, 10);
        rectangle.Width = 0.12;
        rectangle.Layer = KiCadLayerNames.FSilkS;
        var circle = board.GraphicCircles.Add();
        circle.Center = new KiCadPosition(20, 5);
        circle.End = new KiCadPosition(22, 5);
        circle.Width = 0.15;
        circle.Layer = KiCadLayerNames.FSilkS;
        var arc = board.GraphicArcs.Add();
        arc.Start = new KiCadPosition(30, 0);
        arc.Mid = new KiCadPosition(32.928932, 7.071068);
        arc.End = new KiCadPosition(40, 10);
        arc.Width = 0.2;
        arc.Layer = KiCadLayerNames.FSilkS;
        var polygon = board.GraphicPolygons.Add();
        polygon.AddPoint(50, 0);
        polygon.AddPoint(55, 0);
        polygon.AddPoint(55, 5);
        polygon.Width = 0.25;
        polygon.Layer = KiCadLayerNames.FSilkS;
        var curve = board.GraphicCurves.Add();
        curve.AddPoint(60, 0);
        curve.AddPoint(62, 5);
        curve.AddPoint(65, 5);
        curve.AddPoint(67, 0);
        curve.Width = 0.3;
        curve.Layer = KiCadLayerNames.FSilkS;

        var newText = board.ToText();
        var oldText = Regex.Replace(newText, @"\(stroke\s*\(width ([0-9.]+)\)\s*\(type solid\)\s*\)", "(width $1)");
        Assert.Equal(6, Regex.Matches(oldText, @"\(width [0-9.]+\)").Count);
        Assert.DoesNotContain("(stroke", oldText, StringComparison.Ordinal);

        var newPath = Path.Combine(scratch, "new.kicad_pcb");
        var oldPath = Path.Combine(scratch, "old.kicad_pcb");
        File.WriteAllText(newPath, newText);
        File.WriteAllText(oldPath, oldText);
        Run(cli, scratch, "pcb", "upgrade", "--force", newPath);
        Run(cli, scratch, "pcb", "upgrade", "--force", oldPath);

        var fromNew = File.ReadAllText(newPath);
        Assert.Equal(WithoutUuids(File.ReadAllText(oldPath)), WithoutUuids(fromNew));

        var upgraded = KiCadBoard.Parse(fromNew);
        Assert.Equal("pcbnew", upgraded.Generator); // KiCad loaded it and wrote it back itself
        var drawings = upgraded.GraphicLines.Cast<KiCadGraphicItem>()
            .Concat(upgraded.GraphicRectangles)
            .Concat(upgraded.GraphicCircles)
            .Concat(upgraded.GraphicArcs)
            .Concat(upgraded.GraphicPolygons)
            .Concat(upgraded.GraphicCurves)
            .ToList();
        Assert.Equal(6, drawings.Count);
        Assert.All(drawings, d => Assert.Equal(KiCadTokens.Common.Solid, d.Stroke!.Type));
        Assert.Equal(
            [0.1, 0.12, 0.15, 0.2, 0.25, 0.3],
            drawings.Select(d => d.Width).Order());
    }

    // ------------------------------------------------------------------------------------- helpers

    private static KiCadGraphicItem Create(string token) => token switch
    {
        "gr_line" => new KiCadGrLine(),
        "gr_rect" => new KiCadGrRect(),
        "gr_circle" => new KiCadGrCircle(),
        "gr_arc" => new KiCadGrArc(),
        "gr_poly" => new KiCadGrPoly(),
        "gr_curve" => new KiCadGrCurve(),
        _ => throw new ArgumentOutOfRangeException(nameof(token), token, null),
    };

    private static string WithoutUuids(string text) => Regex.Replace(text, "\\(uuid \"[^\"]*\"\\)", "(uuid)");

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
