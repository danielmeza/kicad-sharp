using System;
using System.Globalization;
using System.Runtime.CompilerServices;

using SExpressions;

namespace KiCadSharp.Documents
{
    /// <summary>
    /// A position: <c>(at x y [rotation])</c>.
    /// </summary>
    /// <remarks>
    /// A value, not a view. It is read out of a node and written back as a whole, so
    /// <c>pin.Position = pin.Position with { X = 5 }</c> is how you move something —
    /// <c>pin.Position.X = 5</c> deliberately does not compile, because it would change nothing.
    /// </remarks>
    /// <param name="X">Millimetres, KiCad's own axis.</param>
    /// <param name="Y">Millimetres.</param>
    /// <param name="Rotation">
    /// The angle as the file stores it, which is degrees everywhere except a symbol's
    /// <c>(text ...)</c>: KiCad stores that one in tenths of a degree. See
    /// <see cref="KiCadText.RotationDegrees"/>.
    /// </param>
    public readonly record struct KiCadPosition(double X, double Y, double Rotation = 0)
    {
        internal static KiCadPosition Read(SExpression? at) => at is null
            ? default
            : new KiCadPosition(at.GetValueAsDouble(0), at.GetValueAsDouble(1), at.GetValueAsDouble(2));

        /// <summary>Writes the whole value into <paramref name="at"/>, or nothing.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A coordinate is not a number KiCad reads.</exception>
        internal readonly void Write(SExpression at, bool includeRotation)
        {
            var x = Numbers.Format(X);
            var y = Numbers.Format(Y);
            var rotation = Numbers.Format(Rotation);
            at.SetValue(0, x, SQuoteStyle.Bare);
            at.SetValue(1, y, SQuoteStyle.Bare);
            if (includeRotation || Rotation != 0 || at.GetValue(2) is not null)
            {
                at.SetValue(2, rotation, SQuoteStyle.Bare);
            }
        }

        /// <summary>
        /// Writes the whole value into <paramref name="owner"/>'s <paramref name="token"/> child,
        /// creating it where KiCad reads it — or, when a coordinate is not a number KiCad reads,
        /// throws before creating anything (#101).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A coordinate is not a number KiCad reads.</exception>
        internal readonly void Write(KiCadNode owner, string token, bool includeRotation)
        {
            Check();
            Write(KiCadChildOrder.Require(owner.Node, token), includeRotation);
        }

        /// <summary>Throws when a coordinate is not a number KiCad reads, and does nothing otherwise.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A coordinate is not a number KiCad reads.</exception>
        internal readonly void Check()
        {
            Numbers.Check(X);
            Numbers.Check(Y);
            Numbers.Check(Rotation);
        }

        /// <inheritdoc />
        public override readonly string ToString() =>
            Rotation == 0 ? $"({Numbers.Text(X)}, {Numbers.Text(Y)})" : $"({Numbers.Text(X)}, {Numbers.Text(Y)}, {Numbers.Text(Rotation)}°)";
    }

    /// <summary>A width/height pair: <c>(size w h)</c>.</summary>
    /// <param name="Width">Millimetres.</param>
    /// <param name="Height">Millimetres.</param>
    public readonly record struct KiCadSize(double Width, double Height)
    {
        internal static KiCadSize Read(SExpression? size, double fallback = 0) => size is null
            ? new KiCadSize(fallback, fallback)
            : new KiCadSize(size.GetValueAsDouble(0), size.GetValueAsDouble(1));

        /// <summary>Writes the whole value into <paramref name="size"/>, or nothing.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A side is not a number KiCad reads.</exception>
        internal readonly void Write(SExpression size)
        {
            var width = Numbers.Format(Width);
            var height = Numbers.Format(Height);
            size.SetValue(0, width, SQuoteStyle.Bare);
            size.SetValue(1, height, SQuoteStyle.Bare);
        }

        /// <summary>
        /// Writes the whole value into <paramref name="owner"/>'s <paramref name="token"/> child,
        /// creating it where KiCad reads it — or, when a side is not a number KiCad reads, throws
        /// before creating anything (#101).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A side is not a number KiCad reads.</exception>
        internal readonly void Write(KiCadNode owner, string token)
        {
            Check();
            Write(KiCadChildOrder.Require(owner.Node, token));
        }

        /// <summary>Throws when a side is not a number KiCad reads, and does nothing otherwise.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A side is not a number KiCad reads.</exception>
        internal readonly void Check()
        {
            Numbers.Check(Width);
            Numbers.Check(Height);
        }
    }

    /// <summary>A 3D triple: the <c>(xyz x y z)</c> inside a footprint's <c>(model ...)</c>.</summary>
    /// <param name="X">X.</param>
    /// <param name="Y">Y.</param>
    /// <param name="Z">Z.</param>
    public readonly record struct KiCadXyz(double X, double Y, double Z)
    {
        internal static KiCadXyz Read(SExpression? owner, double fallback)
        {
            var xyz = owner?.GetChild(KiCadTokens.Common.Xyz) ?? owner;
            return xyz is null
                ? new KiCadXyz(fallback, fallback, fallback)
                : new KiCadXyz(xyz.GetValueAsDouble(0), xyz.GetValueAsDouble(1), xyz.GetValueAsDouble(2));
        }

        /// <summary>Writes the whole value into <paramref name="owner"/>'s <c>(xyz …)</c>, or nothing.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A coordinate is not a number KiCad reads.</exception>
        internal readonly void Write(SExpression owner)
        {
            var x = Numbers.Format(X);
            var y = Numbers.Format(Y);
            var z = Numbers.Format(Z);
            var xyz = owner.GetChild(KiCadTokens.Common.Xyz) ?? owner.CreateChild(KiCadTokens.Common.Xyz);
            xyz.SetValue(0, x, SQuoteStyle.Bare);
            xyz.SetValue(1, y, SQuoteStyle.Bare);
            xyz.SetValue(2, z, SQuoteStyle.Bare);
        }

        /// <summary>
        /// Writes the whole value into the <c>(xyz …)</c> of <paramref name="owner"/>'s
        /// <paramref name="token"/> child, creating both where KiCad reads them — or, when a
        /// coordinate is not a number KiCad reads, throws before creating anything (#101).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A coordinate is not a number KiCad reads.</exception>
        internal readonly void Write(KiCadNode owner, string token)
        {
            Numbers.Check(X);
            Numbers.Check(Y);
            Numbers.Check(Z);
            Write(KiCadChildOrder.Require(owner.Node, token));
        }
    }

    /// <summary>
    /// The one formatter every number this library writes goes through, and the one check on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>double.NaN</c>, <c>double.PositiveInfinity</c> and <c>double.NegativeInfinity</c> format
    /// as <c>NaN</c>, <c>Infinity</c> and <c>-Infinity</c>, and KiCad reads none of them as a
    /// number. MEASURED against kicad-cli 10.0.6: <c>pcb export svg</c> refuses the whole board
    /// over each, exit 3, "Failed to load board: need a number for 'hatch pitch'", "… for 'zone
    /// clearance'", "… for 'min_thickness'", "… for 'X coordinate'". The message is the lexer's,
    /// <c>DSNLEXER::NeedNUMBER</c> (<c>common/dsnlexer.cpp</c>, lines 427–439), so no token is
    /// exempt. A setter therefore refuses such a value and writes nothing (#101).
    /// </para>
    /// <para>
    /// A range is not checked. KiCad clamps some values when it loads a board rather than refusing
    /// it — a zone's hatch pitch to 0.1–2 mm, for one — and which values, to what, is per token.
    /// </para>
    /// </remarks>
    internal static class Numbers
    {
        /// <summary>
        /// Formats a number the way KiCad reads it: invariant, no exponent, no trailing zeroes
        /// beyond what the value needs.
        /// </summary>
        /// <param name="value">The number.</param>
        /// <param name="name">The argument the number came from; the compiler fills it in.</param>
        /// <returns>The text.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not a number KiCad reads.</exception>
        internal static string Format(double value, [CallerArgumentExpression(nameof(value))] string? name = null)
        {
            Check(value, name);
            return Text(value);
        }

        /// <summary>Throws when <paramref name="value"/> is not a number KiCad reads, and does nothing otherwise.</summary>
        /// <param name="value">The number.</param>
        /// <param name="name">The argument the number came from; the compiler fills it in.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinite.</exception>
        internal static void Check(double value, [CallerArgumentExpression(nameof(value))] string? name = null)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    $"KiCad reads no number {Text(value)}: a value written to a document must be finite.");
            }
        }

        /// <summary>
        /// The text of any number, for a description. Only <see cref="Format"/> writes to a document.
        /// </summary>
        /// <param name="value">The number.</param>
        /// <returns>The text, <c>NaN</c> and <c>Infinity</c> included.</returns>
        internal static string Text(double value) => value.ToString("0.############", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A stroke: <c>(stroke (width w) (type t) [(color r g b a)])</c>.
    /// </summary>
    public sealed class KiCadStroke : KiCadNode
    {
        /// <summary>Creates a view over a <c>(stroke ...)</c> node.</summary>
        /// <param name="node">The node.</param>
        public KiCadStroke(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the stroke width in millimetres.</summary>
        public double Width
        {
            get => ReadChildDouble(KiCadTokens.Common.Width, 0, 0.254);
            set => WriteChildDouble(KiCadTokens.Common.Width, value);
        }

        /// <summary>Gets or sets the stroke type, e.g. <c>default</c>, <c>solid</c>, <c>dash</c>.</summary>
        public string Type
        {
            get => ReadChild(KiCadTokens.Common.Type) ?? "default";
            set => WriteChild(KiCadTokens.Common.Type, value, SQuoteStyle.Bare);
        }

        /// <summary>Gets the colour components as written, or an empty list when there is no colour.</summary>
        public IReadOnlyList<string> Color
        {
            get
            {
                var color = Node.GetChild(KiCadTokens.Common.Color);
                return color is null ? Array.Empty<string>() : color.Values.ToArray();
            }
        }
    }

    /// <summary>
    /// The two ways KiCad spells a <c>(fill …)</c>, one per parser. Which one a shape takes depends on
    /// the kind of file it is in, not on the shape.
    /// </summary>
    /// <remarks>
    /// From KiCad 10.0.6. The schematic parser, which also reads symbol libraries, takes only the
    /// <c>(type …)</c> form (<c>eeschema/sch_io/kicad_sexpr/sch_io_kicad_sexpr_parser.cpp</c>,
    /// <c>parseFill</c>, line 708). The board parser, which also reads footprints, takes only a bare
    /// value (<c>pcbnew/pcb_io/kicad_sexpr/pcb_io_kicad_sexpr_parser.cpp</c>, lines 3576–3598). Each
    /// rejects the other's form and refuses the whole file.
    /// </remarks>
    public enum KiCadFillSpelling
    {
        /// <summary>
        /// <c>(fill (type none|outline|background|color|hatch|reverse_hatch|cross_hatch) [(color r g b a)])</c>,
        /// on every shape in a symbol library or a schematic, on a text box and on a sheet. KiCad
        /// writes it in <c>formatFill</c> (<c>eeschema/sch_io/kicad_sexpr/sch_io_kicad_sexpr_common.cpp</c>,
        /// line 33).
        /// </summary>
        Schematic,

        /// <summary>
        /// <c>(fill yes|no|solid|none|hatch|reverse_hatch|cross_hatch)</c>, a bare value, on a board's
        /// <c>gr_rect</c>, <c>gr_circle</c> and <c>gr_poly</c> and a footprint's <c>fp_rect</c>,
        /// <c>fp_circle</c> and <c>fp_poly</c>. KiCad writes <c>yes</c>, <c>no</c> or a hatch
        /// (<c>pcbnew/pcb_io/kicad_sexpr/pcb_io_kicad_sexpr.cpp</c>, lines 1071–1098). A zone's fill
        /// is a different form again; see <see cref="KiCadZoneFill"/>.
        /// </summary>
        Board,
    }

    /// <summary>
    /// A fill, spelled two ways: <c>(fill (type none|outline|background|color) [(color r g b a)])</c>
    /// in a symbol library or a schematic, and a bare <c>(fill no)</c> / <c>(fill yes)</c> on board and
    /// footprint shapes. See <see cref="KiCadFillSpelling"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both are read, and a set writes back into whichever spelling the node already uses — a
    /// <c>(fill no)</c> that came out of a <c>.kicad_pcb</c> must not grow a <c>(type ...)</c> child
    /// it never had, and a symbol's <c>(fill (type none))</c> must not collapse into a bare value.
    /// </para>
    /// <para>
    /// An empty <c>(fill)</c>, which is what <c>RequireFill()</c> creates, has no spelling of its own.
    /// The first <see cref="Type"/> set writes its owner's, which every shape's <c>Fill</c> and
    /// <c>RequireFill()</c> pass in. KiCad 10 refuses a board whose <c>gr_rect</c> says
    /// <c>(fill (type no))</c> (#64).
    /// </para>
    /// </remarks>
    public sealed class KiCadFill : KiCadNode
    {
        /// <summary>
        /// The words the schematic parser reads, each mapped to itself, plus the board's words for the
        /// same fills. The mapped words are one fill in KiCad's own model, <c>FILL_T</c>
        /// (<c>include/eda_shape.h</c>, line 57): the schematic's <c>outline</c> and the board's
        /// <c>yes</c> and <c>solid</c> are all <c>FILLED_SHAPE</c>, and <c>none</c> and <c>no</c> are
        /// both <c>NO_FILL</c>.
        /// </summary>
        private static readonly Dictionary<string, string> SchematicWords = new(StringComparer.Ordinal)
        {
            // sch_io_kicad_sexpr_parser.cpp, lines 732–738.
            ["none"] = "none",
            ["outline"] = "outline",
            ["background"] = "background",
            ["color"] = "color",
            ["hatch"] = "hatch",
            ["reverse_hatch"] = "reverse_hatch",
            ["cross_hatch"] = "cross_hatch",
            ["no"] = "none",
            ["yes"] = "outline",
            ["solid"] = "outline",
        };

        /// <summary>
        /// The words the board parser reads, and the schematic's <c>outline</c>; see
        /// <see cref="SchematicWords"/>. A board has no <c>background</c> or <c>color</c> fill: its
        /// parser has no word for either, and its writer saves anything it does not know as
        /// <c>no</c> (<c>pcb_io_kicad_sexpr.cpp</c>, line 1094).
        /// </summary>
        private static readonly Dictionary<string, string> BoardWords = new(StringComparer.Ordinal)
        {
            // pcb_io_kicad_sexpr_parser.cpp, lines 3588–3596.
            ["yes"] = "yes",
            ["solid"] = "solid",
            ["none"] = "none",
            ["no"] = "no",
            ["hatch"] = "hatch",
            ["reverse_hatch"] = "reverse_hatch",
            ["cross_hatch"] = "cross_hatch",
            ["outline"] = "yes",
        };

        private readonly KiCadFillSpelling _ownerSpelling;

        /// <summary>
        /// Creates a view over a <c>(fill ...)</c> node. A node with no spelling of its own is written
        /// in the schematic's; for a fill on a board or footprint shape, use the other constructor.
        /// </summary>
        /// <param name="node">The node.</param>
        public KiCadFill(SExpression node)
            : this(node, KiCadFillSpelling.Schematic)
        {
        }

        /// <summary>Creates a view over a <c>(fill ...)</c> node, knowing which spelling its owner takes.</summary>
        /// <param name="node">The node.</param>
        /// <param name="ownerSpelling">
        /// The spelling KiCad reads for the shape that holds the node. It is used only when the node
        /// has no spelling of its own, as in the empty <c>(fill)</c> a <c>RequireFill()</c> creates.
        /// </param>
        public KiCadFill(SExpression node, KiCadFillSpelling ownerSpelling)
            : base(node)
        {
            _ownerSpelling = ownerSpelling;
        }

        /// <summary>Gets or sets the fill type, as the node spells it.</summary>
        /// <remarks>
        /// <para>
        /// A set writes into the node's own spelling, or its owner's when it has none, and takes the
        /// words KiCad 10 reads there. On a board or footprint shape those are <c>yes</c>, <c>no</c>,
        /// <c>solid</c>, <c>none</c>, <c>hatch</c>, <c>reverse_hatch</c> and <c>cross_hatch</c>. In a
        /// symbol or a schematic they are <c>none</c>, <c>outline</c>, <c>background</c>,
        /// <c>color</c>, <c>hatch</c>, <c>reverse_hatch</c> and <c>cross_hatch</c>.
        /// </para>
        /// <para>
        /// A word from the other spelling that means the same fill is written as this spelling's
        /// word, so <c>"no"</c> and <c>"none"</c> both work on every shape. In a symbol, <c>no</c>
        /// becomes <c>none</c>, and <c>yes</c> and <c>solid</c> become <c>outline</c>. On a board,
        /// <c>outline</c> becomes <c>yes</c>. Reading <see cref="Type"/> back gives the word written.
        /// Any other word throws rather than write a file KiCad refuses, and so do <c>background</c>
        /// and <c>color</c> on a board, which has neither fill.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">KiCad 10 reads no such fill in this spelling.</exception>
        public string Type
        {
            get => ReadChild(KiCadTokens.Common.Type) ?? Node.GetValue(0) ?? "none";

            set
            {
                ArgumentNullException.ThrowIfNull(value);
                var spelling = Spelling;
                var words = spelling == KiCadFillSpelling.Board ? BoardWords : SchematicWords;
                if (!words.TryGetValue(value, out var word))
                {
                    throw new ArgumentException(
                        spelling == KiCadFillSpelling.Board
                            ? $"KiCad reads no fill \"{value}\" on a board or footprint shape. Use yes, no, solid, none, hatch, reverse_hatch or cross_hatch."
                            : $"KiCad reads no fill \"{value}\" in a symbol or a schematic. Use none, outline, background, color, hatch, reverse_hatch or cross_hatch.",
                        nameof(value));
                }

                if (spelling == KiCadFillSpelling.Board)
                {
                    WriteValue(0, word, SQuoteStyle.Bare);
                    return;
                }

                WriteChild(KiCadTokens.Common.Type, word, SQuoteStyle.Bare);
            }
        }

        /// <summary>
        /// The spelling a set writes: the node's own when it has one, a <c>(type …)</c> child or a
        /// bare value, and its owner's otherwise. A sheet's <c>(fill (color …))</c> has neither.
        /// </summary>
        private KiCadFillSpelling Spelling =>
            Node.GetChild(KiCadTokens.Common.Type) is not null ? KiCadFillSpelling.Schematic
            : Node.GetValue(0) is not null ? KiCadFillSpelling.Board
            : _ownerSpelling;

        /// <summary>
        /// True when the shape is filled at all. The two spellings disagree on the word for "not
        /// filled" — <c>none</c> in a symbol, <c>no</c> on a board — so asking this is more reliable
        /// than comparing <see cref="Type"/> against a literal.
        /// </summary>
        public bool IsFilled =>
            !string.Equals(Type, "none", StringComparison.Ordinal)
            && !string.Equals(Type, KiCadTokens.Common.No, StringComparison.Ordinal);

        /// <summary>Gets the colour components as written, or an empty list when there is no colour.</summary>
        public IReadOnlyList<string> Color
        {
            get
            {
                var color = Node.GetChild(KiCadTokens.Common.Color);
                return color is null ? Array.Empty<string>() : color.Values.ToArray();
            }
        }
    }

    /// <summary>
    /// Text rendering: <c>(effects (font (size w h) [(thickness t)] [bold] [italic]) [(justify ...)] [(hide yes)])</c>.
    /// </summary>
    public sealed class KiCadFontEffects : KiCadNode
    {
        /// <summary>Creates a view over an <c>(effects ...)</c> node.</summary>
        /// <param name="node">The node.</param>
        public KiCadFontEffects(SExpression node)
            : base(node)
        {
        }

        private SExpression Font => Node.GetChild(KiCadTokens.Common.Font) ?? Node.CreateChild(KiCadTokens.Common.Font);

        /// <summary>Gets or sets the glyph size, in millimetres.</summary>
        public KiCadSize Size
        {
            get => KiCadSize.Read(Node.GetChild(KiCadTokens.Common.Font)?.GetChild(KiCadTokens.Common.Size), 1.27);
            set
            {
                value.Check();
                var font = Font;
                value.Write(font.GetChild(KiCadTokens.Common.Size) ?? font.CreateChild(KiCadTokens.Common.Size));
            }
        }

        /// <summary>Gets or sets the pen thickness, in millimetres.</summary>
        public double Thickness
        {
            get
            {
                var thickness = Node.GetChild(KiCadTokens.Common.Font)?.GetChild(KiCadTokens.Common.Thickness);
                return thickness is not null && thickness.TryGetValue<double>(0, out var value) ? value : 0.25;
            }

            set
            {
                var text = Numbers.Format(value);
                Font.SetChildValue(KiCadTokens.Common.Thickness, text, SQuoteStyle.Bare);
            }
        }

        /// <summary>Gets or sets whether the text is bold.</summary>
        public bool Bold
        {
            get => Node.GetChild(KiCadTokens.Common.Font) is { } font && (font.GetChild(KiCadTokens.Common.Bold) is { } b && (b.GetValue(0) is null || b.GetValueAsBool()));
            set => Font.SetChildValue(KiCadTokens.Common.Bold, value ? KiCadTokens.Common.Yes : KiCadTokens.Common.No, SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets whether the text is italic.</summary>
        public bool Italic
        {
            get => Node.GetChild(KiCadTokens.Common.Font) is { } font && (font.GetChild(KiCadTokens.Common.Italic) is { } i && (i.GetValue(0) is null || i.GetValueAsBool()));
            set => Font.SetChildValue(KiCadTokens.Common.Italic, value ? KiCadTokens.Common.Yes : KiCadTokens.Common.No, SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets whether the text is hidden.</summary>
        public bool Hide
        {
            get => ReadFlag(KiCadTokens.Common.Hide);
            set => WriteFlag(KiCadTokens.Common.Hide, value);
        }
    }
}
