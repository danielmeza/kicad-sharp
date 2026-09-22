using KiCadSharp.Documents;
using KiCadSharp.Geometry;
using KiCadSharp.Tests.Specctra;

namespace KiCadSharp.Tests.Geometry;

/// <summary>
/// <see cref="BoardOutline"/> against the outline pcbnew 10.0.6 itself draws for a board whose
/// <c>Edge.Cuts</c> holds a Bézier and a rectangle with a corner radius, on the board and inside
/// footprints, as outers and as holes (#116).
/// </summary>
/// <remarks>
/// <para>
/// <c>data/oracles/outline-curves.dsn</c> is <c>ExportSpecctraDSN</c> of
/// <c>outline-curves.kicad_pcb</c>. Its boundary paths are KiCad's own outline polygon
/// (<c>BOARD::GetBoardPolygonOutlines</c>, one path per outer) and its <c>signal</c>-layer keepouts
/// are that polygon's holes, so the two libraries' outlines can be compared vertex by vertex, in
/// both directions: every vertex of KiCad's polygon against this library's edges, and every vertex
/// of this library's polygon against KiCad's.
/// </para>
/// <para>
/// MEASURED on that file, with no library code, against the shapes as written: KiCad flattens the
/// Bézier within the board's max error of 5 µm, its 71 vertices on the curve and the curve sagging
/// at most 4.98 µm from its chords, which cut the inside of each bend; and it draws a rounded
/// corner with vertices OUTSIDE the true arc — 0 to 0.99 µm on the <c>fp_rect</c> of radius 2,
/// 0 to 0.55 µm on the <c>gr_rect</c> of radius 5 and on the stadium hole of radius 3, and −0.05
/// to 0.55 µm on the arcs of the polygon pcbnew made of the rectangle it turned by 30°. This
/// library puts every vertex on the curve within a 0.5 µm chord.
/// </para>
/// <para>
/// MEASURED here, vertex to edge, in µm, as KiCad's off this outline / this outline's off KiCad's:
/// the radius-2 <c>fp_rect</c> 1.37 / 0.82, the polygon of arcs 1.02 / 0.51, the radius-5
/// <c>gr_rect</c> 1.01 / 0.52, the stadium hole 1.00 / 0.51, the three lines and the S curve
/// 0.41 / 4.96, the line and the curve of the D 0.49 / 4.98, the two curves of the pillow hole
/// 0.20 / 4.98. So KiCad's vertices lie within its 1 µm corner overshoot plus a chord here, and this
/// outline's within KiCad's 5 µm chords, plus the 0.1 µm the design's numbers are rounded to.
/// </para>
/// </remarks>
public class BoardOutlineOracleTests
{
    /// <summary>KiCad's vertices against this outline: its 1 µm rounded-corner overshoot, a 0.5 µm chord here, 0.1 µm of rounding.</summary>
    private const double KiCadToHere = 1.6;

    /// <summary>This outline's vertices against KiCad's polygon: within its 5 µm max error and the design's rounding.</summary>
    private const double HereToKiCad = 5.2;

    [Fact]
    public void EveryOuterAndEveryHoleIsTheOneKiCadDraws()
    {
        var board = KiCadBoard.Load(Path.Combine(TestData.Root, "oracles", "outline-curves.kicad_pcb"));
        var kicad = DsnView.Load(Path.Combine(TestData.Root, "oracles", "outline-curves.dsn"));

        var outline = BoardOutline.Of(board);

        // Five outers: three lines closed by an S curve; a rounded rectangle; a footprint's rounded
        // rectangle turned 30°, which pcbnew saved as a polygon of four arcs; the same footprint
        // turned 90°, whose rectangle stayed an fp_rect with its radius; and a footprint's line
        // closed by a curve turned −30°. Two holes in the first: a rounded rectangle whose radius is
        // clamped into a stadium, and a footprint's two curves turned 45°.
        Assert.Equal(5, kicad.Boundary.Count);
        Assert.Equal(5, outline.Outers.Count);
        Assert.Equal(2, outline.Holes.Count);
        Assert.Equal(0, outline.OpenChains);
        var kicadHoles = kicad.Keepouts.Where(k => k.Layer == "signal").Select(k => Closed(Pairs(k.Numbers))).ToList();
        Assert.Equal(2, kicadHoles.Count);

        var failures = new List<string>();
        foreach (var (theirs, mine, what) in Match(kicad.Boundary.Select(Closed).ToList(), outline.Outers, "outer").Concat(Match(kicadHoles, outline.Holes, "hole")))
        {
            var ours = mine.Select(p => (p.X * 1000, -p.Y * 1000)).ToList();
            var toHere = Worst(theirs, ours);
            var toKiCad = Worst(ours, theirs);
            if (toHere > KiCadToHere || toKiCad > HereToKiCad)
            {
                failures.Add($"{what} {mine.Count} vertices vs KiCad's {theirs.Count}: KiCad's lie up to {toHere:F3} um off this outline, this outline's up to {toKiCad:F3} um off KiCad's");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>Each of KiCad's polygons paired with the one here whose box it shares, by the middle of that box.</summary>
    private static IEnumerable<(List<(double X, double Y)> Theirs, IReadOnlyList<BoardPoint> Mine, string What)> Match(
        IReadOnlyList<List<(double X, double Y)>> theirs, IReadOnlyList<IReadOnlyList<BoardPoint>> mine, string what)
    {
        var left = mine.ToList();
        foreach (var polygon in theirs)
        {
            var centre = ((polygon.Min(p => p.X) + polygon.Max(p => p.X)) / 2, -(polygon.Min(p => p.Y) + polygon.Max(p => p.Y)) / 2000);
            var at = left.FindIndex(m => Math.Abs(((m.Min(p => p.X) + m.Max(p => p.X)) / 2) - (centre.Item1 / 1000)) < 0.01
                && Math.Abs(((m.Min(p => p.Y) + m.Max(p => p.Y)) / 2) - centre.Item2) < 0.01);
            Assert.True(at >= 0, $"KiCad's {what} about ({centre.Item1 / 1000:F3}, {centre.Item2:F3}) has no match here");
            yield return (polygon, left[at], what);
            left.RemoveAt(at);
        }

        Assert.Empty(left);
    }

    /// <summary>The furthest any vertex of one polygon lies from the other's edges, micrometres.</summary>
    private static double Worst(IReadOnlyList<(double X, double Y)> from, IReadOnlyList<(double X, double Y)> to) =>
        from.Max(p => Enumerable.Range(0, to.Count).Min(i => Segment(p, to[i], to[(i + 1) % to.Count])));

    private static double Segment((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        var length = (dx * dx) + (dy * dy);
        var t = length == 0 ? 0 : Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / length, 0, 1);
        var (ex, ey) = (p.X - a.X - (t * dx), p.Y - a.Y - (t * dy));
        return Math.Sqrt((ex * ex) + (ey * ey));
    }

    private static List<(double X, double Y)> Pairs(IReadOnlyList<double> numbers)
    {
        var points = new List<(double X, double Y)>();
        for (var i = 0; i + 1 < numbers.Count; i += 2)
        {
            points.Add((numbers[i], numbers[i + 1]));
        }

        return points;
    }

    /// <summary>KiCad closes a path by writing its first point again; the polygon is the points before that.</summary>
    private static List<(double X, double Y)> Closed(List<(double X, double Y)> path) =>
        path.Count > 1 && path[0] == path[^1] ? path[..^1] : path;
}
