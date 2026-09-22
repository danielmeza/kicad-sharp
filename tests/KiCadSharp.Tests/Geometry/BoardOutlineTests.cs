using KiCadSharp.Documents;
using KiCadSharp.Geometry;

namespace KiCadSharp.Tests.Geometry;

/// <summary>
/// The two forms <see cref="BoardOutline"/> used to get wrong on <c>Edge.Cuts</c> (#116): a Bézier,
/// <c>(gr_curve (pts …))</c> or <c>(fp_curve …)</c>, which it dropped, and a rectangle with a corner
/// radius, <c>(gr_rect … (radius r))</c> or <c>(fp_rect …)</c>, which it drew square.
/// <c>data/oracles/outline-curves.kicad_pcb</c> holds both as pcbnew 10.0.6 saved them, and
/// <see cref="BoardOutlineOracleTests"/> measures them against KiCad. These pin the shapes themselves.
/// </summary>
public class BoardOutlineTests
{
    private const double MaxError = CopperGeometry.DefaultMaxError;

    /// <summary>
    /// The probe from #116: three lines and a curve closing a 10 × 10 outline used to leave one
    /// open chain and no outer at all.
    /// </summary>
    [Fact]
    public void ACurveClosesAnOutline()
    {
        var outline = Outline(
            "(gr_line (start 0 0) (end 10 0) (layer \"Edge.Cuts\"))"
            + "(gr_line (start 0 0) (end 0 10) (layer \"Edge.Cuts\"))"
            + "(gr_line (start 0 10) (end 10 10) (layer \"Edge.Cuts\"))"
            + "(gr_curve (pts (xy 10 0) (xy 13 3) (xy 7 7) (xy 10 10)) (layer \"Edge.Cuts\"))");

        Assert.Single(outline.Outers);
        Assert.Empty(outline.Holes);
        Assert.Equal(0, outline.OpenChains);

        // The S bulges out to x ≈ 10.9 near y ≈ 2.4 and in to x ≈ 9.2 near y ≈ 7.6.
        Assert.True(outline.Contains(new BoardPoint(5, 5)));
        Assert.True(outline.Contains(new BoardPoint(10.5, 2.4)));
        Assert.False(outline.Contains(new BoardPoint(9.5, 7.6)));

        // Every point of the curve lies within the chord error of an edge.
        foreach (var t in Enumerable.Range(0, 201).Select(i => i / 200.0))
        {
            var p = Bezier(new(10, 0), new(13, 3), new(7, 7), new(10, 10), t);
            var off = outline.DistanceToEdge(new CopperShape(RoundedShape.Disc(p, 0)));
            Assert.True(off <= MaxError, $"t = {t}: {p} lies {off * 1000:F4} um from the outline");
        }
    }

    /// <summary>
    /// The rectangle from #116: inside a rounded corner, a point off the board used to read as on
    /// it, 0.2 mm inside the edge, where it is 0.546 mm outside.
    /// </summary>
    [Fact]
    public void ARoundedRectangleHasRoundCorners()
    {
        var outline = Outline("(gr_rect (start 0 0) (end 10 10) (radius 2) (layer \"Edge.Cuts\"))");

        var outer = Assert.Single(outline.Outers);
        Assert.Equal(0, outline.OpenChains);
        Assert.Equal(new BoardBox(0, 0, 10, 10), outline.Bounds);

        Assert.False(outline.Contains(new BoardPoint(0.2, 0.2)));
        Assert.True(outline.Contains(new BoardPoint(0.6, 0.6)));
        Assert.True(outline.Contains(new BoardPoint(5, 0.001)));

        // The polygon's vertices lie on the outline, so its chords lie inside it: a point outside
        // reads its true distance or up to a chord error more, one inside up to a chord error less.
        var outside = Math.Sqrt(2) * 1.8 - 2;
        Assert.InRange(Distance(outline, new BoardPoint(0.2, 0.2)), outside - 1e-9, outside + MaxError);
        Assert.InRange(Distance(outline, new BoardPoint(2, 2)), 2 - MaxError, 2 + 1e-9);

        // Every vertex is on a straight side or at the radius from a corner's centre.
        BoardPoint[] centres = [new(2, 2), new(8, 2), new(8, 8), new(2, 8)];
        foreach (var v in outer)
        {
            var onSide = (v.X is 0 or 10 && v.Y is >= 2 and <= 8) || (v.Y is 0 or 10 && v.X is >= 2 and <= 8);
            var onArc = centres.Any(c => Math.Abs(c.DistanceTo(v) - 2) < 1e-9 && Math.Abs(v.X - c.X) <= 2 && Math.Abs(v.Y - c.Y) <= 2
                && (v.X - 5) * (c.X - 5) > 0 && (v.Y - 5) * (c.Y - 5) > 0);
            Assert.True(onSide || onArc, $"{v} is on neither a side nor a corner");
        }
    }

    /// <summary>
    /// KiCad clamps the radius to half the shorter side as it loads the file, so a 10 × 4 rectangle
    /// with a radius of 5 is a stadium; and one written from its far corner is the same rectangle.
    /// </summary>
    [Theory]
    [InlineData("(gr_rect (start 0 0) (end 10 4) (radius 5) (layer \"Edge.Cuts\"))")]
    [InlineData("(gr_rect (start 10 4) (end 0 0) (radius 2) (layer \"Edge.Cuts\"))")]
    public void ARadiusPastHalfTheShorterSideIsClamped(string rectangle)
    {
        var outline = Outline(rectangle);

        var outer = Assert.Single(outline.Outers);
        Assert.Equal(new BoardBox(0, 0, 10, 4), outline.Bounds);
        Assert.True(outline.Contains(new BoardPoint(0.5, 2)));
        Assert.False(outline.Contains(new BoardPoint(0.3, 0.3)));
        Assert.InRange(Distance(outline, new BoardPoint(2, 2)), 2 - MaxError, 2 + 1e-9);
        foreach (var v in outer)
        {
            var onSide = v.Y is 0 or 4 && v.X is >= 2 and <= 8;
            var onEnd = Math.Abs(new BoardPoint(2, 2).DistanceTo(v) - 2) < 1e-9 && v.X <= 2
                || Math.Abs(new BoardPoint(8, 2).DistanceTo(v) - 2) < 1e-9 && v.X >= 8;
            Assert.True(onSide || onEnd, $"{v} is on neither a side nor an end");
        }

        // No two neighbours coincide: the sides that vanished left no zero-length edge behind.
        for (var i = 0; i < outer.Count; i++)
        {
            Assert.True(outer[i].DistanceTo(outer[(i + 1) % outer.Count]) > 0, $"vertex {i} repeats");
        }
    }

    /// <summary>With no radius, or a zero one, a rectangle is the four corners it always was.</summary>
    [Theory]
    [InlineData("(gr_rect (start 0 0) (end 10 4) (layer \"Edge.Cuts\"))")]
    [InlineData("(gr_rect (start 0 0) (end 10 4) (radius 0) (layer \"Edge.Cuts\"))")]
    public void ARectangleWithoutARadiusIsSquare(string rectangle)
    {
        var outer = Assert.Single(Outline(rectangle).Outers);

        Assert.Equal(4, outer.Count);
        Assert.Contains(new BoardPoint(0, 0), outer);
        Assert.Contains(new BoardPoint(10, 4), outer);
    }

    /// <summary>
    /// A footprint's curve and rounded rectangle on <c>Edge.Cuts</c> count too, placed where the
    /// footprint puts them: a rotated rounded rectangle, and a line closed by a curve into a D.
    /// </summary>
    [Fact]
    public void AFootprintsCurveAndRoundedRectangleArePlaced()
    {
        var outline = Outline(
            "(footprint \"RR\" (layer \"F.Cu\") (at 20 10 90)"
            + " (fp_rect (start -4 -2) (end 4 2) (radius 1) (stroke (width 0.1) (type solid)) (fill no) (layer \"Edge.Cuts\")))"
            + "(footprint \"D\" (layer \"F.Cu\") (at 50 10 30)"
            + " (fp_line (start -5 -5) (end -5 5) (stroke (width 0.1) (type solid)) (layer \"Edge.Cuts\"))"
            + " (fp_curve (pts (xy -5 5) (xy 6 5) (xy 6 -5) (xy -5 -5)) (stroke (width 0.1) (type solid)) (layer \"Edge.Cuts\")))");

        Assert.Equal(2, outline.Outers.Count);
        Assert.Equal(0, outline.OpenChains);
        Assert.Empty(outline.Holes);

        // The 8 × 4 rectangle turned by 90° spans 4 × 8 about (20, 10), and its corner is round.
        var rr = outline.Outers.Single(o => o.All(p => p.X < 30));
        Assert.Equal(18, rr.Min(p => p.X), 9);
        Assert.Equal(22, rr.Max(p => p.X), 9);
        Assert.Equal(6, rr.Min(p => p.Y), 9);
        Assert.Equal(14, rr.Max(p => p.Y), 9);
        Assert.False(outline.Contains(new BoardPoint(18.1, 6.1)));
        Assert.True(outline.Contains(new BoardPoint(18.4, 6.4)));

        // The D: its flat side is the line, turned by 30°, and the curve's far point is on the edge.
        var d = outline.Outers.Single(o => o.All(p => p.X > 30));
        var flat = new BoardPoint(50, 10) + new BoardPoint(-5, 0).Rotate(30);
        Assert.Contains(d, p => p.DistanceTo(flat + new BoardPoint(0, -5).Rotate(30)) < 1e-9);
        Assert.Contains(d, p => p.DistanceTo(flat + new BoardPoint(0, 5).Rotate(30)) < 1e-9);
        var apex = new BoardPoint(50, 10) + Bezier(new(-5, 5), new(6, 5), new(6, -5), new(-5, -5), 0.5).Rotate(30);
        Assert.InRange(Distance(outline, apex), 0, MaxError);
        Assert.True(outline.Contains(new BoardPoint(50, 10)));
    }

    /// <summary>
    /// pcbnew 10.0.6 saves a rounded rectangle inside a footprint placed off-axis as a polygon
    /// whose points are four <c>(arc …)</c> entries and nothing else, the sides implied between
    /// them — MEASURED in <c>data/oracles/outline-curves.kicad_pcb</c>, <c>RR1</c> at 30°, which is
    /// this polygon. It is the same outline as the rectangle it was.
    /// </summary>
    [Fact]
    public void APolygonOfArcsIsTheRoundedRectangleItWas()
    {
        var outline = Outline(
            "(footprint \"RR\" (layer \"F.Cu\") (at 90 15 30) (fp_poly (pts"
            + " (arc (start -8 -3) (mid -7.414215 -4.414214) (end -6 -5))"
            + " (arc (start 6 -5) (mid 7.414214 -4.414213) (end 8 -3))"
            + " (arc (start 8 3) (mid 7.414215 4.414214) (end 6 5))"
            + " (arc (start -6 5) (mid -7.414214 4.414213) (end -8 3)))"
            + " (stroke (width 0.1) (type solid)) (fill no) (layer \"Edge.Cuts\")))");
        var rectangle = Outline(
            "(footprint \"RR\" (layer \"F.Cu\") (at 90 15 30)"
            + " (fp_rect (start -8 -5) (end 8 5) (radius 2) (stroke (width 0.1) (type solid)) (fill no) (layer \"Edge.Cuts\")))");

        var outer = Assert.Single(outline.Outers);
        Assert.Equal(0, outline.OpenChains);
        Assert.True(outline.Contains(new BoardPoint(90, 15)));

        // Every vertex of each lies on the other's edge, to the 1 nm pcbnew rounds the arcs to and a chord error.
        foreach (var v in outer)
        {
            Assert.True(Distance(rectangle, v) <= MaxError + 2e-6, $"{v} is {Distance(rectangle, v) * 1000:F4} um off the rectangle");
        }

        foreach (var v in rectangle.Outers[0])
        {
            Assert.True(Distance(outline, v) <= MaxError + 2e-6, $"{v} is {Distance(outline, v) * 1000:F4} um off the polygon");
        }
    }

    /// <summary>A polygon may mix <c>(xy …)</c> and <c>(arc …)</c>; an arc's start may repeat the vertex before it, or not.</summary>
    [Theory]
    [InlineData("(xy 0 0) (xy 8 0) (arc (start 8 0) (mid 9.414213562 0.585786438) (end 10 2)) (xy 10 10) (xy 0 10)")]
    [InlineData("(xy 0 0) (arc (start 8 0) (mid 9.414213562 0.585786438) (end 10 2)) (xy 10 10) (xy 0 10)")]
    public void APolygonMayMixPointsAndArcs(string points)
    {
        var outline = Outline("(gr_poly (pts " + points + ") (layer \"Edge.Cuts\"))");

        var outer = Assert.Single(outline.Outers);
        Assert.Contains(new BoardPoint(0, 0), outer);
        Assert.Contains(new BoardPoint(8, 0), outer);
        Assert.Contains(new BoardPoint(10, 2), outer);
        Assert.Contains(new BoardPoint(10, 10), outer);
        Assert.Contains(new BoardPoint(0, 10), outer);
        Assert.False(outline.Contains(new BoardPoint(9.9, 0.1)));
        Assert.True(outline.Contains(new BoardPoint(9, 1)));
        Assert.True(outline.Contains(new BoardPoint(0.1, 9.9)));
        for (var i = 0; i < outer.Count; i++)
        {
            Assert.True(outer[i].DistanceTo(outer[(i + 1) % outer.Count]) > 0, $"vertex {i} repeats");
        }
    }

    /// <summary>A curve with other than four points is not a Bézier KiCad would load; it is left out, as a primitive is.</summary>
    [Fact]
    public void ACurveWithoutFourPointsIsLeftOut()
    {
        var outline = Outline("(gr_curve (pts (xy 0 0) (xy 5 5) (xy 10 0)) (layer \"Edge.Cuts\"))");

        Assert.Empty(outline.Outers);
        Assert.Equal(0, outline.OpenChains);
        Assert.Null(outline.Bounds);
    }

    /// <summary>A curve on another layer is not an edge.</summary>
    [Fact]
    public void ACurveOffEdgeCutsIsNotAnEdge()
    {
        var outline = Outline("(gr_curve (pts (xy 0 0) (xy 3 3) (xy 7 7) (xy 10 0)) (layer \"F.SilkS\"))");

        Assert.Null(outline.Bounds);
    }

    private static BoardOutline Outline(string shapes) => BoardOutline.Of(KiCadBoard.Parse("(kicad_pcb " + shapes + ")"));

    private static double Distance(BoardOutline outline, BoardPoint p) => outline.DistanceToEdge(new CopperShape(RoundedShape.Disc(p, 0)));

    private static BoardPoint Bezier(BoardPoint p0, BoardPoint p1, BoardPoint p2, BoardPoint p3, double t)
    {
        var u = 1 - t;
        return (p0 * (u * u * u)) + (p1 * (3 * t * u * u)) + (p2 * (3 * t * t * u)) + (p3 * (t * t * t));
    }
}
