using System;
using System.Collections.Generic;

using KiCadSharp.Documents;

namespace KiCadSharp.Fluent
{
    /// <summary>
    /// The fluent mirror of every <c>Add*</c> on <see cref="KiCadFootprintLibrary"/>,
    /// <see cref="KiCadFootprint"/> and <see cref="KiCadFpPoly"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each method calls the <c>Add*</c> it is named after — with the same arguments, so it has the
    /// same effect, the same defaults and the same exceptions — hands what that call returned to the
    /// optional <c>configure</c> callback, and returns the object it was called on. A chain of these
    /// builds exactly the document the equivalent run of <c>Add*</c> statements builds.
    /// </para>
    /// <para>
    /// <see cref="KiCadFpPoly.AddPoint"/> returns nothing, so <see cref="WithPoint"/> has no callback:
    /// there is no child view to hand over.
    /// </para>
    /// </remarks>
    public static class FootprintFluentExtensions
    {
        /// <summary>
        /// Appends a footprint, as <see cref="KiCadFootprintLibrary.AddFootprint"/> does, and returns
        /// the library.
        /// </summary>
        /// <typeparam name="TFootprint">The footprint's type, which the callback receives.</typeparam>
        /// <param name="library">The library to append to.</param>
        /// <param name="footprint">The footprint.</param>
        /// <param name="configure">Called with <paramref name="footprint"/> once it is a child of the library.</param>
        /// <returns><paramref name="library"/>.</returns>
        /// <exception cref="InvalidOperationException">The file is a single footprint and cannot hold another.</exception>
        public static KiCadFootprintLibrary WithFootprint<TFootprint>(this KiCadFootprintLibrary library, TFootprint footprint, Action<TFootprint>? configure = null)
            where TFootprint : KiCadFootprint
        {
            ArgumentNullException.ThrowIfNull(library);
            library.AddFootprint(footprint);
            return Chain.Then(library, footprint, configure);
        }

        /// <summary>
        /// Appends a text item, as <see cref="KiCadFootprint.AddFpText"/> does, and returns the footprint.
        /// </summary>
        /// <param name="footprint">The footprint to append to.</param>
        /// <param name="type">The kind of text: <c>reference</c>, <c>value</c> or <c>user</c>.</param>
        /// <param name="text">The text.</param>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        /// <param name="layer">The layer to draw it on.</param>
        /// <param name="configure">Called with the new text item.</param>
        /// <returns><paramref name="footprint"/>.</returns>
        public static KiCadFootprint WithFpText(this KiCadFootprint footprint, string type, string text, double x, double y, string layer, Action<KiCadFpText>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(footprint);
            return Chain.Then(footprint, footprint.AddFpText(type, text, x, y, layer), configure);
        }

        /// <summary>
        /// Appends a pad, as <see cref="KiCadFootprint.AddPad"/> does, and returns the footprint.
        /// </summary>
        /// <param name="footprint">The footprint to append to.</param>
        /// <param name="number">The pad number.</param>
        /// <param name="type">The pad type, e.g. <c>smd</c>, <c>thru_hole</c>.</param>
        /// <param name="shape">The pad shape, e.g. <c>rect</c>, <c>roundrect</c>.</param>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        /// <param name="width">Pad width, millimetres.</param>
        /// <param name="height">Pad height, millimetres.</param>
        /// <param name="layers">The layers the pad is on.</param>
        /// <param name="configure">Called with the new pad.</param>
        /// <returns><paramref name="footprint"/>.</returns>
        public static KiCadFootprint WithPad(this KiCadFootprint footprint, string number, string type, string shape, double x, double y, double width, double height, IEnumerable<string> layers, Action<KiCadPad>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(footprint);
            return Chain.Then(footprint, footprint.AddPad(number, type, shape, x, y, width, height, layers), configure);
        }

        /// <summary>
        /// Appends a line with <see cref="KiCadFootprint.AddLine"/>'s default stroke width, and
        /// returns the footprint.
        /// </summary>
        /// <param name="footprint">The footprint to append to.</param>
        /// <param name="startX">Start X.</param>
        /// <param name="startY">Start Y.</param>
        /// <param name="endX">End X.</param>
        /// <param name="endY">End Y.</param>
        /// <param name="layer">The layer.</param>
        /// <param name="configure">Called with the new line.</param>
        /// <returns><paramref name="footprint"/>.</returns>
        /// <remarks>
        /// <c>AddLine</c>'s <c>width</c> is optional, and an optional <c>double</c> ahead of the
        /// callback would make a positional lambda bind to it and fail to compile. So the width is
        /// an overload rather than a default here, and leaving it out calls <c>AddLine</c> without
        /// one: the default stays the core's, not a copy of it.
        /// </remarks>
        public static KiCadFootprint WithLine(this KiCadFootprint footprint, double startX, double startY, double endX, double endY, string layer, Action<KiCadFpLine>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(footprint);
            return Chain.Then(footprint, footprint.AddLine(startX, startY, endX, endY, layer), configure);
        }

        /// <summary>
        /// Appends a line, as <see cref="KiCadFootprint.AddLine"/> does, and returns the footprint.
        /// </summary>
        /// <param name="footprint">The footprint to append to.</param>
        /// <param name="startX">Start X.</param>
        /// <param name="startY">Start Y.</param>
        /// <param name="endX">End X.</param>
        /// <param name="endY">End Y.</param>
        /// <param name="layer">The layer.</param>
        /// <param name="width">Stroke width, millimetres.</param>
        /// <param name="configure">Called with the new line.</param>
        /// <returns><paramref name="footprint"/>.</returns>
        public static KiCadFootprint WithLine(this KiCadFootprint footprint, double startX, double startY, double endX, double endY, string layer, double width, Action<KiCadFpLine>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(footprint);
            return Chain.Then(footprint, footprint.AddLine(startX, startY, endX, endY, layer, width), configure);
        }

        /// <summary>
        /// Appends a circle with <see cref="KiCadFootprint.AddCircle"/>'s default stroke width, and
        /// returns the footprint.
        /// </summary>
        /// <param name="footprint">The footprint to append to.</param>
        /// <param name="centerX">Centre X.</param>
        /// <param name="centerY">Centre Y.</param>
        /// <param name="endX">A point on the circumference, X.</param>
        /// <param name="endY">A point on the circumference, Y.</param>
        /// <param name="layer">The layer.</param>
        /// <param name="configure">Called with the new circle.</param>
        /// <returns><paramref name="footprint"/>.</returns>
        /// <remarks>An overload rather than a default for the width, for the reason given on <c>WithLine</c>.</remarks>
        public static KiCadFootprint WithCircle(this KiCadFootprint footprint, double centerX, double centerY, double endX, double endY, string layer, Action<KiCadFpCircle>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(footprint);
            return Chain.Then(footprint, footprint.AddCircle(centerX, centerY, endX, endY, layer), configure);
        }

        /// <summary>
        /// Appends a circle, as <see cref="KiCadFootprint.AddCircle"/> does, and returns the footprint.
        /// </summary>
        /// <param name="footprint">The footprint to append to.</param>
        /// <param name="centerX">Centre X.</param>
        /// <param name="centerY">Centre Y.</param>
        /// <param name="endX">A point on the circumference, X.</param>
        /// <param name="endY">A point on the circumference, Y.</param>
        /// <param name="layer">The layer.</param>
        /// <param name="width">Stroke width, millimetres.</param>
        /// <param name="configure">Called with the new circle.</param>
        /// <returns><paramref name="footprint"/>.</returns>
        public static KiCadFootprint WithCircle(this KiCadFootprint footprint, double centerX, double centerY, double endX, double endY, string layer, double width, Action<KiCadFpCircle>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(footprint);
            return Chain.Then(footprint, footprint.AddCircle(centerX, centerY, endX, endY, layer, width), configure);
        }

        /// <summary>
        /// Appends a 3D model reference, as <see cref="KiCadFootprint.AddModel"/> does, and returns
        /// the footprint.
        /// </summary>
        /// <param name="footprint">The footprint to append to.</param>
        /// <param name="path">The model path, usually with a <c>${KICAD…_3DMODEL_DIR}</c> prefix.</param>
        /// <param name="configure">Called with the new model, e.g. to set its offset, scale or rotation.</param>
        /// <returns><paramref name="footprint"/>.</returns>
        public static KiCadFootprint WithModel(this KiCadFootprint footprint, string path, Action<KiCadModel>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(footprint);
            return Chain.Then(footprint, footprint.AddModel(path), configure);
        }

        /// <summary>
        /// Appends a vertex, as <see cref="KiCadFpPoly.AddPoint"/> does, and returns the polygon.
        /// </summary>
        /// <param name="polygon">The polygon to append to.</param>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        /// <returns><paramref name="polygon"/>.</returns>
        public static KiCadFpPoly WithPoint(this KiCadFpPoly polygon, double x, double y)
        {
            ArgumentNullException.ThrowIfNull(polygon);
            polygon.AddPoint(x, y);
            return polygon;
        }
    }
}
