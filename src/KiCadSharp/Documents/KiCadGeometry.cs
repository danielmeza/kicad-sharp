using System;
using System.Globalization;

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
    /// <param name="Rotation">Degrees.</param>
    public readonly record struct KiCadPosition(double X, double Y, double Rotation = 0)
    {
        internal static KiCadPosition Read(SExpression? at) => at is null
            ? default
            : new KiCadPosition(at.GetValueAsDouble(0), at.GetValueAsDouble(1), at.GetValueAsDouble(2));

        internal readonly void Write(SExpression at, bool includeRotation)
        {
            at.SetValue(0, Numbers.Format(X), SQuoteStyle.Bare);
            at.SetValue(1, Numbers.Format(Y), SQuoteStyle.Bare);
            if (includeRotation || Rotation != 0 || at.GetValue(2) is not null)
            {
                at.SetValue(2, Numbers.Format(Rotation), SQuoteStyle.Bare);
            }
        }

        /// <inheritdoc />
        public override readonly string ToString() =>
            Rotation == 0 ? $"({Numbers.Format(X)}, {Numbers.Format(Y)})" : $"({Numbers.Format(X)}, {Numbers.Format(Y)}, {Numbers.Format(Rotation)}°)";
    }

    /// <summary>A width/height pair: <c>(size w h)</c>.</summary>
    /// <param name="Width">Millimetres.</param>
    /// <param name="Height">Millimetres.</param>
    public readonly record struct KiCadSize(double Width, double Height)
    {
        internal static KiCadSize Read(SExpression? size, double fallback = 0) => size is null
            ? new KiCadSize(fallback, fallback)
            : new KiCadSize(size.GetValueAsDouble(0), size.GetValueAsDouble(1));

        internal readonly void Write(SExpression size)
        {
            size.SetValue(0, Numbers.Format(Width), SQuoteStyle.Bare);
            size.SetValue(1, Numbers.Format(Height), SQuoteStyle.Bare);
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

        internal readonly void Write(SExpression owner)
        {
            var xyz = owner.GetChild(KiCadTokens.Common.Xyz) ?? owner.CreateChild(KiCadTokens.Common.Xyz);
            xyz.SetValue(0, Numbers.Format(X), SQuoteStyle.Bare);
            xyz.SetValue(1, Numbers.Format(Y), SQuoteStyle.Bare);
            xyz.SetValue(2, Numbers.Format(Z), SQuoteStyle.Bare);
        }
    }

    internal static class Numbers
    {
        internal static string Format(double value) => value.ToString("0.############", CultureInfo.InvariantCulture);
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
    /// A fill, spelled two ways: <c>(fill (type none|outline|background|color) [(color r g b a)])</c>
    /// in a symbol library, and a bare <c>(fill no)</c> / <c>(fill solid)</c> on the board and
    /// footprint shapes KiCad 7 and later write.
    /// </summary>
    /// <remarks>
    /// Both are read, and a set writes back into whichever spelling the node already uses — a
    /// <c>(fill no)</c> that came out of a <c>.kicad_pcb</c> must not grow a <c>(type ...)</c> child
    /// it never had, and a symbol's <c>(fill (type none))</c> must not collapse into a bare value.
    /// </remarks>
    public sealed class KiCadFill : KiCadNode
    {
        /// <summary>Creates a view over a <c>(fill ...)</c> node.</summary>
        /// <param name="node">The node.</param>
        public KiCadFill(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the fill type, as the node spells it.</summary>
        public string Type
        {
            get => ReadChild(KiCadTokens.Common.Type) ?? Node.GetValue(0) ?? "none";

            set
            {
                if (Node.GetChild(KiCadTokens.Common.Type) is null && Node.GetValue(0) is not null)
                {
                    WriteValue(0, value, SQuoteStyle.Bare);
                    return;
                }

                WriteChild(KiCadTokens.Common.Type, value, SQuoteStyle.Bare);
            }
        }

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

            set => Font.SetChildValue(KiCadTokens.Common.Thickness, Numbers.Format(value), SQuoteStyle.Bare);
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
