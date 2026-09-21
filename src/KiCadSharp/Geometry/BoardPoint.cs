using System;
using System.Collections.Generic;

namespace KiCadSharp.Geometry
{
    /// <summary>
    /// A point on a board, in millimetres, in the file's own frame: X to the right, Y DOWN.
    /// </summary>
    /// <param name="X">Millimetres.</param>
    /// <param name="Y">Millimetres, increasing downwards as KiCad writes them.</param>
    public readonly record struct BoardPoint(double X, double Y)
    {
        /// <summary>The sum of two points.</summary>
        /// <param name="a">The first.</param>
        /// <param name="b">The second.</param>
        /// <returns>The sum.</returns>
        public static BoardPoint operator +(BoardPoint a, BoardPoint b) => new(a.X + b.X, a.Y + b.Y);

        /// <summary>The difference of two points.</summary>
        /// <param name="a">The first.</param>
        /// <param name="b">The second.</param>
        /// <returns>The difference.</returns>
        public static BoardPoint operator -(BoardPoint a, BoardPoint b) => new(a.X - b.X, a.Y - b.Y);

        /// <summary>A point scaled about the origin.</summary>
        /// <param name="a">The point.</param>
        /// <param name="k">The factor.</param>
        /// <returns>The scaled point.</returns>
        public static BoardPoint operator *(BoardPoint a, double k) => new(a.X * k, a.Y * k);

        /// <summary>Gets the distance from the origin.</summary>
        public double Length => Math.Sqrt((X * X) + (Y * Y));

        /// <summary>The distance to another point.</summary>
        /// <param name="other">The other point.</param>
        /// <returns>Millimetres.</returns>
        public double DistanceTo(BoardPoint other) => (this - other).Length;

        /// <summary>
        /// This point turned about the origin by <paramref name="degrees"/>, the way KiCad's
        /// <c>(at x y angle)</c> turns it.
        /// </summary>
        /// <remarks>
        /// Positive is counter-clockwise AS DISPLAYED, which on a Y-down frame makes <c>(1, 0)</c>
        /// at 90 degrees <c>(0, -1)</c>. A quarter turn is exact: KiCad's own angles are overwhelmingly
        /// multiples of 90, and <c>Math.Cos(Math.PI / 2)</c> is 6e-17, not 0.
        /// </remarks>
        /// <param name="degrees">The angle.</param>
        /// <returns>The turned point.</returns>
        public BoardPoint Rotate(double degrees)
        {
            var turn = ((degrees % 360) + 360) % 360;
            if (turn == 0)
            {
                return this;
            }

            if (turn == 90)
            {
                return new BoardPoint(Y, -X);
            }

            if (turn == 180)
            {
                return new BoardPoint(-X, -Y);
            }

            if (turn == 270)
            {
                return new BoardPoint(-Y, X);
            }

            var radians = degrees * Math.PI / 180.0;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            return new BoardPoint((X * cos) + (Y * sin), (-X * sin) + (Y * cos));
        }
    }

    /// <summary>An axis-aligned box on a board, in millimetres.</summary>
    /// <param name="MinX">Left edge.</param>
    /// <param name="MinY">Top edge (Y is down).</param>
    /// <param name="MaxX">Right edge.</param>
    /// <param name="MaxY">Bottom edge.</param>
    public readonly record struct BoardBox(double MinX, double MinY, double MaxX, double MaxY)
    {
        /// <summary>Gets the width.</summary>
        public double Width => MaxX - MinX;

        /// <summary>Gets the height.</summary>
        public double Height => MaxY - MinY;

        /// <summary>Gets the centre.</summary>
        public BoardPoint Centre => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);

        /// <summary>The smallest box holding every point.</summary>
        /// <param name="points">The points; at least one.</param>
        /// <returns>The box.</returns>
        public static BoardBox Of(IEnumerable<BoardPoint> points)
        {
            ArgumentNullException.ThrowIfNull(points);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in points)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            if (minX > maxX)
            {
                throw new ArgumentException("A box needs at least one point.", nameof(points));
            }

            return new BoardBox(minX, minY, maxX, maxY);
        }

        /// <summary>This box grown by <paramref name="margin"/> on every side.</summary>
        /// <param name="margin">Millimetres; negative shrinks it.</param>
        /// <returns>The grown box.</returns>
        public BoardBox Inflate(double margin) => new(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);

        /// <summary>The smallest box holding both.</summary>
        /// <param name="other">The other box.</param>
        /// <returns>The union.</returns>
        public BoardBox Union(BoardBox other) =>
            new(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));

        /// <summary>Whether the two boxes share any point, edges included.</summary>
        /// <param name="other">The other box.</param>
        /// <returns>True when they touch or overlap.</returns>
        public bool Intersects(BoardBox other) =>
            MinX <= other.MaxX && other.MinX <= MaxX && MinY <= other.MaxY && other.MinY <= MaxY;

        /// <summary>The gap between two boxes, 0 when they touch or overlap.</summary>
        /// <param name="other">The other box.</param>
        /// <returns>Millimetres; a lower bound on the distance between anything inside them.</returns>
        public double DistanceTo(BoardBox other)
        {
            var dx = Math.Max(0, Math.Max(other.MinX - MaxX, MinX - other.MaxX));
            var dy = Math.Max(0, Math.Max(other.MinY - MaxY, MinY - other.MaxY));
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        /// <summary>Whether a point lies inside the box, edges included.</summary>
        /// <param name="p">The point.</param>
        /// <returns>True when it does.</returns>
        public bool Contains(BoardPoint p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
    }
}
