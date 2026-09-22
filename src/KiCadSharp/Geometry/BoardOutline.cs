using System;
using System.Collections.Generic;
using System.Linq;

using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Geometry
{
    /// <summary>
    /// A board's outline, from everything drawn on <c>Edge.Cuts</c>: the closed shapes it forms,
    /// which of them are the board and which are cut out of it, and every edge as copper sees it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lines, arcs and Bézier curves are chained end to end within <see cref="DefaultChainTolerance"/>;
    /// circles, rectangles — with a corner radius or without — and polygons are closed already. A
    /// closed shape inside an odd number of others is a cutout — a mounting hole, a slot — and inside
    /// an even number, board. Footprint graphics on <c>Edge.Cuts</c> count, placed where the
    /// footprint puts them.
    /// </para>
    /// <para>
    /// An arc, a circle, a curve, a rounded corner and an <c>(arc …)</c> among a polygon's points
    /// become points ON them, close enough that no chord strays more than <c>maxError</c> from the
    /// true edge. A chord cuts the inside of its bend, so a shape's polygon lies within the shape by
    /// at most that much — the board's within the board, a cutout's within the cutout. A rectangle's
    /// corner radius is the one written, clamped as KiCad clamps it on load to half the shorter side.
    /// pcbnew saves a rounded rectangle inside a footprint placed off-axis as a polygon of four arcs
    /// (MEASURED: <c>data/oracles/outline-curves.kicad_pcb</c>, <c>RR1</c> at 30°), which is why
    /// the arcs matter here.
    /// </para>
    /// <para>
    /// <b>A cutout is an edge.</b> A track is as far from the rim of a mounting-hole cutout as it is
    /// from the board's own edge, and the fabricator mills both. <see cref="Edges"/> therefore holds
    /// the edges of every shape — and the pieces that failed to chain, too, because an outline drawn
    /// with a gap is still somewhere the router must not put copper.
    /// </para>
    /// </remarks>
    public sealed class BoardOutline
    {
        /// <summary>How close two ends must be to join: 1 µm.</summary>
        public const double DefaultChainTolerance = 0.001;

        private readonly CopperShape? _edges;

        private BoardOutline(
            IReadOnlyList<IReadOnlyList<BoardPoint>> outers,
            IReadOnlyList<IReadOnlyList<BoardPoint>> holes,
            IReadOnlyList<RoundedShape> edges,
            int openChains)
        {
            Outers = outers;
            Holes = holes;
            Edges = edges;
            OpenChains = openChains;
            _edges = edges.Count == 0 ? null : new CopperShape(edges);
        }

        /// <summary>Gets the closed shapes that are board, largest first.</summary>
        public IReadOnlyList<IReadOnlyList<BoardPoint>> Outers { get; }

        /// <summary>Gets the closed shapes cut out of the board.</summary>
        public IReadOnlyList<IReadOnlyList<BoardPoint>> Holes { get; }

        /// <summary>Gets every edge, as zero-width capsules.</summary>
        public IReadOnlyList<RoundedShape> Edges { get; }

        /// <summary>Gets how many chains of lines, arcs and curves did not close — an outline drawn with a gap.</summary>
        public int OpenChains { get; }

        /// <summary>Gets the box around the whole outline, or <see langword="null"/> when nothing is drawn.</summary>
        public BoardBox? Bounds => _edges?.Bounds;

        /// <summary>Reads the outline off a board.</summary>
        /// <param name="board">The board.</param>
        /// <param name="maxError">Chord error for arcs, circles, curves and rounded corners, millimetres.</param>
        /// <param name="chainTolerance">How close two ends must be to join, millimetres.</param>
        /// <returns>The outline.</returns>
        public static BoardOutline Of(
            KiCadBoard board,
            double maxError = CopperGeometry.DefaultMaxError,
            double chainTolerance = DefaultChainTolerance)
        {
            ArgumentNullException.ThrowIfNull(board);
            var open = new List<List<BoardPoint>>();
            var closed = new List<List<BoardPoint>>();

            foreach (var line in board.GraphicLines.Where(OnEdge))
            {
                open.Add([CopperGeometry.Point(line.Start), CopperGeometry.Point(line.End)]);
            }

            foreach (var arc in board.GraphicArcs.Where(OnEdge))
            {
                open.Add([.. CopperGeometry.ArcPoints(CopperGeometry.Point(arc.Start), CopperGeometry.Point(arc.Mid), CopperGeometry.Point(arc.End), maxError)]);
            }

            foreach (var curve in board.GraphicCurves.Where(OnEdge))
            {
                if (CurvePoints(curve.Points.Select(CopperGeometry.Point).ToList(), maxError) is { } points)
                {
                    open.Add(points);
                }
            }

            foreach (var circle in board.GraphicCircles.Where(OnEdge))
            {
                closed.Add(CirclePoints(CopperGeometry.Point(circle.Center), CopperGeometry.Point(circle.End), maxError));
            }

            foreach (var rect in board.GraphicRectangles.Where(OnEdge))
            {
                closed.Add(RectanglePoints(CopperGeometry.Point(rect.Start), CopperGeometry.Point(rect.End), rect.CornerRadius, maxError));
            }

            foreach (var poly in board.GraphicPolygons.Where(OnEdge))
            {
                closed.Add(PolygonPoints(poly.Node, p => p, maxError));
            }

            foreach (var footprint in board.Footprints)
            {
                BoardPoint Place(KiCadPosition p) => CopperGeometry.ToBoard(footprint, CopperGeometry.Point(p));

                foreach (var line in footprint.Lines.Where(l => l.Layer == KiCadLayerNames.EdgeCuts))
                {
                    open.Add([Place(line.Start), Place(line.End)]);
                }

                foreach (var arc in footprint.Arcs.Where(a => a.Layer == KiCadLayerNames.EdgeCuts))
                {
                    open.Add([.. CopperGeometry.ArcPoints(Place(arc.Start), Place(arc.Mid), Place(arc.End), maxError)]);
                }

                foreach (var curve in footprint.Curves.Where(c => c.Layer == KiCadLayerNames.EdgeCuts))
                {
                    // Placing is affine, so the curve through the placed control points is the placed curve.
                    if (CurvePoints(curve.Points.Select(Place).ToList(), maxError) is { } points)
                    {
                        open.Add(points);
                    }
                }

                foreach (var circle in footprint.Circles.Where(c => c.Layer == KiCadLayerNames.EdgeCuts))
                {
                    closed.Add(CirclePoints(Place(circle.Center), Place(circle.End), maxError));
                }

                foreach (var rect in footprint.Rectangles.Where(r => r.Layer == KiCadLayerNames.EdgeCuts))
                {
                    closed.Add(RectanglePoints(CopperGeometry.Point(rect.Start), CopperGeometry.Point(rect.End), rect.CornerRadius, maxError)
                        .Select(p => CopperGeometry.ToBoard(footprint, p))
                        .ToList());
                }

                foreach (var poly in footprint.Polygons.Where(p => p.Layer == KiCadLayerNames.EdgeCuts))
                {
                    closed.Add(PolygonPoints(poly.Node, p => CopperGeometry.ToBoard(footprint, p), maxError));
                }
            }

            var (chains, loose) = Chain(open, chainTolerance);
            closed.AddRange(chains);

            var edges = new List<RoundedShape>();
            foreach (var polygon in closed)
            {
                for (var i = 0; i < polygon.Count; i++)
                {
                    edges.Add(RoundedShape.Capsule(polygon[i], polygon[(i + 1) % polygon.Count], 0));
                }
            }

            foreach (var chain in loose)
            {
                for (var i = 0; i + 1 < chain.Count; i++)
                {
                    edges.Add(RoundedShape.Capsule(chain[i], chain[i + 1], 0));
                }
            }

            var ordered = closed.Where(p => p.Count >= 3).OrderByDescending(p => Math.Abs(Area(p))).ToList();
            var outers = new List<IReadOnlyList<BoardPoint>>();
            var holes = new List<IReadOnlyList<BoardPoint>>();
            for (var i = 0; i < ordered.Count; i++)
            {
                var depth = 0;
                for (var j = 0; j < i; j++)
                {
                    if (Planar.Inside(ordered[i][0], [.. ordered[j]]))
                    {
                        depth++;
                    }
                }

                (depth % 2 == 0 ? outers : holes).Add(ordered[i]);
            }

            return new BoardOutline(outers, holes, edges, loose.Count);
        }

        /// <summary>The distance from the copper to the nearest edge — outline or cutout.</summary>
        /// <param name="copper">The copper.</param>
        /// <returns>Millimetres; <see cref="double.MaxValue"/> when the board has no outline.</returns>
        public double DistanceToEdge(CopperShape copper)
        {
            ArgumentNullException.ThrowIfNull(copper);
            return _edges is null ? double.MaxValue : _edges.DistanceTo(copper);
        }

        /// <summary>Whether a point is on the board: inside an outer shape and in none of the cutouts.</summary>
        /// <param name="p">The point.</param>
        /// <returns>True when it is.</returns>
        public bool Contains(BoardPoint p) =>
            Outers.Any(o => Planar.Inside(p, [.. o])) && !Holes.Any(h => Planar.Inside(p, [.. h]));

        /// <summary>The signed area, positive when the points run counter-clockwise on a Y-up frame.</summary>
        /// <param name="polygon">The polygon.</param>
        /// <returns>Square millimetres.</returns>
        public static double Area(IReadOnlyList<BoardPoint> polygon)
        {
            ArgumentNullException.ThrowIfNull(polygon);
            var sum = 0.0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                sum += (a.X * b.Y) - (b.X * a.Y);
            }

            return sum / 2;
        }

        private static bool OnEdge(KiCadGraphicItem item) => item.Layer == KiCadLayerNames.EdgeCuts;

        private static List<BoardPoint> CirclePoints(BoardPoint centre, BoardPoint rim, double maxError)
        {
            var r = centre.DistanceTo(rim);
            var east = new BoardPoint(centre.X + r, centre.Y);
            var west = new BoardPoint(centre.X - r, centre.Y);
            var top = CopperGeometry.ArcPoints(east, new BoardPoint(centre.X, centre.Y - r), west, maxError);
            var bottom = CopperGeometry.ArcPoints(west, new BoardPoint(centre.X, centre.Y + r), east, maxError);
            return [.. top, .. bottom.Skip(1).Take(bottom.Count - 2)];
        }

        /// <summary>
        /// Points on a cubic Bézier through its four control points, or <see langword="null"/> when
        /// there are not four of them: KiCad's parser requires four, so such a curve is no curve.
        /// </summary>
        private static List<BoardPoint>? CurvePoints(IReadOnlyList<BoardPoint> control, double maxError) =>
            control.Count == 4 ? [.. CopperGeometry.BezierPoints(control[0], control[1], control[2], control[3], maxError)] : null;

        /// <summary>
        /// A polygon's vertices in order, each <c>(xy …)</c> placed, and each <c>(arc (start …)
        /// (mid …) (end …))</c> among them — KiCad 7+ writes those — drawn as points on the arc.
        /// Consecutive entries join with a straight edge, so an arc's start need not repeat the
        /// vertex before it.
        /// </summary>
        private static List<BoardPoint> PolygonPoints(SExpression polygon, Func<BoardPoint, BoardPoint> place, double maxError)
        {
            var points = new List<BoardPoint>();
            if (polygon.GetChild(KiCadTokens.Common.Pts) is { } pts)
            {
                foreach (var entry in pts.Children)
                {
                    if (entry.Token == KiCadTokens.Common.Xy)
                    {
                        Append(points, place(Xy(entry)));
                    }
                    else if (entry.Token == KiCadTokens.Common.Arc)
                    {
                        // Placing turns and moves, so the arc through the placed points is the placed arc.
                        var arc = CopperGeometry.ArcPoints(
                            place(Xy(entry.GetChild(KiCadTokens.Common.Start))),
                            place(Xy(entry.GetChild(KiCadTokens.Common.Mid))),
                            place(Xy(entry.GetChild(KiCadTokens.Common.End))),
                            maxError);
                        foreach (var p in arc)
                        {
                            Append(points, p);
                        }
                    }
                }
            }

            return Close(points);
        }

        /// <summary>
        /// A rectangle's four corners — or, with a corner radius, its straight sides and points on
        /// its four quarter arcs, clockwise on screen from the top side. A side that a large radius
        /// swallows leaves no zero-length edge behind.
        /// </summary>
        private static List<BoardPoint> RectanglePoints(BoardPoint start, BoardPoint end, double cornerRadius, double maxError)
        {
            var r = CopperGeometry.CornerRadius(start, end, cornerRadius);
            if (r <= 0)
            {
                return [start, new(end.X, start.Y), end, new(start.X, end.Y)];
            }

            var x0 = Math.Min(start.X, end.X);
            var y0 = Math.Min(start.Y, end.Y);
            var x1 = Math.Max(start.X, end.X);
            var y1 = Math.Max(start.Y, end.Y);
            var diagonal = r / Math.Sqrt(2);
            var points = new List<BoardPoint>();

            void Corner(BoardPoint from, BoardPoint centre, double sx, double sy, BoardPoint to)
            {
                foreach (var p in CopperGeometry.ArcPoints(from, centre + new BoardPoint(sx * diagonal, sy * diagonal), to, maxError))
                {
                    Append(points, p);
                }
            }

            Append(points, new(x0 + r, y0));
            Append(points, new(x1 - r, y0));
            Corner(new(x1 - r, y0), new(x1 - r, y0 + r), 1, -1, new(x1, y0 + r));
            Append(points, new(x1, y1 - r));
            Corner(new(x1, y1 - r), new(x1 - r, y1 - r), 1, 1, new(x1 - r, y1));
            Append(points, new(x0 + r, y1));
            Corner(new(x0 + r, y1), new(x0 + r, y1 - r), -1, 1, new(x0, y1 - r));
            Append(points, new(x0, y0 + r));
            Corner(new(x0, y0 + r), new(x0 + r, y0 + r), -1, -1, new(x0 + r, y0));
            return Close(points);
        }

        private static BoardPoint Xy(SExpression? node) =>
            node is null ? default : new BoardPoint(node.GetValueAsDouble(0), node.GetValueAsDouble(1));

        /// <summary>Adds a vertex unless it repeats the last one, which an arc's start or end may.</summary>
        private static void Append(List<BoardPoint> points, BoardPoint p)
        {
            if (points.Count == 0 || points[^1].DistanceTo(p) > 1e-12)
            {
                points.Add(p);
            }
        }

        /// <summary>Drops a last vertex that repeats the first: the polygon closes on its own.</summary>
        private static List<BoardPoint> Close(List<BoardPoint> points)
        {
            if (points.Count > 1 && points[^1].DistanceTo(points[0]) <= 1e-12)
            {
                points.RemoveAt(points.Count - 1);
            }

            return points;
        }

        /// <summary>Joins pieces end to end into closed polygons, and returns what would not close.</summary>
        private static (List<List<BoardPoint>> Closed, List<List<BoardPoint>> Open) Chain(List<List<BoardPoint>> pieces, double tolerance)
        {
            var used = new bool[pieces.Count];
            var closed = new List<List<BoardPoint>>();
            var open = new List<List<BoardPoint>>();
            for (var start = 0; start < pieces.Count; start++)
            {
                if (used[start])
                {
                    continue;
                }

                used[start] = true;
                var chain = new List<BoardPoint>(pieces[start]);
                var closes = false;
                while (true)
                {
                    if (chain.Count > 2 && chain[^1].DistanceTo(chain[0]) <= tolerance)
                    {
                        chain.RemoveAt(chain.Count - 1);
                        closes = true;
                        break;
                    }

                    var next = -1;
                    var reversed = false;
                    for (var i = 0; i < pieces.Count && next < 0; i++)
                    {
                        if (used[i])
                        {
                            continue;
                        }

                        if (pieces[i][0].DistanceTo(chain[^1]) <= tolerance)
                        {
                            next = i;
                        }
                        else if (pieces[i][^1].DistanceTo(chain[^1]) <= tolerance)
                        {
                            next = i;
                            reversed = true;
                        }
                    }

                    if (next < 0)
                    {
                        break;
                    }

                    used[next] = true;
                    var piece = reversed ? Enumerable.Reverse(pieces[next]) : pieces[next];
                    chain.AddRange(piece.Skip(1));
                }

                (closes ? closed : open).Add(chain);
            }

            return (closed, open);
        }
    }
}
