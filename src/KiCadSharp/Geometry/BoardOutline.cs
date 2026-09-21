using System;
using System.Collections.Generic;
using System.Linq;

using KiCadSharp.Documents;

namespace KiCadSharp.Geometry
{
    /// <summary>
    /// A board's outline, from everything drawn on <c>Edge.Cuts</c>: the closed shapes it forms,
    /// which of them are the board and which are cut out of it, and every edge as copper sees it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lines and arcs are chained end to end within <see cref="DefaultChainTolerance"/>; circles,
    /// rectangles and polygons are closed already. A closed shape inside an odd number of others is a
    /// cutout — a mounting hole, a slot — and inside an even number, board. Footprint graphics on
    /// <c>Edge.Cuts</c> count, placed where the footprint puts them.
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

        /// <summary>Gets how many chains of lines and arcs did not close — an outline drawn with a gap.</summary>
        public int OpenChains { get; }

        /// <summary>Gets the box around the whole outline, or <see langword="null"/> when nothing is drawn.</summary>
        public BoardBox? Bounds => _edges?.Bounds;

        /// <summary>Reads the outline off a board.</summary>
        /// <param name="board">The board.</param>
        /// <param name="maxError">Chord error for arcs and circles, millimetres.</param>
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

            foreach (var circle in board.GraphicCircles.Where(OnEdge))
            {
                closed.Add(CirclePoints(CopperGeometry.Point(circle.Center), CopperGeometry.Point(circle.End), maxError));
            }

            foreach (var rect in board.GraphicRectangles.Where(OnEdge))
            {
                var s = CopperGeometry.Point(rect.Start);
                var e = CopperGeometry.Point(rect.End);
                closed.Add([s, new(e.X, s.Y), e, new(s.X, e.Y)]);
            }

            foreach (var poly in board.GraphicPolygons.Where(OnEdge))
            {
                closed.Add([.. poly.Points.Select(CopperGeometry.Point)]);
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

                foreach (var circle in footprint.Circles.Where(c => c.Layer == KiCadLayerNames.EdgeCuts))
                {
                    closed.Add(CirclePoints(Place(circle.Center), Place(circle.End), maxError));
                }

                foreach (var rect in footprint.Rectangles.Where(r => r.Layer == KiCadLayerNames.EdgeCuts))
                {
                    var s = CopperGeometry.Point(rect.Start);
                    var e = CopperGeometry.Point(rect.End);
                    closed.Add([Place(rect.Start), CopperGeometry.ToBoard(footprint, new(e.X, s.Y)), Place(rect.End), CopperGeometry.ToBoard(footprint, new(s.X, e.Y))]);
                }

                foreach (var poly in footprint.Polygons.Where(p => p.Layer == KiCadLayerNames.EdgeCuts))
                {
                    closed.Add([.. poly.Points.Select(Place)]);
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
