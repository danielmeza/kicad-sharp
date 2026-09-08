namespace KiCadSharp
{
    /// <summary>
    /// The s-expression token names KiCad's on-disk formats are spelled with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A token is a word in a file format, not a word in this library, so it is written down once
    /// and referred to everywhere else. A literal typed a second time is a defect that cannot be
    /// seen: <c>Node.GetChild("segments")</c> compiles, runs, and quietly reads nothing at all,
    /// because a missing child and a misspelled child are the same answer. When KiCad renames a
    /// token, the rename lands in one place here rather than in however many files happened to
    /// mention it.
    /// </para>
    /// <para>
    /// The groups follow the file formats, not this repository's directory layout:
    /// <see cref="Common"/> holds the tokens that mean the same thing in more than one format, and
    /// <see cref="Board"/>, <see cref="Footprint"/>, <see cref="Symbol"/> and <see cref="Schematic"/>
    /// hold what belongs to one grammar each. Where the same word names different things in
    /// different formats it is declared in each group that uses it, with a comment saying which
    /// thing — <see cref="Board.TrackArc"/> and <see cref="Common.Arc"/> are both <c>arc</c> and are
    /// not the same form.
    /// </para>
    /// <para>
    /// These are the tokens, the heads of forms. Values that appear inside a form and belong to a
    /// closed vocabulary are elsewhere: layer names in <see cref="KiCadLayerNames"/>, the standard
    /// property keys in <see cref="KiCadPropertyNames"/>.
    /// </para>
    /// </remarks>
    public static partial class KiCadTokens
    {
        /// <summary>
        /// Tokens that carry the same meaning in more than one of KiCad's file formats.
        /// </summary>
        /// <remarks>
        /// Membership here is measured, not assumed: a token is common when the library actually
        /// reads or writes it from two or more of the board, footprint, symbol and schematic
        /// grammars. Everything else lives in the group for the one format that uses it.
        /// </remarks>
        public static class Common
        {
            // ---------------------------------------------------------------- file header

            /// <summary>The file format's date stamp: <c>(version 20241229)</c>.</summary>
            public const string Version = "version";

            /// <summary>The program that wrote the file: <c>(generator "pcbnew")</c>.</summary>
            public const string Generator = "generator";

            /// <summary>The writing program's own version: <c>(generator_version "10.0")</c>.</summary>
            public const string GeneratorVersion = "generator_version";

            /// <summary>The sheet size: <c>(paper "A4")</c>.</summary>
            public const string Paper = "paper";

            /// <summary>Whether the file embeds its fonts: <c>(embedded_fonts no)</c>.</summary>
            public const string EmbeddedFonts = "embedded_fonts";

            // ---------------------------------------------------------------- title block

            /// <summary>The sheet's title block: <c>(title_block …)</c>.</summary>
            public const string TitleBlock = "title_block";

            /// <summary>The title block's title line.</summary>
            public const string Title = "title";

            /// <summary>The title block's date.</summary>
            public const string Date = "date";

            /// <summary>The title block's revision.</summary>
            public const string Rev = "rev";

            /// <summary>The title block's company.</summary>
            public const string Company = "company";

            /// <summary>One numbered free-text line of a title block: <c>(comment 1 "…")</c>.</summary>
            public const string Comment = "comment";

            // ---------------------------------------------------------------- geometry

            /// <summary>A placement: <c>(at x y [rotation])</c>.</summary>
            public const string At = "at";

            /// <summary>One point of a point list: <c>(xy x y)</c>.</summary>
            public const string Xy = "xy";

            /// <summary>A 3D triple, inside a footprint's <c>(model …)</c>: <c>(xyz x y z)</c>.</summary>
            public const string Xyz = "xyz";

            /// <summary>A point list: <c>(pts (xy …) …)</c>.</summary>
            public const string Pts = "pts";

            /// <summary>The first point of a two-point shape.</summary>
            public const string Start = "start";

            /// <summary>The last point of a two- or three-point shape.</summary>
            public const string End = "end";

            /// <summary>The point an arc passes through, between its start and its end.</summary>
            public const string Mid = "mid";

            /// <summary>The centre of a circle or of a circular shape.</summary>
            public const string Center = "center";

            /// <summary>A width/height pair: <c>(size w h)</c>.</summary>
            public const string Size = "size";

            /// <summary>A scale factor.</summary>
            public const string Scale = "scale";

            /// <summary>An angle, in degrees.</summary>
            public const string Angle = "angle";

            /// <summary>A length, in millimetres.</summary>
            public const string Length = "length";

            /// <summary>A width, in millimetres.</summary>
            public const string Width = "width";

            /// <summary>A thickness, in millimetres.</summary>
            public const string Thickness = "thickness";

            // ---------------------------------------------------------------- presentation

            /// <summary>Text rendering: <c>(effects (font …) …)</c>.</summary>
            public const string Effects = "effects";

            /// <summary>The font inside an <c>(effects …)</c> form.</summary>
            public const string Font = "font";

            /// <summary>A stroke: <c>(stroke (width w) (type t) [(color …)])</c>.</summary>
            public const string Stroke = "stroke";

            /// <summary>A fill, spelled either <c>(fill (type …))</c> or <c>(fill no)</c>.</summary>
            public const string Fill = "fill";

            /// <summary>A colour: <c>(color r g b a)</c>.</summary>
            public const string Color = "color";

            /// <summary>Bold text.</summary>
            public const string Bold = "bold";

            /// <summary>Italic text.</summary>
            public const string Italic = "italic";

            /// <summary>Whether something is hidden. KiCad 6 writes a bare <c>(hide)</c>.</summary>
            public const string Hide = "hide";

            /// <summary>A kind, inside the form it qualifies: a stroke type, a fill type, a layer type.</summary>
            public const string Type = "type";

            // ---------------------------------------------------------------- identity

            /// <summary>The persistent identifier KiCad 7 and later give almost everything.</summary>
            public const string Uuid = "uuid";

            /// <summary>A name.</summary>
            public const string Name = "name";

            /// <summary>The layer something is drawn on: <c>(layer "F.Cu")</c>.</summary>
            public const string Layer = "layer";

            /// <summary>A list of layers, or a board's layer table: <c>(layers …)</c>.</summary>
            public const string Layers = "layers";

            /// <summary>Whether an item is locked against editing.</summary>
            public const string Locked = "locked";

            /// <summary>A key/value property: <c>(property "Reference" "R1" …)</c>.</summary>
            public const string Property = "property";

            /// <summary>The list a group or a net-class flag names its members in.</summary>
            public const string Members = "members";

            /// <summary>A net: the board's <c>(net n "NAME")</c> table row, or the net an item is on.</summary>
            public const string Net = "net";

            /// <summary>A hole: a via's drill or a pad's.</summary>
            public const string Drill = "drill";

            // ---------------------------------------------------------------- symbols

            /// <summary>
            /// A symbol: a definition in a <c>.kicad_sym</c> or in a schematic's
            /// <c>(lib_symbols …)</c>, a sub-unit inside one, or a placed instance on a sheet.
            /// </summary>
            public const string Symbol = "symbol";

            /// <summary>A pin: a symbol's electrical pin, or a hierarchical sheet's pin.</summary>
            public const string Pin = "pin";

            /// <summary>Whether a symbol appears in the bill of materials.</summary>
            public const string InBom = "in_bom";

            /// <summary>Whether a symbol is transferred to the board.</summary>
            public const string OnBoard = "on_board";

            // ---------------------------------------------------------------- graphic shapes

            // The drawing forms of the symbol grammar. A schematic sheet draws with the same ones,
            // which is why they are here and not under Symbol.

            /// <summary>A polyline: <c>(polyline (pts …) (stroke …) (fill …))</c>.</summary>
            public const string Polyline = "polyline";

            /// <summary>A rectangle, drawn from two opposite corners.</summary>
            public const string Rectangle = "rectangle";

            /// <summary>A circle, drawn from a centre and a radius.</summary>
            public const string Circle = "circle";

            /// <summary>
            /// A graphic arc, drawn through three points. A board's routed <c>(arc …)</c> is spelled
            /// the same way and is a different form — see <see cref="Board.TrackArc"/>.
            /// </summary>
            public const string Arc = "arc";

            /// <summary>A Bézier curve.</summary>
            public const string Bezier = "bezier";

            /// <summary>A free text item.</summary>
            public const string Text = "text";

            /// <summary>A text box: text laid out inside a rectangle.</summary>
            public const string TextBox = "text_box";

            // ---------------------------------------------------------------- flag values

            /// <summary>The word KiCad 7 and later write for a flag that is set.</summary>
            public const string Yes = "yes";

            /// <summary>The word KiCad 7 and later write for a flag that is clear.</summary>
            public const string No = "no";
        }
    }
}
