using System.Globalization;

using KiCadSharp.Documents;
using KiCadSharp.Geometry;
using KiCadSharp.Specctra;

namespace KiCadSharp.Tests.Geometry;

/// <summary>
/// The two primitive forms <see cref="CopperGeometry"/> used to get wrong (#77): a Bézier,
/// <c>(gr_curve (pts …))</c>, which it dropped, and a rectangle with a corner radius,
/// <c>(gr_rect … (radius r))</c>, which it drew square. Both are what KiCad 10.0.6 writes inside a
/// pad's <c>(primitives …)</c>; <c>data/oracles/custom-primitives.kicad_pcb</c> holds them as pcbnew
/// saved them, and <see cref="CopperGeometryOracleTests"/> measures them against KiCad. These pin the
/// shapes themselves.
/// </summary>
public class CustomPadPrimitiveTests
{
    private const double MaxError = CopperGeometry.DefaultMaxError;

    /// <summary>The curve from #77, which used to leave only the anchor.</summary>
    [Fact]
    public void ABezierIsCopper()
    {
        var copper = Pad("(gr_curve (pts (xy 0 0) (xy 1 2) (xy 3 2) (xy 4 0)) (width 0.2))");

        // The curve peaks at t = ½, at (2, 1.5); the pen reaches 0.1 past it, the covering capsules
        // at most two chord errors more.
        Assert.InRange(copper.Bounds.MaxX, 4.1, 4.1 + (2 * MaxError));
        Assert.InRange(copper.Bounds.MaxY, 1.6, 1.6 + (2 * MaxError));
        foreach (var t in Enumerable.Range(0, 101).Select(i => i / 100.0))
        {
            var p = Bezier(new(0, 0), new(1, 2), new(3, 2), new(4, 0), t);
            Assert.True(copper.Contains(p), $"t = {t}: {p} is on the curve and not in the copper");
        }

        Assert.False(copper.Contains(new BoardPoint(2, 1.6 + (3 * MaxError))));
        Assert.False(copper.Contains(new BoardPoint(2, 1.4 - (3 * MaxError))));
    }

    /// <summary>
    /// Every point of the curve lies within the chord error of the polyline, and every vertex lies
    /// on the curve: an arch, an S with an inflection, a loop that crosses itself, and a cusp whose
    /// inner control points reach back past its ends.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0.5, 1.2, 1.5, 1.2, 2, 0)]
    [InlineData(0, 0, 0.8, -1.2, 1.2, 1.2, 2, 0)]
    [InlineData(0, 0, 2.2, 1.6, -0.2, 1.6, 2, 0)]
    [InlineData(0, 0, 3, 0, -2, 0, 1, 0)]
    [InlineData(0, 0, 40, 30, -10, 30, 30, 0)]
    public void ABezierIsFlattenedWithinTheChordError(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
    {
        BoardPoint[] c = [new(x0, y0), new(x1, y1), new(x2, y2), new(x3, y3)];

        var points = CopperGeometry.BezierPoints(c[0], c[1], c[2], c[3], MaxError);

        Assert.Equal(c[0], points[0]);
        Assert.Equal(c[3], points[^1]);
        var samples = Enumerable.Range(0, 20001).Select(i => Bezier(c[0], c[1], c[2], c[3], i / 20000.0)).ToList();
        foreach (var p in samples)
        {
            var off = Enumerable.Range(0, points.Count - 1).Min(i => Planar.PointSegment(p, points[i], points[i + 1]));
            Assert.True(off <= MaxError, $"{p} lies {off * 1000:F4} um from the polyline");
        }

        foreach (var v in points)
        {
            Assert.True(samples.Min(s => s.DistanceTo(v)) < 0.01, $"{v} is not on the curve");
        }
    }

    /// <summary>A straight Bézier is one chord; one whose points all coincide is a disc.</summary>
    [Fact]
    public void ADegenerateBezierIsStillItsPen()
    {
        Assert.Equal(2, CopperGeometry.BezierPoints(new(0, 0), new(1, 0), new(2, 0), new(3, 0), MaxError).Count);

        var dot = Pad("(gr_curve (pts (xy 2 2) (xy 2 2) (xy 2 2) (xy 2 2)) (width 0.2))");

        Assert.True(dot.Contains(new BoardPoint(2.1, 2)));
        Assert.False(dot.Contains(new BoardPoint(2.1 + (3 * MaxError), 2)));
    }

    /// <summary>
    /// The rectangle from #77: filled, it is the rectangle shrunk by the radius and swept by it,
    /// exactly as a round-rectangle pad is — and its sharp corner is not copper.
    /// </summary>
    [Fact]
    public void AFilledRoundedRectangleIsExact()
    {
        var copper = Pad("(gr_rect (start -2 -1) (end 2 1) (radius 0.8) (width 0) (fill yes))");

        var part = copper.Parts[1];
        Assert.Equal(0.8, part.Radius, 12);
        Same([new(-1.2, -0.2), new(1.2, -0.2), new(1.2, 0.2), new(-1.2, 0.2)], part.Core);
        Assert.False(copper.Contains(new BoardPoint(2, 1)));
        var corner = new BoardPoint(1.2, 0.2) + (new BoardPoint(1, 1) * (0.8 / Math.Sqrt(2)));
        Assert.True(part.Contains(corner * 0.99999));
    }

    /// <summary>A pen on a filled rounded rectangle grows it: the corner radius and the sides by half the pen.</summary>
    [Fact]
    public void AFilledRoundedRectangleWithAPenGrowsByHalfThePen()
    {
        var part = Pad("(gr_rect (start -0.9 -0.5) (end 0.9 0.5) (radius 0.25) (width 0.1) (fill yes))").Parts[1];

        Assert.Equal(0.3, part.Radius, 12);
        Same([new(-0.65, -0.25), new(0.65, -0.25), new(0.65, 0.25), new(-0.65, 0.25)], part.Core);
    }

    /// <summary>
    /// KiCad clamps the radius to half the shorter side on load, and a rectangle written from its
    /// far corner is the same rectangle: both give the stadium.
    /// </summary>
    [Theory]
    [InlineData("(gr_rect (start -2 -1) (end 2 1) (radius 3) (width 0) (fill yes))")]
    [InlineData("(gr_rect (start 2 1) (end -2 -1) (radius 1) (width 0) (fill yes))")]
    public void ARadiusPastHalfTheShorterSideIsClamped(string primitive)
    {
        var part = Pad(primitive).Parts[1];

        Assert.Equal(1, part.Radius, 12);
        Assert.Equal(2, part.Core.Count);
        Assert.Equal(new[] { -1.0, 1.0 }, part.Core.Select(p => p.X).Order());
        Assert.All(part.Core, p => Assert.Equal(0, p.Y, 12));
    }

    /// <summary>With no radius, or a zero one, a rectangle is what it always was.</summary>
    [Theory]
    [InlineData("(gr_rect (start -2 -1) (end 2 1) (width 0) (fill yes))")]
    [InlineData("(gr_rect (start -2 -1) (end 2 1) (radius 0) (width 0) (fill yes))")]
    public void ARectangleWithoutARadiusIsSquare(string primitive)
    {
        var part = Pad(primitive).Parts[1];

        Assert.Equal(0, part.Radius);
        Same([new(-2, -1), new(2, -1), new(2, 1), new(-2, 1)], part.Core);
    }

    /// <summary>
    /// Stroked, a rounded rectangle is its outline drawn with the pen: the pen covers the rounded
    /// corner and not the square one, and nothing inside.
    /// </summary>
    [Fact]
    public void AStrokedRoundedRectangleIsItsOutline()
    {
        var copper = Pad("(gr_rect (start -2 -1) (end 2 1) (radius 0.5) (width 0.1) (fill no))", anchor: 0.1);

        var arc = new BoardPoint(1.5, 0.5) + (new BoardPoint(1, 1) * (0.5 / Math.Sqrt(2)));
        foreach (var inset in new[] { -0.0499, 0, 0.0499 })
        {
            Assert.True(copper.Contains(arc + (new BoardPoint(1, 1) * (inset / Math.Sqrt(2)))), $"{inset} from the corner arc");
        }

        Assert.False(copper.Contains(new BoardPoint(2, 1)));
        Assert.False(copper.Contains(new BoardPoint(1.5, 0.5)));
        Assert.True(copper.Contains(new BoardPoint(0, 1.0499)));
        Assert.False(copper.Contains(new BoardPoint(0, 0.9)));
    }

    /// <summary>
    /// A square whose radius is half its side is a circle; stroked, it is a ring with no sides at
    /// all. KiCad 10.0.6 plots this one as a dot (see <c>data/oracles/README.md</c>); its DRC, and
    /// this, see the ring.
    /// </summary>
    [Fact]
    public void AStrokedRoundedSquareOfHalfItsSideIsARing()
    {
        var copper = Pad("(gr_rect (start -0.5 -0.5) (end 0.5 0.5) (radius 0.5) (width 0.1) (fill no))", anchor: 0.1);

        foreach (var degrees in Enumerable.Range(0, 72).Select(i => i * 5.0))
        {
            var a = degrees * Math.PI / 180;
            var on = new BoardPoint(Math.Cos(a), Math.Sin(a));
            Assert.True(copper.Contains(on * 0.5), $"{degrees} degrees");
            Assert.True(copper.Contains(on * 0.5499), $"{degrees} degrees, outer edge");
            Assert.False(copper.Contains(on * (0.55 + (3 * MaxError))), $"{degrees} degrees, past the outer edge");
            Assert.False(copper.Contains(on * (0.45 - (3 * MaxError))), $"{degrees} degrees, inside the inner edge");
        }
    }

    /// <summary>
    /// A custom pad with no <c>(primitives …)</c> at all is its anchor alone (#115). pcbnew 10.0.6
    /// writes the form for every custom pad, even an empty one; a pad built in code can leave it
    /// out, and this used to throw on it — from <see cref="CopperGeometry.Pad"/> and from the
    /// design export, which goes through the same code.
    /// </summary>
    [Fact]
    public void ACustomPadWithoutPrimitivesIsItsAnchor()
    {
        var board = KiCadBoard.Parse(
            "(kicad_pcb (layers (0 \"F.Cu\" signal) (2 \"B.Cu\" signal) (25 \"Edge.Cuts\" user))"
            + " (gr_rect (start -5 -5) (end 5 5) (layer \"Edge.Cuts\"))"
            + " (footprint \"F\" (layer \"F.Cu\") (at 0 0) (pad \"1\" smd custom (at 0 0) (size 0.5 0.5)"
            + " (layers \"F.Cu\") (options (clearance outline) (anchor circle)))))");
        var footprint = board.Footprints.Single();

        var copper = CopperGeometry.Pad(footprint, footprint.Pads.Single());

        var anchor = Assert.Single(copper.Parts);
        Assert.Equal(0.25, anchor.Radius, 12);
        Assert.True(copper.Contains(new BoardPoint(0.24, 0)));
        Assert.False(copper.Contains(new BoardPoint(0.26, 0)));
        Assert.Contains("padstack", SpecctraDesign.Export(board, new SpecctraOptions()), StringComparison.Ordinal);
    }

    /// <summary>The copper of a lone custom pad at the origin holding one primitive.</summary>
    private static CopperShape Pad(string primitive, double anchor = 0.5)
    {
        var board = KiCadBoard.Parse(
            "(kicad_pcb (footprint \"F\" (layer \"F.Cu\") (at 0 0) (pad \"1\" smd custom (at 0 0) "
            + string.Create(CultureInfo.InvariantCulture, $"(size {anchor} {anchor}) ")
            + "(layers \"F.Cu\") (options (clearance outline) (anchor circle)) (primitives " + primitive + "))))");
        var footprint = board.Footprints.Single();
        return CopperGeometry.Pad(footprint, footprint.Pads.Single());
    }

    private static void Same(BoardPoint[] expected, IReadOnlyList<BoardPoint> actual)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.True(expected[i].DistanceTo(actual[i]) < 1e-12, $"vertex {i}: {actual[i]}, not {expected[i]}");
        }
    }

    private static BoardPoint Bezier(BoardPoint p0, BoardPoint p1, BoardPoint p2, BoardPoint p3, double t)
    {
        var u = 1 - t;
        return (p0 * (u * u * u)) + (p1 * (3 * t * u * u)) + (p2 * (3 * t * t * u)) + (p3 * (t * t * t));
    }
}
