using System;

using KiCadSharp.Documents;

namespace KiCadSharp.Fluent
{
    /// <summary>
    /// The fluent mirror of every <c>Add*</c> on <see cref="KiCadBoard"/> and the board items:
    /// <see cref="KiCadZone"/>, <see cref="KiCadGrPoly"/>, <see cref="KiCadGrCurve"/>,
    /// <see cref="KiCadDimension"/> and <see cref="KiCadGroup"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each method calls the <c>Add*</c> it is named after with the same arguments and returns the
    /// object it was called on. Only <see cref="KiCadBoard.AddNet"/> produces a child view, so only
    /// <see cref="WithNet"/> takes a callback; the <c>AddPoint</c> and <c>AddMember</c> methods
    /// return nothing, and there is nothing to hand over.
    /// </para>
    /// <para>
    /// Most board items have no <c>Add*</c> of their own: a zone, a track or a drawing is added
    /// through the board's live lists, <c>board.Zones.Add()</c>. Their fluent form is
    /// <see cref="NodeListFluentExtensions"/>'s <c>With</c>: <c>board.Zones.With(zone =&gt; zone.WithPoint(0, 0)…)</c>.
    /// </para>
    /// </remarks>
    public static class BoardFluentExtensions
    {
        /// <summary>Appends a net, as <see cref="KiCadBoard.AddNet"/> does, and returns the board.</summary>
        /// <param name="board">The board to append to.</param>
        /// <param name="code">The net code, which must be unique in the file.</param>
        /// <param name="name">The net name.</param>
        /// <param name="configure">Called with the new net.</param>
        /// <returns><paramref name="board"/>.</returns>
        public static KiCadBoard WithNet(this KiCadBoard board, int code, string name, Action<KiCadNet>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(board);
            return Chain.Then(board, board.AddNet(code, name), configure);
        }

        /// <summary>
        /// Appends a vertex to the outline, as <see cref="KiCadZone.AddPoint"/> does, and returns the zone.
        /// </summary>
        /// <param name="zone">The zone to append to.</param>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        /// <returns><paramref name="zone"/>.</returns>
        public static KiCadZone WithPoint(this KiCadZone zone, double x, double y)
        {
            ArgumentNullException.ThrowIfNull(zone);
            zone.AddPoint(x, y);
            return zone;
        }

        /// <summary>
        /// Appends a vertex, as <see cref="KiCadGrPoly.AddPoint"/> does, and returns the polygon.
        /// </summary>
        /// <param name="polygon">The polygon to append to.</param>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        /// <returns><paramref name="polygon"/>.</returns>
        public static KiCadGrPoly WithPoint(this KiCadGrPoly polygon, double x, double y)
        {
            ArgumentNullException.ThrowIfNull(polygon);
            polygon.AddPoint(x, y);
            return polygon;
        }

        /// <summary>
        /// Appends a control point, as <see cref="KiCadGrCurve.AddPoint"/> does, and returns the curve.
        /// </summary>
        /// <param name="curve">The curve to append to.</param>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        /// <returns><paramref name="curve"/>.</returns>
        public static KiCadGrCurve WithPoint(this KiCadGrCurve curve, double x, double y)
        {
            ArgumentNullException.ThrowIfNull(curve);
            curve.AddPoint(x, y);
            return curve;
        }

        /// <summary>
        /// Appends a measured point, as <see cref="KiCadDimension.AddPoint"/> does, and returns the dimension.
        /// </summary>
        /// <param name="dimension">The dimension to append to.</param>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        /// <returns><paramref name="dimension"/>.</returns>
        public static KiCadDimension WithPoint(this KiCadDimension dimension, double x, double y)
        {
            ArgumentNullException.ThrowIfNull(dimension);
            dimension.AddPoint(x, y);
            return dimension;
        }

        /// <summary>
        /// Adds an item's UUID to the group, as <see cref="KiCadGroup.AddMember"/> does, and returns the group.
        /// </summary>
        /// <param name="group">The group to add to.</param>
        /// <param name="uuid">The UUID of the item to add.</param>
        /// <returns><paramref name="group"/>.</returns>
        public static KiCadGroup WithMember(this KiCadGroup group, string uuid)
        {
            ArgumentNullException.ThrowIfNull(group);
            group.AddMember(uuid);
            return group;
        }
    }
}
