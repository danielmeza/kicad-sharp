using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using SExpressions;

namespace KiCadSharp.Documents
{
    /// <summary>
    /// Reads the <c>(net …)</c> child that copper, zones and pads all carry, in both spellings the
    /// board format has used.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Up to and including KiCad 9 — board format <c>20241229</c> — copper points at a net by number:
    /// <c>(segment … (net 1))</c>, with the number resolved against the board-level
    /// <c>(net 1 "GND")</c> table. A pad wrote both, <c>(net 1 "GND")</c>.
    /// </para>
    /// <para>
    /// KiCad 10 — board format <c>20260206</c> — writes the <em>name</em> in that place,
    /// <c>(segment … (net "GND"))</c>, and drops the board-level table entirely. MEASURED against a
    /// board saved by pcbnew 10.0.6: 0 <c>(net …)</c> forms at the top level, and every segment, via,
    /// arc, zone and pad naming its net.
    /// </para>
    /// <para>
    /// The two are told apart by how the atom is written, which the parser records: a code is bare, a
    /// name is quoted. That is the file's own distinction, not a guess about the content — a net
    /// genuinely called <c>5</c> is still <c>(net "5")</c>.
    /// </para>
    /// </remarks>
    internal static class KiCadNetRef
    {
        /// <summary>Reads the net code, or <see langword="null"/> when the form names its net instead.</summary>
        /// <param name="net">The <c>(net …)</c> form, or <see langword="null"/>.</param>
        /// <returns>The code.</returns>
        internal static int? ReadCode(SExpression? net)
        {
            if (net is null || IsQuoted(net, 0))
            {
                return null;
            }

            return net.TryGetValue<int>(0, out var code) ? code : null;
        }

        /// <summary>Reads the net name, or <see langword="null"/> when the form carries only a code.</summary>
        /// <param name="net">The <c>(net …)</c> form, or <see langword="null"/>.</param>
        /// <returns>The name.</returns>
        internal static string? ReadName(SExpression? net)
        {
            if (net is null)
            {
                return null;
            }

            // (net 1 "GND") — a pad up to KiCad 9 carries both, name second.
            if (net.GetValue(1) is { } second)
            {
                return second;
            }

            // (net "GND") — KiCad 10 writes the name where the code used to be.
            return IsQuoted(net, 0) ? net.GetValue(0) : null;
        }

        /// <summary>Writes a net name into whichever slot the form already keeps it in.</summary>
        /// <param name="net">The <c>(net …)</c> form.</param>
        /// <param name="name">The name.</param>
        internal static void WriteName(SExpression net, string name) =>
            net.SetValue(NameIndex(net), name, SQuoteStyle.Quoted);

        /// <summary>The value index a name belongs at: 0 when the form names its net, 1 when it numbers it.</summary>
        /// <param name="net">The <c>(net …)</c> form.</param>
        /// <returns>The index.</returns>
        internal static int NameIndex(SExpression net) => ReadCode(net) is null ? 0 : 1;

        private static bool IsQuoted(SExpression node, int index)
        {
            var seen = 0;
            foreach (var item in node.Items)
            {
                if (item.Kind != SItemKind.Atom)
                {
                    continue;
                }

                if (seen++ == index)
                {
                    return item.QuoteStyle == SQuoteStyle.Quoted;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Something on a board that belongs to a net and carries a UUID: a track segment, a track arc,
    /// or a via.
    /// </summary>
    /// <remarks>
    /// How copper points at its net changed with the board format, so both <see cref="Net"/> — the
    /// KiCad 9 code, resolved through <see cref="KiCadBoard.GetNet(int)"/> — and
    /// <see cref="NetName"/> — the KiCad 10 name, written on the item itself — are here, and exactly
    /// one of them answers on any given file. See <see cref="KiCadNetRef"/> for the measurement.
    /// </remarks>
    public abstract class KiCadTrackItem : KiCadNode
    {
        /// <summary>Creates a view over the form.</summary>
        /// <param name="node">The form.</param>
        protected KiCadTrackItem(SExpression node)
            : base(node)
        {
        }

        /// <summary>
        /// Gets or sets the net code, 0 for copper that belongs to no net — and also 0 on a KiCad 10
        /// board, which writes no codes at all. <see cref="NetName"/> is what answers there.
        /// </summary>
        public int Net
        {
            get => KiCadNetRef.ReadCode(Node.GetChild("net")) ?? 0;
            set => Node.SetChildValue("net", value.ToString(CultureInfo.InvariantCulture), SQuoteStyle.Bare);
        }

        /// <summary>
        /// Gets the net name the item carries — KiCad 10's <c>(net "GND")</c> — or
        /// <see langword="null"/> on a file that numbers its nets instead, where the name lives in
        /// the board's net table and <see cref="KiCadBoard.GetNet(int)"/> reaches it.
        /// </summary>
        public string? NetName => KiCadNetRef.ReadName(Node.GetChild("net"));

        /// <summary>
        /// Gets or sets the item's UUID, which is how a <see cref="KiCadGroup"/> refers to it.
        /// <see langword="null"/> on a file old enough to have used timestamps instead.
        /// </summary>
        public string? Uuid
        {
            get => ReadChild("uuid");
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets whether the item is locked against being moved or rerouted.</summary>
        public bool Locked
        {
            get => ReadFlag("locked");
            set => WriteFlag("locked", value);
        }
    }

    /// <summary>
    /// A straight piece of track:
    /// <c>(segment (start x y) (end x y) (width w) (layer "F.Cu") (net n) (uuid "…"))</c>.
    /// </summary>
    public sealed class KiCadTrackSegment : KiCadTrackItem
    {
        /// <summary>Creates a view over a <c>(segment …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadTrackSegment(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty segment.</summary>
        public KiCadTrackSegment()
            : base(new SExpression("segment"))
        {
        }

        /// <summary>Gets or sets the start point.</summary>
        public KiCadPosition Start
        {
            get => KiCadPosition.Read(Node.GetChild("start"));
            set => value.Write(Require("start"), includeRotation: false);
        }

        /// <summary>Gets or sets the end point.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild("end"));
            set => value.Write(Require("end"), includeRotation: false);
        }

        /// <summary>Gets or sets the track width in millimetres.</summary>
        public double Width
        {
            get => ReadChildDouble("width", 0, 0.2);
            set => WriteChildDouble("width", value);
        }

        /// <summary>Gets or sets the copper layer the track runs on.</summary>
        public string Layer
        {
            get => ReadChild("layer") ?? "F.Cu";
            set => WriteChild("layer", value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets the straight-line length of the segment, in millimetres.</summary>
        public double Length
        {
            get
            {
                var start = Start;
                var end = End;
                return Math.Sqrt(((end.X - start.X) * (end.X - start.X)) + ((end.Y - start.Y) * (end.Y - start.Y)));
            }
        }
    }

    /// <summary>
    /// A curved piece of track:
    /// <c>(arc (start x y) (mid x y) (end x y) (width w) (layer "F.Cu") (net n))</c>.
    /// </summary>
    /// <remarks>
    /// The token is <c>arc</c>, not <c>gr_arc</c>: this is copper on a net, not a drawing. The mid
    /// point is a point the arc passes through rather than a centre and an angle, which is what lets
    /// KiCad round-trip it without accumulating error.
    /// </remarks>
    public sealed class KiCadTrackArc : KiCadTrackItem
    {
        /// <summary>Creates a view over an <c>(arc …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadTrackArc(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty track arc.</summary>
        public KiCadTrackArc()
            : base(new SExpression("arc"))
        {
        }

        /// <summary>Gets or sets the start point.</summary>
        public KiCadPosition Start
        {
            get => KiCadPosition.Read(Node.GetChild("start"));
            set => value.Write(Require("start"), includeRotation: false);
        }

        /// <summary>Gets or sets the point the arc passes through.</summary>
        public KiCadPosition Mid
        {
            get => KiCadPosition.Read(Node.GetChild("mid"));
            set => value.Write(Require("mid"), includeRotation: false);
        }

        /// <summary>Gets or sets the end point.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild("end"));
            set => value.Write(Require("end"), includeRotation: false);
        }

        /// <summary>Gets or sets the track width in millimetres.</summary>
        public double Width
        {
            get => ReadChildDouble("width", 0, 0.2);
            set => WriteChildDouble("width", value);
        }

        /// <summary>Gets or sets the copper layer the track runs on.</summary>
        public string Layer
        {
            get => ReadChild("layer") ?? "F.Cu";
            set => WriteChild("layer", value, SQuoteStyle.Quoted);
        }
    }

    /// <summary>
    /// A via: <c>(via (at x y) (size s) (drill d) (layers "F.Cu" "B.Cu") (net n) (uuid "…"))</c>,
    /// with an optional bare type before the children — <c>(via micro (at …) …)</c>.
    /// </summary>
    public sealed class KiCadVia : KiCadTrackItem
    {
        /// <summary>Creates a view over a <c>(via …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadVia(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty via.</summary>
        public KiCadVia()
            : base(new SExpression("via"))
        {
        }

        /// <summary>
        /// Gets or sets the via type: <c>through</c>, <c>blind</c> or <c>micro</c>.
        /// </summary>
        /// <remarks>
        /// KiCad writes the type as a bare value before the children and omits it entirely for a
        /// through via, which is the overwhelmingly common case. Setting this back to
        /// <c>through</c> therefore removes the value rather than writing the word, so a board that
        /// round-trips through this property is spelled the way KiCad spells it.
        /// </remarks>
        public string ViaType
        {
            get => Node.GetValue(0) is { } value && IsViaType(value) ? value : "through";

            set
            {
                ArgumentNullException.ThrowIfNull(value);
                var hasBareType = Node.GetValue(0) is { } current && IsViaType(current);

                if (string.Equals(value, "through", StringComparison.Ordinal))
                {
                    if (hasBareType)
                    {
                        Node.Values.RemoveAt(0);
                    }

                    return;
                }

                if (hasBareType)
                {
                    WriteValue(0, value, SQuoteStyle.Bare);
                }
                else
                {
                    Node.Values.Insert(0, value);
                }
            }
        }

        /// <summary>Gets or sets where the via sits.</summary>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild("at"));
            set => value.Write(Require("at"), includeRotation: false);
        }

        /// <summary>Gets or sets the annular pad diameter in millimetres.</summary>
        public double Size
        {
            get => ReadChildDouble("size", 0, 0.6);
            set => WriteChildDouble("size", value);
        }

        /// <summary>Gets or sets the hole diameter in millimetres.</summary>
        public double Drill
        {
            get => ReadChildDouble("drill", 0, 0.3);
            set => WriteChildDouble("drill", value);
        }

        /// <summary>
        /// Gets or sets the two layers the via spans, outermost first. A through via still names
        /// them, so the pair is not redundant with <see cref="ViaType"/>.
        /// </summary>
        public IReadOnlyList<string> Layers
        {
            get
            {
                var layers = Node.GetChild("layers");
                return layers is null ? Array.Empty<string>() : layers.Values.ToArray();
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);
                var layers = Require("layers");
                layers.Values.Clear();
                foreach (var layer in value)
                {
                    layers.Values.Add(layer, SQuoteStyle.Quoted);
                }
            }
        }

        /// <summary>Gets or sets whether the via may be moved off its net by the router.</summary>
        public bool Free
        {
            get => ReadFlag("free");
            set => WriteFlag("free", value);
        }

        /// <summary>Gets or sets whether KiCad may drop the annular ring on layers the via does not connect.</summary>
        public bool RemoveUnusedLayers
        {
            get => ReadFlag("remove_unused_layers");
            set => WriteFlag("remove_unused_layers", value);
        }

        private static bool IsViaType(string value) =>
            string.Equals(value, "through", StringComparison.Ordinal)
            || string.Equals(value, "blind", StringComparison.Ordinal)
            || string.Equals(value, "micro", StringComparison.Ordinal);
    }

    /// <summary>
    /// A zone: a copper pour, or — when it carries a <c>(keepout …)</c> form — a rule area that
    /// pours nothing and forbids things instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A zone holds two different polygons and confusing them is the classic bug. <see cref="Points"/>
    /// is the outline the designer drew; <see cref="FilledPolygons"/> is what the filler computed
    /// from it, per layer, after clearances and thermal reliefs were applied. The second is derived
    /// and is absent until the board has been filled — <see cref="IsFilled"/> says which state the
    /// file is in.
    /// </para>
    /// <para>
    /// The layer list is spelled two ways: <c>(layer "F.Cu")</c> for a single-layer zone and
    /// <c>(layers "F.Cu" "In1.Cu")</c> for a multi-layer one. <see cref="Layers"/> reads both and
    /// writes back into whichever the file already uses.
    /// </para>
    /// </remarks>
    public sealed class KiCadZone : KiCadNode
    {
        /// <summary>Creates a view over a <c>(zone …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadZone(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty zone.</summary>
        public KiCadZone()
            : base(new SExpression("zone"))
        {
        }

        /// <summary>
        /// Gets or sets the net code the zone pours, 0 for a rule area — and also 0 on a KiCad 10
        /// board, which names its nets rather than numbering them. <see cref="NetName"/> answers there.
        /// </summary>
        public int Net
        {
            get => KiCadNetRef.ReadCode(Node.GetChild("net")) ?? 0;
            set => Node.SetChildValue("net", value.ToString(CultureInfo.InvariantCulture), SQuoteStyle.Bare);
        }

        /// <summary>
        /// Gets or sets the net name, in whichever place this file keeps it: KiCad 9 wrote it beside
        /// the code as <c>(net 1) (net_name "GND")</c>, so a zone was the one place a net was spelled
        /// twice; KiCad 10 writes <c>(net "GND")</c> and no <c>net_name</c> at all. A set goes back
        /// into the spelling the node already uses rather than adding the other one.
        /// </summary>
        public string NetName
        {
            get => ReadChild("net_name") ?? KiCadNetRef.ReadName(Node.GetChild("net")) ?? string.Empty;

            set
            {
                if (Node.GetChild("net_name") is null && Node.GetChild("net") is { } net && KiCadNetRef.ReadName(net) is not null)
                {
                    KiCadNetRef.WriteName(net, value);
                    return;
                }

                WriteChild("net_name", value, SQuoteStyle.Quoted);
            }
        }

        /// <summary>Gets or sets the layers the zone pours on.</summary>
        public IReadOnlyList<string> Layers
        {
            get
            {
                if (Node.GetChild("layers") is { } layers)
                {
                    return layers.Values.ToArray();
                }

                return Node.GetChild("layer")?.GetValue(0) is { } single ? new[] { single } : Array.Empty<string>();
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);
                var names = value.ToArray();

                // Keep the file's own spelling: a one-layer zone written as (layer "F.Cu") stays that
                // way, and only grows into (layers …) when it really needs more than one.
                if (Node.GetChild("layers") is null && names.Length <= 1 && Node.GetChild("layer") is { } single)
                {
                    single.SetValue(0, names.Length == 0 ? string.Empty : names[0], SQuoteStyle.Quoted);
                    return;
                }

                Node.RemoveChild("layer");
                var layers = Require("layers");
                layers.Values.Clear();
                foreach (var name in names)
                {
                    layers.Values.Add(name, SQuoteStyle.Quoted);
                }
            }
        }

        /// <summary>Gets or sets the zone's UUID.</summary>
        public string? Uuid
        {
            get => ReadChild("uuid");
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the zone's name, which the designer types and the design rules match on.</summary>
        public string? Name
        {
            get => ReadChild("name");
            set => WriteChild("name", value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets or sets the fill priority. Higher wins: where two zones overlap, the one with the
        /// larger priority pours and the other is clipped. KiCad omits the token when it is 0.
        /// </summary>
        public int Priority
        {
            get => Node.GetChild("priority") is { } priority && priority.TryGetValue<int>(0, out var value) ? value : 0;
            set => Node.SetChildValue("priority", value.ToString(CultureInfo.InvariantCulture), SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets how the zone outline is drawn on screen: <c>none</c>, <c>edge</c> or <c>full</c>.</summary>
        public string HatchStyle
        {
            get => Node.GetChild("hatch")?.GetValue(0) ?? "none";
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                Require("hatch").SetValue(0, value, SQuoteStyle.Bare);
            }
        }

        /// <summary>Gets or sets the spacing of the outline hatching, in millimetres.</summary>
        public double HatchPitch
        {
            get => Node.GetChild("hatch")?.GetValueAsDouble(1) ?? 0;
            set => Require("hatch").SetValue(1, Numbers.Format(value), SQuoteStyle.Bare);
        }

        /// <summary>
        /// Gets or sets how pads connect to the pour: <c>yes</c> for solid, <c>no</c> for none,
        /// <c>thru_hole_only</c>, or the empty string for KiCad's default of thermal reliefs, which
        /// it writes as a bare <c>(connect_pads (clearance …))</c> with no mode at all.
        /// </summary>
        public string ConnectPadsMode
        {
            get => Node.GetChild("connect_pads")?.GetValue(0) ?? string.Empty;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                Require("connect_pads").SetValue(0, value, SQuoteStyle.Bare);
            }
        }

        /// <summary>Gets or sets the gap left around a pad that connects to the pour, in millimetres.</summary>
        public double ConnectPadsClearance
        {
            get
            {
                var clearance = Node.GetChild("connect_pads")?.GetChild("clearance");
                return clearance is not null && clearance.TryGetValue<double>(0, out var value) ? value : 0;
            }

            set => Require("connect_pads").SetChildValue("clearance", Numbers.Format(value), SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets the narrowest copper the filler may leave, in millimetres.</summary>
        public double MinThickness
        {
            get => ReadChildDouble("min_thickness", 0, 0.25);
            set => WriteChildDouble("min_thickness", value);
        }

        /// <summary>
        /// Gets or sets whether the filled polygons were computed with the KiCad 5 outline thickness
        /// applied. KiCad writes it on every zone so that reopening an old board cannot silently
        /// change how much copper is there.
        /// </summary>
        public bool FilledAreasThickness
        {
            get => ReadFlag("filled_areas_thickness");
            set => WriteFlag("filled_areas_thickness", value);
        }

        /// <summary>Gets the fill settings, or <see langword="null"/> when the zone has no <c>fill</c> form.</summary>
        public KiCadZoneFill? Fill => Node.GetChild("fill") is { } fill ? new KiCadZoneFill(fill) : null;

        /// <summary>
        /// True when the board was saved filled: <c>(fill yes …)</c>. A zone that has an outline but
        /// no computed copper reads false, and its <see cref="FilledPolygons"/> is empty.
        /// </summary>
        public bool IsFilled => Fill?.Enabled ?? false;

        /// <summary>True when the zone is a rule area — it pours nothing and carries a <c>(keepout …)</c> form.</summary>
        public bool IsKeepout => Node.GetChild("keepout") is not null;

        /// <summary>Gets the outline the designer drew, in order: the <c>(polygon (pts (xy …)))</c> form.</summary>
        public IReadOnlyList<KiCadPosition> Points =>
            ReadPoints(Node.GetChild("polygon"));

        /// <summary>
        /// Gets the copper the filler computed, one entry per layer poured. Empty until the board is
        /// filled; see <see cref="IsFilled"/>.
        /// </summary>
        public KiCadNodeList<KiCadZoneFilledPolygon> FilledPolygons =>
            new(Node, "filled_polygon", n => new KiCadZoneFilledPolygon(n));

        /// <summary>Gets the fill settings, adding a <c>(fill …)</c> form when the zone has none.</summary>
        /// <returns>The view.</returns>
        public KiCadZoneFill RequireFill() => new(Require("fill"));

        /// <summary>Appends a vertex to the outline, creating the <c>polygon</c> form when there is none.</summary>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        public void AddPoint(double x, double y)
        {
            var polygon = Node.GetChild("polygon") ?? Node.CreateChild("polygon");
            var points = polygon.GetChild("pts") ?? polygon.CreateChild("pts");
            points.CreateChild("xy", Numbers.Format(x), Numbers.Format(y));
        }

        internal static IReadOnlyList<KiCadPosition> ReadPoints(SExpression? owner) =>
            (owner?.GetChild("pts")?.GetChildren("xy") ?? Enumerable.Empty<SExpression>())
                .Select(xy => new KiCadPosition(xy.GetValueAsDouble(0), xy.GetValueAsDouble(1)))
                .ToArray();
    }

    /// <summary>
    /// A zone's fill settings: <c>(fill yes (thermal_gap 0.5) (thermal_bridge_width 0.5))</c>.
    /// </summary>
    public sealed class KiCadZoneFill : KiCadNode
    {
        /// <summary>Creates a view over a zone's <c>(fill …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadZoneFill(SExpression node)
            : base(node)
        {
        }

        /// <summary>
        /// Gets or sets whether the zone is filled, the bare <c>yes</c> KiCad writes before the
        /// children. A rule area and an unfilled zone both leave it out.
        /// </summary>
        public bool Enabled
        {
            get => string.Equals(Node.GetValue(0), "yes", StringComparison.Ordinal);
            set
            {
                if (value)
                {
                    if (!string.Equals(Node.GetValue(0), "yes", StringComparison.Ordinal))
                    {
                        Node.Values.Insert(0, "yes");
                    }

                    return;
                }

                if (string.Equals(Node.GetValue(0), "yes", StringComparison.Ordinal))
                {
                    Node.Values.RemoveAt(0);
                }
            }
        }

        /// <summary>Gets or sets the fill pattern: <c>solid</c> when the token is absent, or <c>hatch</c>.</summary>
        public string Mode
        {
            get => ReadChild("mode") ?? "solid";
            set => WriteChild("mode", value, SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets the gap a thermal relief leaves around a pad, in millimetres.</summary>
        public double ThermalGap
        {
            get => ReadChildDouble("thermal_gap");
            set => WriteChildDouble("thermal_gap", value);
        }

        /// <summary>Gets or sets the width of each spoke of a thermal relief, in millimetres.</summary>
        public double ThermalBridgeWidth
        {
            get => ReadChildDouble("thermal_bridge_width");
            set => WriteChildDouble("thermal_bridge_width", value);
        }
    }

    /// <summary>
    /// One layer's worth of computed copper:
    /// <c>(filled_polygon (layer "F.Cu") (pts (xy …)))</c>.
    /// </summary>
    /// <remarks>
    /// This is the filler's output, not the designer's input. Editing it is editing a cached result
    /// — KiCad will overwrite it the next time the board is filled.
    /// </remarks>
    public sealed class KiCadZoneFilledPolygon : KiCadNode
    {
        /// <summary>Creates a view over a <c>(filled_polygon …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadZoneFilledPolygon(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the layer this copper is on.</summary>
        public string Layer
        {
            get => ReadChild("layer") ?? "F.Cu";
            set => WriteChild("layer", value, SQuoteStyle.Quoted);
        }

        /// <summary>True when the polygon is a hole in the pour rather than copper.</summary>
        public bool IsIsland => Node.GetChild("island") is not null;

        /// <summary>Gets the outline of the computed copper, in order.</summary>
        public IReadOnlyList<KiCadPosition> Points => KiCadZone.ReadPoints(Node);
    }

    /// <summary>
    /// A board-level drawing that sits on a layer and has a stroke: <c>gr_line</c>, <c>gr_rect</c>,
    /// <c>gr_circle</c>, <c>gr_arc</c>, <c>gr_poly</c>, <c>gr_curve</c>.
    /// </summary>
    /// <remarks>
    /// The same shapes as <see cref="KiCadFpItem"/>, one level up: these belong to the board rather
    /// than to a footprint, which is the whole difference between <c>gr_line</c> and <c>fp_line</c>.
    /// Unlike <see cref="KiCadFpItem.Stroke"/>, reading <see cref="Stroke"/> here never adds a form
    /// the file does not have — see <see cref="RequireStroke"/>.
    /// </remarks>
    public abstract class KiCadGraphicItem : KiCadNode
    {
        /// <summary>Creates a view over the form.</summary>
        /// <param name="node">The form.</param>
        protected KiCadGraphicItem(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the layer the drawing is on.</summary>
        public string Layer
        {
            get => ReadChild("layer") ?? "F.SilkS";
            set => WriteChild("layer", value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets or sets the stroke width. KiCad 7+ writes <c>(stroke (width w) (type t))</c>; KiCad 5
        /// and 6 wrote a bare <c>(width w)</c>, and both are read here.
        /// </summary>
        public double Width
        {
            get
            {
                if (Node.GetChild("stroke")?.GetChild("width") is { } strokeWidth && strokeWidth.TryGetValue<double>(0, out var fromStroke))
                {
                    return fromStroke;
                }

                return ReadChildDouble("width", 0, 0.1);
            }

            set
            {
                if (Node.GetChild("stroke") is { } stroke)
                {
                    stroke.SetChildValue("width", Numbers.Format(value), SQuoteStyle.Bare);
                    return;
                }

                WriteChildDouble("width", value);
            }
        }

        /// <summary>Gets the stroke, or <see langword="null"/> on a file that writes a bare <c>(width w)</c>.</summary>
        public KiCadStroke? Stroke => Node.GetChild("stroke") is { } stroke ? new KiCadStroke(stroke) : null;

        /// <summary>Gets or sets the drawing's UUID.</summary>
        public string? Uuid
        {
            get => ReadChild("uuid");
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets whether the drawing is locked against being moved.</summary>
        public bool Locked
        {
            get => ReadFlag("locked");
            set => WriteFlag("locked", value);
        }

        /// <summary>Gets the stroke, adding a <c>(stroke …)</c> form when the drawing has none.</summary>
        /// <returns>The view.</returns>
        public KiCadStroke RequireStroke() => new(Require("stroke"));

        /// <summary>Gets the fill, or <see langword="null"/> when the shape has no <c>fill</c> form.</summary>
        /// <remarks>An open shape — a line, an arc, a curve — has none, and reading this does not give it one.</remarks>
        public KiCadFill? Fill => Node.GetChild("fill") is { } fill ? new KiCadFill(fill) : null;

        /// <summary>Gets the fill, adding a <c>(fill …)</c> form when the shape has none.</summary>
        /// <returns>The view.</returns>
        public KiCadFill RequireFill() => new(Require("fill"));
    }

    /// <summary>A board line: <c>(gr_line (start x y) (end x y) (stroke …) (layer "Edge.Cuts"))</c>.</summary>
    public sealed class KiCadGrLine : KiCadGraphicItem
    {
        /// <summary>Creates a view over a <c>(gr_line …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadGrLine(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty line.</summary>
        public KiCadGrLine()
            : base(new SExpression("gr_line"))
        {
        }

        /// <summary>Gets or sets the start point.</summary>
        public KiCadPosition Start
        {
            get => KiCadPosition.Read(Node.GetChild("start"));
            set => value.Write(Require("start"), includeRotation: false);
        }

        /// <summary>Gets or sets the end point.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild("end"));
            set => value.Write(Require("end"), includeRotation: false);
        }
    }

    /// <summary>A board rectangle: <c>(gr_rect (start x y) (end x y) (stroke …) (fill no) (layer "Edge.Cuts"))</c>.</summary>
    /// <remarks>A board outline is usually one of these on <c>Edge.Cuts</c>, which is why it matters that it is a shape and not four lines.</remarks>
    public sealed class KiCadGrRect : KiCadGraphicItem
    {
        /// <summary>Creates a view over a <c>(gr_rect …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadGrRect(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty rectangle.</summary>
        public KiCadGrRect()
            : base(new SExpression("gr_rect"))
        {
        }

        /// <summary>Gets or sets the first corner.</summary>
        public KiCadPosition Start
        {
            get => KiCadPosition.Read(Node.GetChild("start"));
            set => value.Write(Require("start"), includeRotation: false);
        }

        /// <summary>Gets or sets the opposite corner.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild("end"));
            set => value.Write(Require("end"), includeRotation: false);
        }

        /// <summary>Gets the width of the rectangle in millimetres, corner to corner.</summary>
        public double Width2D => Math.Abs(End.X - Start.X);

        /// <summary>Gets the height of the rectangle in millimetres, corner to corner.</summary>
        public double Height2D => Math.Abs(End.Y - Start.Y);
    }

    /// <summary>A board circle: <c>(gr_circle (center x y) (end x y) (stroke …) (layer "…"))</c>.</summary>
    public sealed class KiCadGrCircle : KiCadGraphicItem
    {
        /// <summary>Creates a view over a <c>(gr_circle …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadGrCircle(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty circle.</summary>
        public KiCadGrCircle()
            : base(new SExpression("gr_circle"))
        {
        }

        /// <summary>Gets or sets the centre.</summary>
        public KiCadPosition Center
        {
            get => KiCadPosition.Read(Node.GetChild("center"));
            set => value.Write(Require("center"), includeRotation: false);
        }

        /// <summary>Gets or sets a point on the circumference; KiCad stores that rather than a radius.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild("end"));
            set => value.Write(Require("end"), includeRotation: false);
        }

        /// <summary>Gets the radius in millimetres, the distance from the centre to the stored point.</summary>
        public double Radius
        {
            get
            {
                var center = Center;
                var end = End;
                return Math.Sqrt(((end.X - center.X) * (end.X - center.X)) + ((end.Y - center.Y) * (end.Y - center.Y)));
            }
        }
    }

    /// <summary>A board arc: <c>(gr_arc (start x y) (mid x y) (end x y) (stroke …) (layer "…"))</c>.</summary>
    public sealed class KiCadGrArc : KiCadGraphicItem
    {
        /// <summary>Creates a view over a <c>(gr_arc …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadGrArc(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty arc.</summary>
        public KiCadGrArc()
            : base(new SExpression("gr_arc"))
        {
        }

        /// <summary>Gets or sets the start point.</summary>
        public KiCadPosition Start
        {
            get => KiCadPosition.Read(Node.GetChild("start"));
            set => value.Write(Require("start"), includeRotation: false);
        }

        /// <summary>Gets or sets the point the arc passes through.</summary>
        public KiCadPosition Mid
        {
            get => KiCadPosition.Read(Node.GetChild("mid"));
            set => value.Write(Require("mid"), includeRotation: false);
        }

        /// <summary>Gets or sets the end point.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild("end"));
            set => value.Write(Require("end"), includeRotation: false);
        }

        /// <summary>Gets or sets the KiCad 5 sweep angle, or 0 on a file that stores a mid point instead.</summary>
        public double Angle
        {
            get => ReadChildDouble("angle");
            set => WriteChildDouble("angle", value);
        }
    }

    /// <summary>A board polygon: <c>(gr_poly (pts (xy x y) …) (stroke …) (fill …) (layer "…"))</c>.</summary>
    public sealed class KiCadGrPoly : KiCadGraphicItem
    {
        /// <summary>Creates a view over a <c>(gr_poly …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadGrPoly(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty polygon.</summary>
        public KiCadGrPoly()
            : base(new SExpression("gr_poly"))
        {
        }

        /// <summary>Gets the vertices, in order.</summary>
        public IReadOnlyList<KiCadPosition> Points => KiCadZone.ReadPoints(Node);

        /// <summary>Appends a vertex.</summary>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        public void AddPoint(double x, double y)
        {
            var points = Node.GetChild("pts") ?? Node.CreateChild("pts");
            points.CreateChild("xy", Numbers.Format(x), Numbers.Format(y));
        }
    }

    /// <summary>A board Bézier curve: <c>(gr_curve (pts (xy …) (xy …) (xy …) (xy …)) (stroke …) (layer "…"))</c>.</summary>
    /// <remarks>The four points are a cubic Bézier: start, two control points, end — not a polyline.</remarks>
    public sealed class KiCadGrCurve : KiCadGraphicItem
    {
        /// <summary>Creates a view over a <c>(gr_curve …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadGrCurve(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty curve.</summary>
        public KiCadGrCurve()
            : base(new SExpression("gr_curve"))
        {
        }

        /// <summary>Gets the four control points, in order.</summary>
        public IReadOnlyList<KiCadPosition> Points => KiCadZone.ReadPoints(Node);

        /// <summary>Appends a control point.</summary>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        public void AddPoint(double x, double y)
        {
            var points = Node.GetChild("pts") ?? Node.CreateChild("pts");
            points.CreateChild("xy", Numbers.Format(x), Numbers.Format(y));
        }
    }

    /// <summary>
    /// Board text: <c>(gr_text "…" (at x y rot) (layer "F.SilkS") (uuid "…") (effects …))</c>.
    /// </summary>
    /// <remarks>
    /// The text is the form's first value, not a child, which is why it is <see cref="Text"/> rather
    /// than a token — the same shape as <see cref="KiCadFpText"/> without the leading kind.
    /// </remarks>
    public sealed class KiCadGrText : KiCadNode
    {
        /// <summary>Creates a view over a <c>(gr_text …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadGrText(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty text item.</summary>
        public KiCadGrText()
            : base(new SExpression("gr_text"))
        {
        }

        /// <summary>Creates a text item.</summary>
        /// <param name="text">The text.</param>
        /// <param name="position">Where the text sits.</param>
        /// <param name="layer">The layer to draw it on.</param>
        public KiCadGrText(string text, KiCadPosition position, string layer)
            : base(new SExpression("gr_text"))
        {
            ArgumentNullException.ThrowIfNull(text);
            Node.AddValue(text, SQuoteStyle.Quoted);
            Position = position;
            Layer = layer;
        }

        /// <summary>Gets or sets the text, which may hold <c>${VARIABLE}</c> references KiCad expands when plotting.</summary>
        public string Text
        {
            get => Node.GetValue(0) ?? string.Empty;
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets where the text sits, including its rotation.</summary>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild("at"));
            set => value.Write(Require("at"), includeRotation: true);
        }

        /// <summary>Gets or sets the layer.</summary>
        public string Layer
        {
            get => ReadChild("layer") ?? "F.SilkS";
            set => WriteChild("layer", value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the text's UUID.</summary>
        public string? Uuid
        {
            get => ReadChild("uuid");
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets whether the text is cut out of the copper or silk around it rather than drawn on it.</summary>
        public bool KnockOut
        {
            get => ReadFlag("knockout");
            set => WriteFlag("knockout", value);
        }

        /// <summary>Gets the text rendering, or <see langword="null"/> when the file leaves it to KiCad's defaults.</summary>
        public KiCadFontEffects? FontEffects =>
            Node.GetChild("effects") is { } effects ? new KiCadFontEffects(effects) : null;

        /// <summary>Gets the text rendering, adding an <c>(effects …)</c> form when there is none.</summary>
        /// <returns>The view.</returns>
        public KiCadFontEffects RequireFontEffects() => new(Require("effects"));
    }

    /// <summary>
    /// A dimension: <c>(dimension (type aligned) (layer "Dwgs.User") (pts (xy …) (xy …))
    /// (height h) (gr_text "…" …) (format …) (style …))</c>.
    /// </summary>
    /// <remarks>
    /// Modelled loosely on purpose. A dimension carries a whole formatting sub-language — units,
    /// precision, suppression of trailing zeroes, arrow style, extension-line offsets — and the five
    /// dimension types do not agree on which of it applies. What is named here is what every type
    /// has; the rest is reachable through <see cref="KiCadNode.Node"/> and survives a save either way.
    /// </remarks>
    public sealed class KiCadDimension : KiCadNode
    {
        /// <summary>Creates a view over a <c>(dimension …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadDimension(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty dimension.</summary>
        public KiCadDimension()
            : base(new SExpression("dimension"))
        {
        }

        /// <summary>
        /// Gets or sets the kind of dimension: <c>aligned</c>, <c>orthogonal</c>, <c>leader</c>,
        /// <c>center</c> or <c>radial</c>.
        /// </summary>
        public string Type
        {
            get => ReadChild("type") ?? "aligned";
            set => WriteChild("type", value, SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets the layer the dimension is drawn on.</summary>
        public string Layer
        {
            get => ReadChild("layer") ?? "Dwgs.User";
            set => WriteChild("layer", value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the dimension's UUID.</summary>
        public string? Uuid
        {
            get => ReadChild("uuid");
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets the two points being measured — the <c>(pts (xy …) (xy …))</c> form.</summary>
        public IReadOnlyList<KiCadPosition> Points => KiCadZone.ReadPoints(Node);

        /// <summary>Gets or sets how far the dimension line sits from the points it measures, in millimetres.</summary>
        public double Height
        {
            get => ReadChildDouble("height");
            set => WriteChildDouble("height", value);
        }

        /// <summary>
        /// Gets the rendered label, or <see langword="null"/>. It is a real <c>gr_text</c> nested
        /// inside the dimension, so it is the same <see cref="KiCadGrText"/> view a board-level text
        /// gets — including its position, which KiCad recomputes whenever the dimension moves.
        /// </summary>
        public KiCadGrText? Text =>
            Node.GetChild("gr_text") is { } text ? new KiCadGrText(text) : null;

        /// <summary>Appends a measured point.</summary>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        public void AddPoint(double x, double y)
        {
            var points = Node.GetChild("pts") ?? Node.CreateChild("pts");
            points.CreateChild("xy", Numbers.Format(x), Numbers.Format(y));
        }
    }

    /// <summary>
    /// A group: <c>(group "name" (uuid "…") (members "uuid" "uuid" …))</c>.
    /// </summary>
    /// <remarks>
    /// A group owns nothing — it names its members by their UUIDs, and the members live wherever
    /// they lived before. Removing an item from a board therefore leaves a dangling member here
    /// unless the group is edited too, which is exactly why <see cref="Members"/> is a list of
    /// strings rather than of views: there is no guarantee the other end exists.
    /// </remarks>
    public sealed class KiCadGroup : KiCadNode
    {
        /// <summary>Creates a view over a <c>(group …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadGroup(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates a group.</summary>
        /// <param name="name">The group's name.</param>
        public KiCadGroup(string name)
            : base(new SExpression("group"))
        {
            ArgumentNullException.ThrowIfNull(name);
            Node.AddValue(name, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the group's name, the form's first value.</summary>
        public string Name
        {
            get => Node.GetValue(0) ?? string.Empty;
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the group's own UUID, so that groups can hold groups.</summary>
        public string? Uuid
        {
            get => ReadChild("uuid");
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the UUIDs of the items in the group.</summary>
        public IReadOnlyList<string> Members
        {
            get
            {
                var members = Node.GetChild("members");
                return members is null ? Array.Empty<string>() : members.Values.ToArray();
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);
                var members = Require("members");
                members.Values.Clear();
                foreach (var member in value)
                {
                    members.Values.Add(member, SQuoteStyle.Quoted);
                }
            }
        }

        /// <summary>Adds an item's UUID to the group.</summary>
        /// <param name="uuid">The UUID of the item to add.</param>
        public void AddMember(string uuid)
        {
            ArgumentNullException.ThrowIfNull(uuid);
            Require("members").Values.Add(uuid, SQuoteStyle.Quoted);
        }

        /// <summary>Removes an item's UUID from the group.</summary>
        /// <param name="uuid">The UUID of the item to remove.</param>
        /// <returns>True when the group held it.</returns>
        public bool RemoveMember(string uuid)
        {
            ArgumentNullException.ThrowIfNull(uuid);
            return Node.GetChild("members")?.Values.Remove(uuid) ?? false;
        }
    }
}
