using System;
using System.Collections.Generic;
using System.Linq;

using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Schematics
{
    /// <summary>
    /// A connection drawn between two points: the shared shape of <c>(wire ...)</c> and
    /// <c>(bus ...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both forms are <c>(pts (xy x y) (xy x y))</c> plus a stroke and a UUID; only the token says
    /// whether the editor treats the segment as one net or as a bundle. They share a base so the two
    /// tokens cannot drift apart, and so a caller that only cares about geometry can take either.
    /// </para>
    /// <para>
    /// KiCad writes exactly two points per segment, but the format allows more, so
    /// <see cref="Points"/> is the honest reading and <see cref="Start"/>/<see cref="End"/> are the
    /// first and last of it.
    /// </para>
    /// </remarks>
    public abstract class KiCadSchematicLine : KiCadNode
    {
        /// <summary>Creates a view over a segment form.</summary>
        /// <param name="node">The form.</param>
        protected KiCadSchematicLine(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets every vertex of the segment, in the order the file lists them.</summary>
        public IReadOnlyList<KiCadPosition> Points =>
            (Node.GetChild("pts")?.GetChildren("xy") ?? Enumerable.Empty<SExpression>())
                .Select(xy => new KiCadPosition(xy.GetValueAsDouble(0), xy.GetValueAsDouble(1)))
                .ToArray();

        /// <summary>Gets or sets the first vertex. Setting it moves that end and nothing else.</summary>
        public KiCadPosition Start
        {
            get
            {
                var points = Points;
                return points.Count == 0 ? default : points[0];
            }

            set => WriteXy(PointAt(0), value);
        }

        /// <summary>
        /// Gets or sets the last vertex. On a segment with a single point, setting this appends the
        /// second one rather than overwriting the first.
        /// </summary>
        public KiCadPosition End
        {
            get
            {
                var points = Points;
                return points.Count == 0 ? default : points[^1];
            }

            set
            {
                var vertices = Vertices();
                WriteXy(vertices.Count >= 2 ? vertices[^1] : PointAt(1), value);
            }
        }

        /// <summary>Gets the stroke, creating a <c>(stroke ...)</c> child if there is none.</summary>
        public KiCadStroke? Stroke => Node.GetChild("stroke") is { } node ? new KiCadStroke(node) : null;

        /// <summary>Gets the <c>(stroke ...)</c> form, adding an empty one when the node has none.</summary>
        /// <returns>The view.</returns>
        public KiCadStroke RequireStroke() => new(Require("stroke"));

        /// <summary>Gets or sets the segment's UUID, which is what the editor's undo history keys on.</summary>
        public string Uuid
        {
            get => ReadChild("uuid") ?? string.Empty;
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }

        /// <summary>Appends a vertex to the segment.</summary>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        public void AddPoint(double x, double y) =>
            PointsNode.CreateChild("xy", Numbers.Format(x), Numbers.Format(y));

        private SExpression PointsNode => Node.GetChild("pts") ?? Node.CreateChild("pts");

        private List<SExpression> Vertices() => (Node.GetChild("pts")?.GetChildren("xy") ?? Enumerable.Empty<SExpression>()).ToList();

        private SExpression PointAt(int index)
        {
            var points = PointsNode;
            var vertices = points.GetChildren("xy").ToList();
            while (vertices.Count <= index)
            {
                vertices.Add(points.CreateChild("xy", "0", "0"));
            }

            return vertices[index];
        }

        private static void WriteXy(SExpression xy, KiCadPosition point)
        {
            xy.SetValue(0, Numbers.Format(point.X), SQuoteStyle.Bare);
            xy.SetValue(1, Numbers.Format(point.Y), SQuoteStyle.Bare);
        }
    }

    /// <summary>A wire: <c>(wire (pts (xy x y) (xy x y)) (stroke ...) (uuid ...))</c>.</summary>
    public sealed class KiCadWire : KiCadSchematicLine
    {
        /// <summary>Creates a view over a <c>(wire ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadWire(SExpression node)
            : base(node)
        {
        }
    }

    /// <summary>
    /// A bus: the same form as a wire, drawn thicker, carrying the members a
    /// <see cref="KiCadBusAlias"/> or a <c>{}</c> label names.
    /// </summary>
    public sealed class KiCadBus : KiCadSchematicLine
    {
        /// <summary>Creates a view over a <c>(bus ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadBus(SExpression node)
            : base(node)
        {
        }
    }

    /// <summary>
    /// The stub that taps one signal off a bus: <c>(bus_entry (at x y) (size w h) (stroke ...) (uuid ...))</c>.
    /// </summary>
    /// <remarks>
    /// The size is a displacement, not an extent: the entry runs from <see cref="Position"/> to
    /// <see cref="Position"/> plus <see cref="Size"/>, which is why it is usually negative in one axis.
    /// </remarks>
    public sealed class KiCadBusEntry : KiCadNode
    {
        /// <summary>Creates a view over a <c>(bus_entry ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadBusEntry(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets where the entry starts.</summary>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild("at"));
            set => value.Write(Require("at"), includeRotation: false);
        }

        /// <summary>Gets or sets how far the entry runs from <see cref="Position"/>.</summary>
        public KiCadSize Size
        {
            get => KiCadSize.Read(Node.GetChild("size"));
            set => value.Write(Require("size"));
        }

        /// <summary>Gets the stroke, creating a <c>(stroke ...)</c> child if there is none.</summary>
        public KiCadStroke? Stroke => Node.GetChild("stroke") is { } node ? new KiCadStroke(node) : null;

        /// <summary>Gets the <c>(stroke ...)</c> form, adding an empty one when the node has none.</summary>
        /// <returns>The view.</returns>
        public KiCadStroke RequireStroke() => new(Require("stroke"));

        /// <summary>Gets or sets the entry's UUID.</summary>
        public string Uuid
        {
            get => ReadChild("uuid") ?? string.Empty;
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }
    }

    /// <summary>
    /// The dot that says four wire ends are one node: <c>(junction (at x y) (diameter d) (color r g b a) (uuid ...))</c>.
    /// </summary>
    /// <remarks>
    /// A zero diameter and a zero colour both mean "whatever the theme says", which is what KiCad
    /// writes for every junction it places itself — so those are not missing values to be filled in.
    /// </remarks>
    public sealed class KiCadJunction : KiCadNode
    {
        /// <summary>Creates a view over a <c>(junction ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadJunction(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets where the junction sits.</summary>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild("at"));
            set => value.Write(Require("at"), includeRotation: false);
        }

        /// <summary>Gets or sets the dot's diameter in millimetres; 0 defers to the theme.</summary>
        public double Diameter
        {
            get => ReadChildDouble("diameter");
            set => WriteChildDouble("diameter", value);
        }

        /// <summary>Gets the colour components as written, or an empty list when there is no colour.</summary>
        public IReadOnlyList<string> Color
        {
            get
            {
                var color = Node.GetChild("color");
                return color is null ? Array.Empty<string>() : color.Values.ToArray();
            }
        }

        /// <summary>Gets or sets the junction's UUID.</summary>
        public string Uuid
        {
            get => ReadChild("uuid") ?? string.Empty;
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }
    }

    /// <summary>
    /// The cross that marks a pin as deliberately unconnected: <c>(no_connect (at x y) (uuid ...))</c>.
    /// </summary>
    /// <remarks>
    /// It carries no net and no text; it exists so the electrical rules checker stays quiet about a
    /// pin the designer meant to leave open.
    /// </remarks>
    public sealed class KiCadNoConnect : KiCadNode
    {
        /// <summary>Creates a view over a <c>(no_connect ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadNoConnect(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the pin end the cross sits on.</summary>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild("at"));
            set => value.Write(Require("at"), includeRotation: false);
        }

        /// <summary>Gets or sets the marker's UUID.</summary>
        public string Uuid
        {
            get => ReadChild("uuid") ?? string.Empty;
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }
    }

    /// <summary>
    /// Anything that names a net by sitting on it: the shared shape of <c>label</c>,
    /// <c>global_label</c>, <c>hierarchical_label</c> and <c>netclass_flag</c>.
    /// </summary>
    /// <remarks>
    /// All four are <c>(token "TEXT" (at x y r) (effects ...) (uuid ...))</c>. What differs is
    /// scope, not shape: a plain label is local to its sheet, a global label joins every sheet, a
    /// hierarchical label is the sheet's half of a <see cref="KiCadSheetPin"/>, and a netclass flag
    /// names a rule set rather than a net.
    /// </remarks>
    public abstract class KiCadSchematicLabel : KiCadNode
    {
        /// <summary>Creates a view over a label-shaped form.</summary>
        /// <param name="node">The form.</param>
        protected KiCadSchematicLabel(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the text, which for the three label kinds <em>is</em> the net name.</summary>
        public string Text
        {
            get => Node.GetValue(0) ?? string.Empty;
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets where the label sits and which way it points.</summary>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild("at"));
            set => value.Write(Require("at"), includeRotation: true);
        }

        /// <summary>Gets the text rendering, creating an <c>(effects ...)</c> if there is none.</summary>
        public KiCadFontEffects? FontEffects => Node.GetChild("effects") is { } node ? new KiCadFontEffects(node) : null;

        /// <summary>Gets the <c>(effects ...)</c> form, adding an empty one when the node has none.</summary>
        /// <returns>The view.</returns>
        public KiCadFontEffects RequireFontEffects() => new(Require("effects"));

        /// <summary>Gets or sets the label's UUID.</summary>
        public string Uuid
        {
            get => ReadChild("uuid") ?? string.Empty;
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }
    }

    /// <summary>A sheet-local net name: <c>(label "NAME" (at x y r) (effects ...) (uuid ...))</c>.</summary>
    public sealed class KiCadLabel : KiCadSchematicLabel
    {
        /// <summary>Creates a view over a <c>(label ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadLabel(SExpression node)
            : base(node)
        {
        }
    }

    /// <summary>
    /// A label that also draws a direction and carries fields: <c>global_label</c>,
    /// <c>hierarchical_label</c> and <c>netclass_flag</c>.
    /// </summary>
    /// <remarks>
    /// The <c>(shape ...)</c> is the arrow KiCad draws — <c>input</c>, <c>output</c>,
    /// <c>bidirectional</c>, <c>tri_state</c>, <c>passive</c> — and it is documentation, not
    /// connectivity: two global labels of the same name join whatever their shapes say.
    /// </remarks>
    public abstract class KiCadShapedLabel : KiCadSchematicLabel
    {
        /// <summary>Creates a view over a shaped label form.</summary>
        /// <param name="node">The form.</param>
        protected KiCadShapedLabel(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the arrow shape drawn around the text.</summary>
        public string Shape
        {
            get => ReadChild("shape") ?? "input";
            set => WriteChild("shape", value, SQuoteStyle.Bare);
        }

        /// <summary>
        /// Gets the label's fields. A global label carries an <c>Intersheetrefs</c> property holding
        /// the page numbers KiCad prints beside it.
        /// </summary>
        public KiCadNodeList<KiCadProperty> Properties => new(Node, "property", n => new KiCadProperty(n));

        /// <summary>Gets the value of a named field.</summary>
        /// <param name="key">The field key.</param>
        /// <returns>The value, or <see langword="null"/>.</returns>
        public string? GetPropertyValue(string key) =>
            Properties.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal))?.Value;
    }

    /// <summary>
    /// A net name shared by every sheet in the design:
    /// <c>(global_label "NAME" (shape input) (at ...) (effects ...) (uuid ...) (property ...))</c>.
    /// </summary>
    public sealed class KiCadGlobalLabel : KiCadShapedLabel
    {
        /// <summary>Creates a view over a <c>(global_label ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadGlobalLabel(SExpression node)
            : base(node)
        {
        }
    }

    /// <summary>
    /// The sheet-side half of a hierarchical connection:
    /// <c>(hierarchical_label "NAME" (shape input) (at ...) (effects ...) (uuid ...))</c>.
    /// </summary>
    /// <remarks>
    /// It only connects upward if the parent's <see cref="KiCadSheetPin"/> has the same name, which
    /// is why the two types read the same way.
    /// </remarks>
    public sealed class KiCadHierarchicalLabel : KiCadShapedLabel
    {
        /// <summary>Creates a view over a <c>(hierarchical_label ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadHierarchicalLabel(SExpression node)
            : base(node)
        {
        }
    }

    /// <summary>
    /// A flag that assigns a net class rather than a name:
    /// <c>(netclass_flag "NAME" (length l) (shape round) (at ...) (effects ...) (uuid ...))</c>.
    /// </summary>
    public sealed class KiCadNetClassFlag : KiCadShapedLabel
    {
        /// <summary>Creates a view over a <c>(netclass_flag ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadNetClassFlag(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the length of the stem drawn from the net to the flag, in millimetres.</summary>
        public double Length
        {
            get => ReadChildDouble("length", 0, 2.54);
            set => WriteChildDouble("length", value);
        }
    }

    /// <summary>
    /// Free text drawn on a sheet: <c>(text "..." (at x y r) (effects ...) (uuid ...))</c>.
    /// </summary>
    /// <remarks>
    /// The same token as the text inside a symbol, so this is the symbol view plus the two things a
    /// sheet adds: a UUID, and the simulator exclusion flag KiCad 8 introduced.
    /// </remarks>
    public sealed class KiCadSchematicText : KiCadText
    {
        /// <summary>Creates a view over a <c>(text ...)</c> form on a sheet.</summary>
        /// <param name="node">The form.</param>
        public KiCadSchematicText(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets whether the simulator ignores this text.</summary>
        public bool ExcludeFromSim
        {
            get => ReadFlag("exclude_from_sim");
            set => WriteFlag("exclude_from_sim", value);
        }

        /// <summary>Gets or sets the text's UUID.</summary>
        public string Uuid
        {
            get => ReadChild("uuid") ?? string.Empty;
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }
    }

    /// <summary>
    /// A text box: <c>(text_box "..." (at x y r) (size w h) (stroke ...) (fill ...) (effects ...) (uuid ...))</c>.
    /// </summary>
    /// <remarks>
    /// A graphical item rather than a label — it has a border and a fill, and it names no net. The
    /// stroke and fill come from <see cref="KiCadGraphicalItem"/>, which is where every other bordered
    /// shape gets them.
    /// </remarks>
    public sealed class KiCadTextBox : KiCadGraphicalItem
    {
        /// <summary>Creates a view over a <c>(text_box ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadTextBox(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the text inside the box.</summary>
        public string Text
        {
            get => Node.GetValue(0) ?? string.Empty;
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the top-left corner and the rotation of the box.</summary>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild("at"));
            set => value.Write(Require("at"), includeRotation: true);
        }

        /// <summary>Gets or sets the box's extent, in millimetres.</summary>
        public KiCadSize Size
        {
            get => KiCadSize.Read(Node.GetChild("size"));
            set => value.Write(Require("size"));
        }

        /// <summary>Gets the text rendering, creating an <c>(effects ...)</c> if there is none.</summary>
        public KiCadFontEffects? FontEffects => Node.GetChild("effects") is { } node ? new KiCadFontEffects(node) : null;

        /// <summary>Gets the <c>(effects ...)</c> form, adding an empty one when the node has none.</summary>
        /// <returns>The view.</returns>
        public KiCadFontEffects RequireFontEffects() => new(Require("effects"));

        /// <summary>Gets or sets the box's UUID.</summary>
        public string Uuid
        {
            get => ReadChild("uuid") ?? string.Empty;
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }
    }

    /// <summary>
    /// A cubic Bézier: <c>(bezier (pts (xy x y) x4) (stroke ...) (fill ...) (uuid ...))</c>.
    /// </summary>
    /// <remarks>
    /// The four points are control points, not vertices — the curve passes through the first and the
    /// last and is only pulled towards the middle two — which is why they are not called
    /// <c>Points</c> the way a polyline's are.
    /// </remarks>
    public sealed class KiCadBezier : KiCadGraphicalItem
    {
        /// <summary>Creates a view over a <c>(bezier ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadBezier(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets the control points, in order.</summary>
        public IReadOnlyList<KiCadPosition> ControlPoints =>
            (Node.GetChild("pts")?.GetChildren("xy") ?? Enumerable.Empty<SExpression>())
                .Select(xy => new KiCadPosition(xy.GetValueAsDouble(0), xy.GetValueAsDouble(1)))
                .ToArray();

        /// <summary>Gets or sets the curve's UUID.</summary>
        public string Uuid
        {
            get => ReadChild("uuid") ?? string.Empty;
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }
    }

    /// <summary>
    /// A bitmap placed on a sheet: <c>(image (at x y) (scale s) (uuid ...) (data ...))</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="Data"/> is the base64 payload exactly as the file spells it, joined back into one
    /// string. It is deliberately not decoded: an image is usually the largest thing in a schematic,
    /// and decoding it on every read would cost more than every other view here put together — while
    /// the bytes survive a save whether or not anyone looks at them.
    /// </remarks>
    public sealed class KiCadImage : KiCadNode
    {
        /// <summary>Creates a view over an <c>(image ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadImage(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets where the image's centre sits.</summary>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild("at"));
            set => value.Write(Require("at"), includeRotation: false);
        }

        /// <summary>Gets or sets the scale factor; 1 draws the bitmap at its native size.</summary>
        public double Scale
        {
            get => ReadChildDouble("scale", 0, 1);
            set => WriteChildDouble("scale", value);
        }

        /// <summary>
        /// Gets the base64 PNG payload, with the line breaks KiCad wrote it in removed. Empty when
        /// the form carries no <c>(data ...)</c>.
        /// </summary>
        public string Data => Node.GetChild("data") is { } data ? string.Concat(data.Values) : string.Empty;

        /// <summary>Gets the payload split the way the file splits it, one string per atom.</summary>
        public IReadOnlyList<string> DataAtoms =>
            Node.GetChild("data") is { } data ? data.Values.ToArray() : Array.Empty<string>();

        /// <summary>Gets or sets the image's UUID.</summary>
        public string Uuid
        {
            get => ReadChild("uuid") ?? string.Empty;
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }
    }
}
