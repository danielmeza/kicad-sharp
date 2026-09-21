using System;
using System.Collections.Generic;
using System.Linq;

namespace KiCadSharp.Geometry
{
    /// <summary>
    /// A core — one point, one segment or one polygon — swept by a disc of <see cref="Radius"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one shape the whole of a board's copper is made of, EXACTLY, not approximately:
    /// </para>
    /// <list type="table">
    ///   <item><term>a via, a circular pad</term><description>a point swept by its radius</description></item>
    ///   <item><term>a track, an oval pad</term><description>a segment swept by half its width</description></item>
    ///   <item><term>a round-rectangle pad</term><description>the rectangle shrunk by the corner radius, swept by it</description></item>
    ///   <item><term>a rectangle, a trapezoid, a zone fill</term><description>a polygon swept by nothing</description></item>
    /// </list>
    /// <para>
    /// And the distance between two of them is the distance between their cores less both radii,
    /// so the question every clearance check asks has an exact answer with no polygonisation of
    /// either side. Only a track ARC is approximated, and <see cref="CopperGeometry"/> says by how
    /// much.
    /// </para>
    /// </remarks>
    public sealed class RoundedShape
    {
        private readonly BoardPoint[] _core;

        private RoundedShape(BoardPoint[] core, double radius)
        {
            if (core.Length == 0)
            {
                throw new ArgumentException("A shape needs at least one point.", nameof(core));
            }

            ArgumentOutOfRangeException.ThrowIfNegative(radius);
            _core = core;
            Radius = radius;
            Bounds = BoardBox.Of(core).Inflate(radius);
        }

        /// <summary>
        /// Gets the core: one point, two (a segment), or three or more (a polygon, closed implicitly).
        /// </summary>
        public IReadOnlyList<BoardPoint> Core => _core;

        /// <summary>Gets how far the copper reaches past the core, in millimetres.</summary>
        public double Radius { get; }

        /// <summary>Gets the box the copper fills.</summary>
        public BoardBox Bounds { get; }

        /// <summary>A filled circle.</summary>
        /// <param name="centre">The centre.</param>
        /// <param name="radius">The radius.</param>
        /// <returns>The shape.</returns>
        public static RoundedShape Disc(BoardPoint centre, double radius) => new([centre], radius);

        /// <summary>A segment swept by a disc: a track, or an oval pad.</summary>
        /// <param name="start">One end of the core.</param>
        /// <param name="end">The other end.</param>
        /// <param name="radius">Half the width.</param>
        /// <returns>The shape.</returns>
        public static RoundedShape Capsule(BoardPoint start, BoardPoint end, double radius) =>
            start == end ? new([start], radius) : new([start, end], radius);

        /// <summary>A polygon, optionally swept by a disc.</summary>
        /// <param name="points">The vertices, in order. A repeated closing vertex is dropped.</param>
        /// <param name="radius">How far the copper reaches past the polygon; 0 for sharp corners.</param>
        /// <returns>The shape.</returns>
        public static RoundedShape Polygon(IEnumerable<BoardPoint> points, double radius = 0)
        {
            ArgumentNullException.ThrowIfNull(points);
            var list = points.ToList();
            if (list.Count > 1 && list[0] == list[^1])
            {
                list.RemoveAt(list.Count - 1);
            }

            return list.Count switch
            {
                0 => throw new ArgumentException("A polygon needs at least one point.", nameof(points)),
                1 => new([list[0]], radius),
                2 => Capsule(list[0], list[1], radius),
                _ => new([.. list], radius),
            };
        }

        /// <summary>
        /// The distance between the two shapes' copper, 0 when they touch or overlap.
        /// </summary>
        /// <param name="other">The other shape.</param>
        /// <returns>Millimetres.</returns>
        public double DistanceTo(RoundedShape other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return DistanceTo(other, double.MaxValue);
        }

        /// <summary>The distance, giving up early once it is known to be at least <paramref name="stop"/>.</summary>
        /// <param name="other">The other shape.</param>
        /// <param name="stop">A distance past which the exact answer does not matter.</param>
        /// <returns>The distance, or a value at least <paramref name="stop"/>.</returns>
        internal double DistanceTo(RoundedShape other, double stop)
        {
            var reach = Radius + other.Radius;
            var core = CoreDistance(_core, other._core, stop + reach);
            return Math.Max(0, core - reach);
        }

        /// <summary>Whether a point lies in the copper.</summary>
        /// <param name="p">The point.</param>
        /// <returns>True when it does.</returns>
        public bool Contains(BoardPoint p) => CoreDistance(_core, [p], Radius) <= Radius;

        private static double CoreDistance(BoardPoint[] a, BoardPoint[] b, double stop)
        {
            if (a.Length <= 2 && b.Length <= 2)
            {
                return Planar.SegmentSegment(a[0], a[^1], b[0], b[^1]);
            }

            if (a.Length >= 3 && b.Length <= 2)
            {
                return Planar.PolygonSegment(a, b[0], b[^1], stop);
            }

            if (b.Length >= 3 && a.Length <= 2)
            {
                return Planar.PolygonSegment(b, a[0], a[^1], stop);
            }

            return Planar.PolygonPolygon(a, b, stop);
        }
    }

    /// <summary>
    /// A piece of copper made of one or more <see cref="RoundedShape"/>s: a custom pad, a track arc
    /// approximated by a chain of capsules, or a single primitive.
    /// </summary>
    public sealed class CopperShape
    {
        private readonly RoundedShape[] _parts;

        /// <summary>Creates a shape from its parts.</summary>
        /// <param name="parts">At least one.</param>
        public CopperShape(IEnumerable<RoundedShape> parts)
        {
            ArgumentNullException.ThrowIfNull(parts);
            _parts = parts.ToArray();
            if (_parts.Length == 0)
            {
                throw new ArgumentException("Copper needs at least one part.", nameof(parts));
            }

            var bounds = _parts[0].Bounds;
            for (var i = 1; i < _parts.Length; i++)
            {
                bounds = bounds.Union(_parts[i].Bounds);
            }

            Bounds = bounds;
        }

        /// <summary>Creates a shape of one part.</summary>
        /// <param name="part">The part.</param>
        public CopperShape(RoundedShape part)
            : this([part])
        {
        }

        /// <summary>Gets the parts.</summary>
        public IReadOnlyList<RoundedShape> Parts => _parts;

        /// <summary>Gets the box the copper fills.</summary>
        public BoardBox Bounds { get; }

        /// <summary>The distance between the two pieces of copper, 0 when they touch or overlap.</summary>
        /// <param name="other">The other piece.</param>
        /// <returns>Millimetres.</returns>
        public double DistanceTo(CopperShape other)
        {
            ArgumentNullException.ThrowIfNull(other);
            var best = double.MaxValue;
            foreach (var a in _parts)
            {
                foreach (var b in other._parts)
                {
                    if (a.Bounds.DistanceTo(b.Bounds) >= best)
                    {
                        continue;
                    }

                    best = Math.Min(best, a.DistanceTo(b, best));
                    if (best == 0)
                    {
                        return 0;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Whether the two keep at least <paramref name="clearance"/> between them — the question a
        /// clearance rule asks.
        /// </summary>
        /// <param name="other">The other piece.</param>
        /// <param name="clearance">Millimetres.</param>
        /// <returns>True when the gap is at least the clearance.</returns>
        public bool Clears(CopperShape other, double clearance)
        {
            ArgumentNullException.ThrowIfNull(other);
            return Bounds.DistanceTo(other.Bounds) >= clearance || DistanceTo(other) >= clearance;
        }

        /// <summary>Whether a point lies in the copper.</summary>
        /// <param name="p">The point.</param>
        /// <returns>True when it does.</returns>
        public bool Contains(BoardPoint p) => Bounds.Contains(p) && _parts.Any(part => part.Contains(p));
    }

    /// <summary>The plane geometry under <see cref="RoundedShape"/>. Every routine here is exact.</summary>
    internal static class Planar
    {
        internal static double PointSegment(BoardPoint p, BoardPoint a, BoardPoint b)
        {
            var ab = b - a;
            var lengthSquared = (ab.X * ab.X) + (ab.Y * ab.Y);
            if (lengthSquared == 0)
            {
                return p.DistanceTo(a);
            }

            var t = Math.Clamp((((p.X - a.X) * ab.X) + ((p.Y - a.Y) * ab.Y)) / lengthSquared, 0, 1);
            return p.DistanceTo(a + (ab * t));
        }

        internal static double SegmentSegment(BoardPoint a, BoardPoint b, BoardPoint c, BoardPoint d)
        {
            if (Intersect(a, b, c, d))
            {
                return 0;
            }

            return Math.Min(
                Math.Min(PointSegment(a, c, d), PointSegment(b, c, d)),
                Math.Min(PointSegment(c, a, b), PointSegment(d, a, b)));
        }

        /// <summary>Even-odd containment, which is right for KiCad's fractured fill polygons too.</summary>
        internal static bool Inside(BoardPoint p, BoardPoint[] polygon)
        {
            var inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];
                if ((pi.Y > p.Y) != (pj.Y > p.Y)
                    && p.X < ((pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y)) + pi.X)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        internal static double PolygonSegment(BoardPoint[] polygon, BoardPoint a, BoardPoint b, double stop)
        {
            if (Inside(a, polygon) || Inside(b, polygon))
            {
                return 0;
            }

            var segment = BoardBox.Of([a, b]);
            var best = double.MaxValue;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                var edge = new BoardBox(
                    Math.Min(polygon[i].X, polygon[j].X), Math.Min(polygon[i].Y, polygon[j].Y),
                    Math.Max(polygon[i].X, polygon[j].X), Math.Max(polygon[i].Y, polygon[j].Y));
                if (edge.DistanceTo(segment) >= Math.Min(best, stop))
                {
                    continue;
                }

                best = Math.Min(best, SegmentSegment(polygon[j], polygon[i], a, b));
                if (best == 0)
                {
                    return 0;
                }
            }

            return best;
        }

        internal static double PolygonPolygon(BoardPoint[] p, BoardPoint[] q, double stop)
        {
            if (Inside(q[0], p) || Inside(p[0], q))
            {
                return 0;
            }

            var qBox = BoardBox.Of(q);
            var best = double.MaxValue;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
            {
                var edge = new BoardBox(
                    Math.Min(p[i].X, p[j].X), Math.Min(p[i].Y, p[j].Y),
                    Math.Max(p[i].X, p[j].X), Math.Max(p[i].Y, p[j].Y));
                if (edge.DistanceTo(qBox) >= Math.Min(best, stop))
                {
                    continue;
                }

                best = Math.Min(best, PolygonSegment(q, p[j], p[i], Math.Min(best, stop)));
                if (best == 0)
                {
                    return 0;
                }
            }

            return best;
        }

        /// <summary>Whether two closed segments share a point, collinear overlap included.</summary>
        private static bool Intersect(BoardPoint a, BoardPoint b, BoardPoint c, BoardPoint d)
        {
            var d1 = Cross(c, d, a);
            var d2 = Cross(c, d, b);
            var d3 = Cross(a, b, c);
            var d4 = Cross(a, b, d);

            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            {
                return true;
            }

            return (d1 == 0 && OnSegment(c, d, a))
                || (d2 == 0 && OnSegment(c, d, b))
                || (d3 == 0 && OnSegment(a, b, c))
                || (d4 == 0 && OnSegment(a, b, d));
        }

        private static double Cross(BoardPoint o, BoardPoint a, BoardPoint b) =>
            ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));

        private static bool OnSegment(BoardPoint a, BoardPoint b, BoardPoint p) =>
            p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X)
            && p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
    }
}
