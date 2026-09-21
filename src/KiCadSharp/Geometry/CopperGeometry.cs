using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Geometry
{
    /// <summary>
    /// The copper each item on a board occupies, in board coordinates, as a <see cref="CopperShape"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Where things sit is a composition, and the file stores the parts.</b> A pad's
    /// <c>(at x y angle)</c> holds its position in the FOOTPRINT's frame — unrotated — and its
    /// orientation ON THE BOARD: the footprint's angle is already in it. So the centre is the
    /// footprint's position plus the pad's offset turned by the footprint's angle, and the shape is
    /// turned by the pad's own angle alone. A copper offset (<c>(drill … (offset x y))</c>) moves
    /// the copper away from the hole in the pad's frame. A footprint on the back is stored already
    /// flipped, so the same composition places it.
    /// </para>
    /// <para>
    /// Every shape is exact except a track arc, which is a chain of capsules whose radius is grown by
    /// the chord error, so the chain always COVERS the arc — a distance measured from it can be low
    /// by at most <c>maxError</c>, never high. A clearance check built on this can only err on the
    /// safe side. The same holds for the rounded corners of a chamfered pad.
    /// </para>
    /// <para>
    /// A pad whose padstack differs per layer (KiCad 9's <c>(padstack (mode custom) …)</c>) is
    /// described by its main shape on every layer.
    /// </para>
    /// </remarks>
    public static class CopperGeometry
    {
        /// <summary>The default chord error for arcs and rounded corners: half a micron.</summary>
        public const double DefaultMaxError = 0.0005;

        /// <summary>A point in a footprint's own frame, placed on the board.</summary>
        /// <param name="footprint">The footprint.</param>
        /// <param name="local">The point in the footprint's frame.</param>
        /// <returns>The point on the board.</returns>
        public static BoardPoint ToBoard(KiCadFootprint footprint, BoardPoint local)
        {
            ArgumentNullException.ThrowIfNull(footprint);
            var at = footprint.Position;
            return new BoardPoint(at.X, at.Y) + local.Rotate(at.Rotation);
        }

        /// <summary>Where a pad's hole — its anchor — sits on the board.</summary>
        /// <param name="footprint">The footprint the pad belongs to.</param>
        /// <param name="pad">The pad.</param>
        /// <returns>The point.</returns>
        public static BoardPoint PadPosition(KiCadFootprint footprint, KiCadPad pad)
        {
            ArgumentNullException.ThrowIfNull(pad);
            return ToBoard(footprint, new BoardPoint(pad.Position.X, pad.Position.Y));
        }

        /// <summary>The copper of one pad.</summary>
        /// <param name="footprint">The footprint the pad belongs to.</param>
        /// <param name="pad">The pad.</param>
        /// <param name="maxError">Chord error for rounded corners, millimetres.</param>
        /// <returns>The copper.</returns>
        public static CopperShape Pad(KiCadFootprint footprint, KiCadPad pad, double maxError = DefaultMaxError)
        {
            ArgumentNullException.ThrowIfNull(pad);
            var angle = pad.Position.Rotation;
            var centre = PadPosition(footprint, pad);
            if (pad.Drill?.Offset is { } offset)
            {
                centre += new BoardPoint(offset.X, offset.Y).Rotate(angle);
            }

            return new CopperShape(PadLocal(pad, maxError).Select(part => Place(part, centre, angle)));
        }

        /// <summary>The hole of a pad, or <see langword="null"/> for a surface-mount pad.</summary>
        /// <param name="footprint">The footprint the pad belongs to.</param>
        /// <param name="pad">The pad.</param>
        /// <returns>The hole's extent.</returns>
        public static CopperShape? Hole(KiCadFootprint footprint, KiCadPad pad)
        {
            ArgumentNullException.ThrowIfNull(pad);
            if (pad.Drill is not { } drill || drill.Width <= 0)
            {
                return null;
            }

            var centre = PadPosition(footprint, pad);
            if (!drill.IsOval || drill.Width == drill.Height)
            {
                return new CopperShape(RoundedShape.Disc(centre, drill.Width / 2));
            }

            var local = Oval(drill.Width, drill.Height);
            return new CopperShape(Place(local, centre, pad.Position.Rotation));
        }

        /// <summary>The copper of a straight track.</summary>
        /// <param name="segment">The segment.</param>
        /// <returns>The copper.</returns>
        public static CopperShape Segment(KiCadTrackSegment segment)
        {
            ArgumentNullException.ThrowIfNull(segment);
            return new CopperShape(RoundedShape.Capsule(Point(segment.Start), Point(segment.End), segment.Width / 2));
        }

        /// <summary>The copper of a track arc, as capsules that cover it.</summary>
        /// <param name="arc">The arc.</param>
        /// <param name="maxError">Chord error, millimetres.</param>
        /// <returns>The copper.</returns>
        public static CopperShape Arc(KiCadTrackArc arc, double maxError = DefaultMaxError)
        {
            ArgumentNullException.ThrowIfNull(arc);
            return new CopperShape(ArcBand(Point(arc.Start), Point(arc.Mid), Point(arc.End), arc.Width / 2, maxError));
        }

        /// <summary>The copper of a via.</summary>
        /// <param name="via">The via.</param>
        /// <returns>The copper.</returns>
        public static CopperShape Via(KiCadVia via)
        {
            ArgumentNullException.ThrowIfNull(via);
            return new CopperShape(RoundedShape.Disc(Point(via.Position), via.Size / 2));
        }

        /// <summary>The copper of one polygon a zone fill produced.</summary>
        /// <param name="polygon">The filled polygon.</param>
        /// <returns>The copper.</returns>
        public static CopperShape Fill(KiCadZoneFilledPolygon polygon)
        {
            ArgumentNullException.ThrowIfNull(polygon);
            return new CopperShape(RoundedShape.Polygon(polygon.Points.Select(Point)));
        }

        /// <summary>
        /// Points ON a circular arc from <paramref name="start"/> through <paramref name="mid"/> to
        /// <paramref name="end"/>, close enough that no chord strays more than
        /// <paramref name="maxError"/> from it.
        /// </summary>
        /// <param name="start">Where the arc starts.</param>
        /// <param name="mid">Any point on it between the ends.</param>
        /// <param name="end">Where it ends.</param>
        /// <param name="maxError">The largest chord error, millimetres.</param>
        /// <returns>The points, both ends included. A degenerate arc gives its ends.</returns>
        public static IReadOnlyList<BoardPoint> ArcPoints(BoardPoint start, BoardPoint mid, BoardPoint end, double maxError = DefaultMaxError)
        {
            if (!Circle(start, mid, end, out var centre, out var radius))
            {
                return [start, end];
            }

            var a0 = Math.Atan2(start.Y - centre.Y, start.X - centre.X);
            var am = Math.Atan2(mid.Y - centre.Y, mid.X - centre.X);
            var a1 = Math.Atan2(end.Y - centre.Y, end.X - centre.X);
            var sweep = Sweep(a0, am, a1);
            var steps = Steps(radius, Math.Abs(sweep), maxError);
            var points = new List<BoardPoint>(steps + 1) { start };
            for (var i = 1; i < steps; i++)
            {
                var a = a0 + (sweep * i / steps);
                points.Add(new BoardPoint(centre.X + (radius * Math.Cos(a)), centre.Y + (radius * Math.Sin(a))));
            }

            points.Add(end);
            return points;
        }

        /// <summary>The copper layers a pad or via declaration covers, in the board's physical order.</summary>
        /// <param name="declared">The names as written, wildcards included: <c>*.Cu</c>, <c>F&amp;B.Cu</c>.</param>
        /// <param name="copperLayers">The board's copper layers, top first.</param>
        /// <returns>The layers.</returns>
        public static IReadOnlyList<string> CopperLayersOf(IEnumerable<string> declared, IReadOnlyList<string> copperLayers)
        {
            ArgumentNullException.ThrowIfNull(declared);
            ArgumentNullException.ThrowIfNull(copperLayers);
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in declared)
            {
                if (string.Equals(name, "*.Cu", StringComparison.Ordinal))
                {
                    wanted.UnionWith(copperLayers);
                }
                else if (string.Equals(name, "F&B.Cu", StringComparison.Ordinal))
                {
                    wanted.Add(KiCadLayerNames.FCu);
                    wanted.Add(KiCadLayerNames.BCu);
                }
                else
                {
                    wanted.Add(name);
                }
            }

            return copperLayers.Where(wanted.Contains).ToArray();
        }

        /// <summary>A board's copper layers in physical order: <c>F.Cu</c>, the inner layers by number, <c>B.Cu</c>.</summary>
        /// <param name="board">The board.</param>
        /// <returns>The names.</returns>
        /// <remarks>
        /// Not the layer table's order: KiCad 9 numbers <c>B.Cu</c> 2 and <c>In1.Cu</c> 4, so sorting by
        /// ordinal puts the bottom layer second.
        /// </remarks>
        public static IReadOnlyList<string> CopperLayers(KiCadBoard board)
        {
            ArgumentNullException.ThrowIfNull(board);
            return board.Layers
                .Where(l => l.Name.EndsWith(".Cu", StringComparison.Ordinal) && l.IsCopper)
                .Select(l => l.Name)
                .OrderBy(PhysicalRank)
                .ToArray();
        }

        internal static int PhysicalRank(string layer)
        {
            if (string.Equals(layer, KiCadLayerNames.FCu, StringComparison.Ordinal))
            {
                return 0;
            }

            if (string.Equals(layer, KiCadLayerNames.BCu, StringComparison.Ordinal))
            {
                return int.MaxValue;
            }

            return layer.StartsWith("In", StringComparison.Ordinal)
                && int.TryParse(layer.AsSpan(2, layer.Length - 5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n
                : int.MaxValue - 1;
        }

        internal static BoardPoint Point(KiCadPosition p) => new(p.X, p.Y);

        /// <summary>
        /// Whether a pad has corners cut off: a <c>(chamfer …)</c> naming a corner and a non-zero
        /// <c>(chamfer_ratio …)</c>.
        /// </summary>
        /// <remarks>
        /// <b>Not the shape token.</b> MEASURED: pcbnew 10.0.6 writes a chamfered pad as
        /// <c>roundrect</c> with the two chamfer forms beside it — it wrote <c>chamfered_rect</c> for
        /// none of the 12 chamfered pads it saved into <c>pad-shapes.kicad_pcb</c> — and its uncut
        /// corners keep the round-rectangle radius. Read as a plain round rectangle, such a pad
        /// measured up to 116 µm closer to its neighbours than KiCad says it is.
        /// </remarks>
        /// <param name="pad">The pad.</param>
        /// <returns>True when it does.</returns>
        public static bool IsChamfered(KiCadPad pad)
        {
            ArgumentNullException.ThrowIfNull(pad);
            var ratio = pad.Node.GetChild(KiCadTokens.Footprint.ChamferRatio)?.GetValueAsDouble(0) ?? 0;
            var corners = pad.Node.GetChild(KiCadTokens.Footprint.Chamfer)?.Values.Count ?? 0;
            return (pad.Shape is "roundrect" or "chamfered_rect") && ratio > 0 && corners > 0;
        }

        /// <summary>The pad's copper in its own frame: centred on the copper centre, unrotated.</summary>
        internal static IEnumerable<RoundedShape> PadLocal(KiCadPad pad, double maxError)
        {
            var w = pad.Size.Width;
            var h = pad.Size.Height;
            if (IsChamfered(pad))
            {
                return [RoundedShape.Polygon(ChamferedOutline(pad, w, h, maxError))];
            }

            switch (pad.Shape)
            {
                case "circle":
                    return [RoundedShape.Disc(default, w / 2)];
                case "oval":
                    return [Oval(w, h)];
                case "roundrect":
                    return [RoundRect(w, h, RoundRadius(pad, w, h))];
                case "chamfered_rect":
                    // Named chamfered but cutting no corner: what is left is the rounding, if any.
                    return [RoundRect(w, h, pad.Node.GetChild(KiCadTokens.Footprint.RoundRectRatio) is null ? 0 : RoundRadius(pad, w, h))];
                case "trapezoid":
                    return [RoundedShape.Polygon(Trapezoid(pad, w, h))];
                case "custom":
                    return Custom(pad, w, h, maxError);
                default:
                    return [Rect(w, h)];
            }
        }

        internal static double RoundRadius(KiCadPad pad, double w, double h)
        {
            var ratio = pad.Node.GetChild(KiCadTokens.Footprint.RoundRectRatio)?.GetValueAsDouble(0) ?? 0.25;
            return Math.Min(ratio, 0.5) * Math.Min(w, h);
        }

        internal static RoundedShape Rect(double w, double h) =>
            RoundedShape.Polygon([new(-w / 2, -h / 2), new(w / 2, -h / 2), new(w / 2, h / 2), new(-w / 2, h / 2)]);

        internal static RoundedShape Oval(double w, double h)
        {
            if (w >= h)
            {
                var d = (w - h) / 2;
                return RoundedShape.Capsule(new(-d, 0), new(d, 0), h / 2);
            }

            var e = (h - w) / 2;
            return RoundedShape.Capsule(new(0, -e), new(0, e), w / 2);
        }

        internal static RoundedShape RoundRect(double w, double h, double r)
        {
            var hx = Math.Max(0, (w / 2) - r);
            var hy = Math.Max(0, (h / 2) - r);
            if (hx == 0 && hy == 0)
            {
                return RoundedShape.Disc(default, r);
            }

            if (hx == 0)
            {
                return RoundedShape.Capsule(new(0, -hy), new(0, hy), r);
            }

            if (hy == 0)
            {
                return RoundedShape.Capsule(new(-hx, 0), new(hx, 0), r);
            }

            return RoundedShape.Polygon([new(-hx, -hy), new(hx, -hy), new(hx, hy), new(-hx, hy)], r);
        }

        /// <summary>
        /// A trapezoid's corners. <c>(rect_delta dx dy)</c> widens one side and narrows the other: dx
        /// acts on the height across X, dy on the width across Y.
        /// </summary>
        internal static BoardPoint[] Trapezoid(KiCadPad pad, double w, double h)
        {
            var delta = pad.Node.GetChild(KiCadTokens.Footprint.RectDelta);
            var ddx = (delta?.GetValueAsDouble(0) ?? 0) / 2;
            var ddy = (delta?.GetValueAsDouble(1) ?? 0) / 2;
            var dx = w / 2;
            var dy = h / 2;
            return
            [
                new(-dx - ddy, dy + ddx),
                new(dx + ddy, dy - ddx),
                new(dx - ddy, -dy + ddx),
                new(-dx + ddy, -dy - ddx),
            ];
        }

        /// <summary>A chamfered rectangle's outline, its uncut corners rounded when the pad asks.</summary>
        internal static IEnumerable<BoardPoint> ChamferedOutline(KiCadPad pad, double w, double h, double maxError)
        {
            var ratio = pad.Node.GetChild(KiCadTokens.Footprint.ChamferRatio)?.GetValueAsDouble(0) ?? 0.2;
            var chamfer = Math.Min(ratio, 0.5) * Math.Min(w, h);
            var cut = new HashSet<string>(
                (IEnumerable<string>?)pad.Node.GetChild(KiCadTokens.Footprint.Chamfer)?.Values ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            var radius = pad.Node.GetChild(KiCadTokens.Footprint.RoundRectRatio) is null ? 0 : RoundRadius(pad, w, h);
            return CornerOutline(w, h, radius, chamfer, cut, maxError);
        }

        /// <summary>
        /// A rectangle's outline with some corners cut by <paramref name="chamfer"/> and the rest
        /// rounded by <paramref name="radius"/>, as a polygon that covers the true shape.
        /// </summary>
        internal static IEnumerable<BoardPoint> CornerOutline(
            double w, double h, double radius, double chamfer, IReadOnlySet<string> cut, double maxError)
        {
            // Corners clockwise on screen from top-left, with the direction each edge leaves them.
            (string Name, BoardPoint Corner, BoardPoint In, BoardPoint Out)[] corners =
            [
                (KiCadTokens.Footprint.ChamferTopLeft, new(-w / 2, -h / 2), new(0, 1), new(1, 0)),
                (KiCadTokens.Footprint.ChamferTopRight, new(w / 2, -h / 2), new(-1, 0), new(0, 1)),
                (KiCadTokens.Footprint.ChamferBottomRight, new(w / 2, h / 2), new(0, -1), new(-1, 0)),
                (KiCadTokens.Footprint.ChamferBottomLeft, new(-w / 2, h / 2), new(1, 0), new(0, -1)),
            ];

            var points = new List<BoardPoint>();
            foreach (var (name, corner, into, outOf) in corners)
            {
                if (cut.Contains(name) && chamfer > 0)
                {
                    points.Add(corner + (into * chamfer));
                    points.Add(corner + (outOf * chamfer));
                }
                else if (radius > 0)
                {
                    points.AddRange(CoveringCorner(corner + ((into + outOf) * radius), radius, into, outOf, maxError));
                }
                else
                {
                    points.Add(corner);
                }
            }

            return points;
        }

        /// <summary>
        /// A rounded corner as a polygon that COVERS the arc: points on a circle grown until its
        /// chords clear the true radius.
        /// </summary>
        private static IEnumerable<BoardPoint> CoveringCorner(BoardPoint centre, double radius, BoardPoint into, BoardPoint outOf, double maxError)
        {
            // The corner arc runs from the tangent on the incoming edge to the one on the outgoing
            // edge. The incoming edge lies back along `into` from the corner, so its tangent point
            // is the centre less `outOf` * r; the outgoing one is the centre less `into` * r.
            var a0 = Math.Atan2(-outOf.Y, -outOf.X);
            var a1 = Math.Atan2(-into.Y, -into.X);
            var sweep = Math.IEEERemainder(a1 - a0, 2 * Math.PI);
            var grown = radius + maxError;
            var steps = Steps(grown, Math.Abs(sweep), maxError);
            for (var i = 0; i <= steps; i++)
            {
                var a = a0 + (sweep * i / steps);
                yield return new BoardPoint(centre.X + (grown * Math.Cos(a)), centre.Y + (grown * Math.Sin(a)));
            }
        }

        private static IEnumerable<RoundedShape> Custom(KiCadPad pad, double w, double h, double maxError)
        {
            var anchor = pad.Node.GetChild(KiCadTokens.Footprint.Options)?.GetChild(KiCadTokens.Footprint.Anchor)?.GetValue(0);
            var parts = new List<RoundedShape>
            {
                string.Equals(anchor, "rect", StringComparison.Ordinal) ? Rect(w, h) : RoundedShape.Disc(default, w / 2),
            };

            foreach (var primitive in pad.Node.GetChild(KiCadTokens.Footprint.Primitives)?.Children ?? [])
            {
                parts.AddRange(Primitive(primitive, maxError));
            }

            return parts;
        }

        private static IEnumerable<RoundedShape> Primitive(SExpression node, double maxError)
        {
            var stroke = (node.GetChild(KiCadTokens.Common.Width)?.GetValueAsDouble(0)
                          ?? node.GetChild(KiCadTokens.Common.Stroke)?.GetChild(KiCadTokens.Common.Width)?.GetValueAsDouble(0)
                          ?? 0) / 2;
            var filled = node.GetChild(KiCadTokens.Common.Fill)?.GetValue(0) is { } fill
                && !string.Equals(fill, KiCadTokens.Common.No, StringComparison.Ordinal)
                && !string.Equals(fill, "none", StringComparison.Ordinal);

            switch (node.Token)
            {
                case KiCadTokens.Board.GrLine:
                    return [RoundedShape.Capsule(At(node, KiCadTokens.Common.Start), At(node, KiCadTokens.Common.End), stroke)];

                case KiCadTokens.Board.GrPoly:
                {
                    var pts = (node.GetChild(KiCadTokens.Common.Pts)?.GetChildren(KiCadTokens.Common.Xy) ?? [])
                        .Select(xy => new BoardPoint(xy.GetValueAsDouble(0), xy.GetValueAsDouble(1)))
                        .ToList();
                    return pts.Count == 0 ? [] : filled || pts.Count < 3 ? [RoundedShape.Polygon(pts, stroke)] : Ring(pts, stroke);
                }

                case KiCadTokens.Board.GrRect:
                {
                    var s = At(node, KiCadTokens.Common.Start);
                    var e = At(node, KiCadTokens.Common.End);
                    BoardPoint[] pts = [s, new(e.X, s.Y), e, new(s.X, e.Y)];
                    return filled ? [RoundedShape.Polygon(pts, stroke)] : Ring(pts, stroke);
                }

                case KiCadTokens.Board.GrCircle:
                {
                    var c = At(node, KiCadTokens.Common.Center);
                    var r = c.DistanceTo(At(node, KiCadTokens.Common.End));
                    if (filled)
                    {
                        return [RoundedShape.Disc(c, r + stroke)];
                    }

                    return Ring(ArcPoints(new(c.X + r, c.Y), new(c.X, c.Y + r), new(c.X - r, c.Y), maxError)
                        .Concat(ArcPoints(new(c.X - r, c.Y), new(c.X, c.Y - r), new(c.X + r, c.Y), maxError).Skip(1))
                        .ToList(), stroke + maxError);
                }

                case KiCadTokens.Board.GrArc:
                    return ArcBand(At(node, KiCadTokens.Common.Start), At(node, KiCadTokens.Common.Mid), At(node, KiCadTokens.Common.End), stroke, maxError);

                default:
                    return [];
            }
        }

        private static BoardPoint At(SExpression node, string token) =>
            node.GetChild(token) is { } at ? new BoardPoint(at.GetValueAsDouble(0), at.GetValueAsDouble(1)) : default;

        /// <summary>The outline of a closed polygon drawn with a pen, as capsules edge by edge.</summary>
        private static IEnumerable<RoundedShape> Ring(IReadOnlyList<BoardPoint> points, double radius)
        {
            for (var i = 0; i < points.Count; i++)
            {
                yield return RoundedShape.Capsule(points[i], points[(i + 1) % points.Count], radius);
            }
        }

        /// <summary>An arc drawn with a pen, as capsules that cover it.</summary>
        internal static IEnumerable<RoundedShape> ArcBand(BoardPoint start, BoardPoint mid, BoardPoint end, double halfWidth, double maxError)
        {
            var points = ArcPoints(start, mid, end, maxError);
            for (var i = 0; i + 1 < points.Count; i++)
            {
                yield return RoundedShape.Capsule(points[i], points[i + 1], halfWidth + maxError);
            }
        }

        private static RoundedShape Place(RoundedShape local, BoardPoint centre, double angle) =>
            RoundedShape.Polygon(local.Core.Select(p => centre + p.Rotate(angle)), local.Radius);

        private static bool Circle(BoardPoint a, BoardPoint b, BoardPoint c, out BoardPoint centre, out double radius)
        {
            var d = 2 * ((a.X * (b.Y - c.Y)) + (b.X * (c.Y - a.Y)) + (c.X * (a.Y - b.Y)));
            if (Math.Abs(d) < 1e-12)
            {
                centre = default;
                radius = 0;
                return false;
            }

            var a2 = (a.X * a.X) + (a.Y * a.Y);
            var b2 = (b.X * b.X) + (b.Y * b.Y);
            var c2 = (c.X * c.X) + (c.Y * c.Y);
            centre = new BoardPoint(
                ((a2 * (b.Y - c.Y)) + (b2 * (c.Y - a.Y)) + (c2 * (a.Y - b.Y))) / d,
                ((a2 * (c.X - b.X)) + (b2 * (a.X - c.X)) + (c2 * (b.X - a.X))) / d);
            radius = centre.DistanceTo(a);
            return true;
        }

        /// <summary>The signed sweep from a0 to a1 that passes through am.</summary>
        private static double Sweep(double a0, double am, double a1)
        {
            var toMid = Normalise(am - a0);
            var toEnd = Normalise(a1 - a0);
            if (toEnd == 0)
            {
                return 2 * Math.PI;
            }

            // Counter-clockwise (in atan2's sense) reaches the end after the mid, or the arc runs the other way.
            return toMid < toEnd ? toEnd : toEnd - (2 * Math.PI);
        }

        private static double Normalise(double a)
        {
            var twoPi = 2 * Math.PI;
            a %= twoPi;
            return a < 0 ? a + twoPi : a;
        }

        private static int Steps(double radius, double sweep, double maxError)
        {
            if (radius <= maxError)
            {
                return Math.Max(1, (int)Math.Ceiling(sweep / (Math.PI / 2)));
            }

            var step = 2 * Math.Acos(1 - (maxError / radius));
            return Math.Max(1, (int)Math.Ceiling(sweep / step));
        }
    }
}
