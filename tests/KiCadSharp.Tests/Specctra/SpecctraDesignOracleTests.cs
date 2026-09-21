using KiCadSharp.Documents;
using KiCadSharp.Geometry;
using KiCadSharp.Specctra;

namespace KiCadSharp.Tests.Specctra;

/// <summary>
/// <see cref="SpecctraDesign"/> against the <c>.dsn</c> pcbnew 10.0.6 writes for the same boards.
/// </summary>
/// <remarks>
/// <para>
/// <c>data/oracles/*.dsn</c> came out of <c>pcbnew.ExportSpecctraDSN</c> in KiCad 10.0.6, run on
/// the vendored boards with no project file beside them — so KiCad used its own defaults, which are
/// <see cref="SpecctraOptions"/>'s: 0.2 mm tracks at 0.2 mm, a 0.6/0.3 mm via, 0.25 mm around a bare
/// hole. The boards between them carry every pad shape but custom and chamfered, parts on both
/// sides, through-hole pins and bare holes, planes on outer and inner layers, rule-area keepouts,
/// routed arcs, and locked copper.
/// </para>
/// <para>
/// Compared item by item through <see cref="DsnView"/> within 1.5 µm: the layer stack, every
/// component's placement, every pin's position, rotation and padstack geometry, every image keepout,
/// every net's pins, the default class, every plane and keepout polygon, every wire segment and via.
/// Looser, each for the reason given where it is done: a padstack POLYGON (2.5 µm — both writers
/// grow a rounded corner slightly to cover it, by different amounts), the boundary, and wiring that
/// follows an arc.
/// </para>
/// </remarks>
public class SpecctraDesignOracleTests
{
    private const double Tolerance = 1.5;

    public static TheoryData<string, string> Boards => new()
    {
        { "SNEdge.dsn", TestData.SNEdgeBoard },
        { "bitaxeGamma.dsn", TestData.BitaxeGammaBoard },
        { "m2-pcie-adapter.dsn", TestData.M2PcieAdapterBoard },
        { "SNEdge-unrouted.dsn", Path.Combine(TestData.Root, "oracles", "SNEdge-unrouted.kicad_pcb") },
        { "pad-shapes.dsn", Path.Combine(TestData.Root, "oracles", "pad-shapes.kicad_pcb") },
    };

    [Theory]
    [MemberData(nameof(Boards))]
    public void TheDesignIsTheOneKiCadWrites(string oracle, string boardPath)
    {
        var board = KiCadBoard.Load(boardPath);
        var kicad = DsnView.Load(Path.Combine(TestData.Root, "oracles", oracle));
        var ours = DsnView.Of(SpecctraReader.Parse(SpecctraDesign.Export(board, new SpecctraOptions())));

        Assert.Equal(kicad.Layers, ours.Layers);
        Same("planes", kicad.Planes, ours.Planes);
        Same("keepouts", kicad.Keepouts.Where(k => k.Layer != "signal"), ours.Keepouts.Where(k => k.Layer != "signal"));

        Assert.Equal(kicad.Components.Keys.Order(), ours.Components.Keys.Order());
        foreach (var (reference, theirs) in kicad.Components)
        {
            var mine = ours.Components[reference];
            Assert.True(
                Math.Abs(theirs.X - mine.X) <= Tolerance && Math.Abs(theirs.Y - mine.Y) <= Tolerance
                && theirs.Side == mine.Side && theirs.Rotation == mine.Rotation,
                $"{reference} is placed at {mine.X},{mine.Y} {mine.Side} {mine.Rotation}; KiCad places it at {theirs.X},{theirs.Y} {theirs.Side} {theirs.Rotation}");
            Same($"{reference} keepouts", theirs.Keepouts, mine.Keepouts);
            Assert.Equal(theirs.Pins.Keys.Order(), mine.Pins.Keys.Order());
            foreach (var (pin, expected) in theirs.Pins)
            {
                Assert.True(expected.Matches(mine.Pins[pin], Tolerance), $"{reference}-{pin}:\n  KiCad {expected}\n  here  {mine.Pins[pin]}");
            }
        }

        Assert.Equal(kicad.Nets.Keys.Order(), ours.Nets.Keys.Order());
        foreach (var (net, pins) in kicad.Nets)
        {
            Assert.True(pins.SetEquals(ours.Nets[net]), $"net {net}: KiCad {string.Join(" ", pins.Order())}, here {string.Join(" ", ours.Nets[net].Order())}");
        }

        var theirDefault = kicad.Classes["kicad_default"];
        var myDefault = ours.Classes["kicad_default"];
        Assert.Equal((theirDefault.Width, theirDefault.Clearance, theirDefault.Drill), (myDefault.Width, myDefault.Clearance, myDefault.Drill));
        Same("default via", theirDefault.Via, myDefault.Via);
        Assert.True(theirDefault.Nets.SetEquals(myDefault.Nets));

        // KiCad writes a track arc as ONE straight segment from end to end; this library writes the
        // polyline that follows it. Each side's arc wiring is left out and the rest compared.
        var arcs = ArcPoints(board);
        bool OnArc(DsnView.Shape s) => arcs.Any(points =>
            points.Any(p => Near(p, s.Numbers[0], s.Numbers[1])) && points.Any(p => Near(p, s.Numbers[2], s.Numbers[3])));
        Same("wiring", kicad.Segments.Where(s => !OnArc(s)), ours.Segments.Where(s => !OnArc(s)), unordered: true);
        Same("vias", kicad.Vias, ours.Vias);

        // The boundary: KiCad approximates the outline's arcs with its own vertices, this library
        // with others; both enclose the same board, which is what is asserted - to 0.2 % of area
        // and 5 µm of extent.
        Assert.Equal(kicad.Boundary.Count, ours.Boundary.Count);
        for (var i = 0; i < kicad.Boundary.Count; i++)
        {
            Assert.Equal(Area(kicad.Boundary[i]), Area(ours.Boundary[i]), Area(kicad.Boundary[i]) * 0.002);
            Assert.Equal(kicad.Boundary[i].Min(p => p.X), ours.Boundary[i].Min(p => p.X), tolerance: 5.0);
            Assert.Equal(kicad.Boundary[i].Max(p => p.Y), ours.Boundary[i].Max(p => p.Y), tolerance: 5.0);
        }
    }

    /// <summary>The oracles are what the tests think they are: KiCad 10.0.6's own output.</summary>
    [Theory]
    [MemberData(nameof(Boards))]
    public void TheOracleIsKiCads(string oracle, string boardPath)
    {
        Assert.True(File.Exists(boardPath));
        var parser = SpecctraReader.Parse(File.ReadAllText(Path.Combine(TestData.Root, "oracles", oracle))).Child("parser")!;
        Assert.Equal("KiCad's Pcbnew", parser.Child("host_cad")!.Atom(0));
        Assert.StartsWith("10.0.6", parser.Child("host_version")!.Atom(0), StringComparison.Ordinal);
    }

    /// <summary>Locked copper is `fix` wiring, the router's word for "route to it, never return it".</summary>
    [Fact]
    public void LockedCopperIsFixed()
    {
        var board = KiCadBoard.Load(Path.Combine(TestData.Root, "oracles", "SNEdge-unrouted.kicad_pcb"));

        var wiring = SpecctraDesign.Build(board, new SpecctraOptions()).Child("wiring")!;

        var types = wiring.ChildrenNamed("wire").Select(w => w.Child("type")!.Atom(0)).ToList();
        Assert.NotEmpty(types);
        Assert.All(types, t => Assert.Equal("fix", t));
    }

    /// <summary>A net in a class of its own takes that class's rule and via.</summary>
    [Fact]
    public void ANetClassCarriesItsRuleAndItsVia()
    {
        var board = KiCadBoard.Load(TestData.SNEdgeBoard);
        var options = new SpecctraOptions
        {
            Classes = [new SpecctraNetClass("Power", 0.5, 0.25, 0.8, 0.4) { Nets = ["VCC"] }],
        };

        var network = SpecctraDesign.Build(board, options).Child("network")!;

        var power = network.ChildrenNamed("class").Single(c => c.Atom(0) == "Power");
        Assert.Equal(["Power", "VCC"], power.Atoms);
        Assert.Equal(500, power.Child("rule")!.Child("width")!.Number(0));
        Assert.Equal(250, power.Child("rule")!.Child("clearance")!.Number(0));
        Assert.Equal("Via[0-1]_800:400_um", power.Child("circuit")!.Child("use_via")!.Atom(0));
        Assert.DoesNotContain("VCC", network.ChildrenNamed("class").Single(c => c.Atom(0) == "kicad_default").Atoms);
    }

    /// <summary>A board with no outline has nowhere to route, and says so rather than guessing one.</summary>
    [Fact]
    public void ABoardWithNoOutlineIsRefused()
    {
        var board = KiCadBoard.Load(TestData.ProbeBoard);
        foreach (var line in board.GraphicLines.Where(l => l.Layer == KiCadLayerNames.EdgeCuts).ToList())
        {
            board.GraphicLines.Remove(line);
        }

        foreach (var rect in board.GraphicRectangles.Where(r => r.Layer == KiCadLayerNames.EdgeCuts).ToList())
        {
            board.GraphicRectangles.Remove(rect);
        }

        var refused = Assert.Throws<InvalidOperationException>(() => SpecctraDesign.Export(board, new SpecctraOptions()));
        Assert.Contains("no closed outline", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A word the format would split or misread is quoted; one it would not, is not.</summary>
    [Theory]
    [InlineData("USB_D+", false)]
    [InlineData("USB_D-", true)]
    [InlineData("-5V", false)]
    [InlineData("Net-(U1-CH4_OUT)", true)]
    [InlineData("orbion:SOT-23", true)]
    [InlineData("orbion:R_0402_1005Metric", false)]
    [InlineData("#PWR01", true)]
    [InlineData("a b", true)]
    [InlineData("", true)]
    public void WordsAreQuotedTheWayKiCadQuotesThem(string word, bool quoted) =>
        Assert.Equal(quoted, SpecctraNode.NeedsQuotes(word));

    /// <summary>What this writes, the reader reads back unchanged.</summary>
    [Fact]
    public void TheWriterAndTheReaderAgree()
    {
        var pcb = SpecctraDesign.Build(KiCadBoard.Load(TestData.BitaxeGammaBoard), new SpecctraOptions());

        var again = SpecctraReader.Parse(pcb.ToString());

        Assert.Equal(pcb.ToString(), again.ToString());
    }

    /// <summary>Two multisets of shapes the same within the tolerance; when not, what each lacks.</summary>
    private static void Same(string what, IEnumerable<DsnView.Shape> kicad, IEnumerable<DsnView.Shape> ours, bool unordered = false)
    {
        var left = ours.ToList();
        var lost = new List<DsnView.Shape>();
        foreach (var shape in kicad)
        {
            var at = left.FindIndex(s => s.Matches(shape, Tolerance) || (unordered && s.Matches(Reversed(shape), Tolerance)));
            if (at < 0)
            {
                lost.Add(shape);
            }
            else
            {
                left.RemoveAt(at);
            }
        }

        Assert.True(lost.Count == 0 && left.Count == 0,
            $"{what}: {lost.Count} KiCad has and this does not, {left.Count} the other way.\n  KiCad only: "
            + string.Join("\n              ", lost.Take(8)) + "\n  here only:  " + string.Join("\n              ", left.Take(8)));
    }

    private static DsnView.Shape Reversed(DsnView.Shape segment) =>
        segment with { Numbers = [segment.Numbers[2], segment.Numbers[3], segment.Numbers[0], segment.Numbers[1]] };

    private static double Area(List<(double X, double Y)> polygon)
    {
        var sum = 0.0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var (ax, ay) = polygon[i];
            var (bx, by) = polygon[(i + 1) % polygon.Count];
            sum += (ax * by) - (bx * ay);
        }

        return Math.Abs(sum / 2);
    }

    /// <summary>Each of the board's arcs as the points either writer puts on it, in the design's µm, Y up.</summary>
    private static List<List<(double X, double Y)>> ArcPoints(KiCadBoard board) =>
        board.TrackArcs
            .Select(a => CopperGeometry.ArcPoints(new BoardPoint(a.Start.X, a.Start.Y), new BoardPoint(a.Mid.X, a.Mid.Y), new BoardPoint(a.End.X, a.End.Y))
                .Select(p => (p.X * 1000, -p.Y * 1000))
                .ToList())
            .ToList();

    private static bool Near((double X, double Y) p, double x, double y) => Math.Abs(p.X - x) <= Tolerance && Math.Abs(p.Y - y) <= Tolerance;
}
