using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using KiCadSharp.Documents;
using KiCadSharp.Geometry;

using SExpressions;

namespace KiCadSharp.Specctra
{
    /// <summary>
    /// A board written as a Specctra design (<c>.dsn</c>), for an autorouter such as Freerouting —
    /// what KiCad's File → Export → Specctra DSN writes, without KiCad.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Judged against KiCad, not against itself.</b> The format is KiCad's dialect of Specctra,
    /// and the tests compare what this writes with what pcbnew 10.0.6 writes for the same boards —
    /// layers, boundary, planes, keepouts, every pin of every image with its padstack's geometry,
    /// every component's placement, every net's pins, every class, every wire and via — item by
    /// item. Names of padstacks are not compared: they are labels the file resolves internally.
    /// </para>
    /// <para>
    /// Where this deliberately differs from KiCad, and why:
    /// </para>
    /// <list type="bullet">
    ///   <item>A track <b>arc</b> is written as the polyline that follows it. KiCad writes a straight
    ///   path from one end to the other, which puts copper where there is none.</item>
    ///   <item>A <b>custom pad</b> is written as the convex hull of its primitives, which covers it.
    ///   KiCad writes a convex hull too, of the polygon it draws for the pad, which lies inside the pad
    ///   by up to KiCad's max error (5 µm by default).</item>
    ///   <item>Image <b>outlines</b> — courtyard and silkscreen — are not written. The router does not
    ///   read them.</item>
    ///   <item>Numbers are written to 0.1 nm rather than to six significant figures.</item>
    /// </list>
    /// <para>
    /// Locked copper is written as <c>fix</c> wiring, as KiCad does: the router routes to it and
    /// never returns it, which is how a hand-drawn neck into a fine-pitch pad survives a session.
    /// </para>
    /// </remarks>
    public static class SpecctraDesign
    {
        private const double Um = 1000.0;

        /// <summary>The board as <c>.dsn</c> text.</summary>
        /// <param name="board">The board.</param>
        /// <param name="options">The net classes and the numbers the board file does not carry.</param>
        /// <returns>The text.</returns>
        /// <exception cref="InvalidOperationException">The board has no copper layer or no closed outline.</exception>
        public static string Export(KiCadBoard board, SpecctraOptions options) => Build(board, options).ToString();

        /// <summary>The board as a Specctra <c>pcb</c> form.</summary>
        /// <param name="board">The board.</param>
        /// <param name="options">The net classes and the numbers the board file does not carry.</param>
        /// <returns>The form.</returns>
        /// <exception cref="InvalidOperationException">The board has no copper layer or no closed outline.</exception>
        public static SpecctraNode Build(KiCadBoard board, SpecctraOptions options)
        {
            ArgumentNullException.ThrowIfNull(board);
            ArgumentNullException.ThrowIfNull(options);
            return new Writer(board, options).Build();
        }

        /// <summary>Every net name on the board, in the order the file first mentions it.</summary>
        /// <param name="board">The board.</param>
        /// <returns>The names; the unnamed net is left out.</returns>
        public static IReadOnlyList<string> NetNames(KiCadBoard board)
        {
            ArgumentNullException.ThrowIfNull(board);
            var seen = new List<string>();
            var set = new HashSet<string>(StringComparer.Ordinal);
            void Note(string? name)
            {
                if (!string.IsNullOrEmpty(name) && set.Add(name))
                {
                    seen.Add(name);
                }
            }

            foreach (var net in board.Nets)
            {
                Note(net.Name);
            }

            foreach (var footprint in board.Footprints)
            {
                foreach (var pad in footprint.Pads)
                {
                    Note(pad.Net);
                }
            }

            foreach (var item in board.Segments.Cast<KiCadTrackItem>().Concat(board.TrackArcs).Concat(board.Vias))
            {
                Note(NetOf(board, item));
            }

            foreach (var zone in board.Zones)
            {
                Note(zone.NetName);
            }

            return seen;
        }

        /// <summary>The net a piece of copper is on, in either board format, or <see langword="null"/>.</summary>
        /// <param name="board">The board it is on.</param>
        /// <param name="item">The track, arc or via.</param>
        /// <returns>The name.</returns>
        public static string? NetOf(KiCadBoard board, KiCadTrackItem item)
        {
            ArgumentNullException.ThrowIfNull(board);
            ArgumentNullException.ThrowIfNull(item);
            if (item.NetName is { Length: > 0 } name)
            {
                return name;
            }

            return item.Net > 0 ? board.GetNet(item.Net)?.Name : null;
        }

        /// <summary>A footprint's reference designator, in either spelling the format has used.</summary>
        /// <param name="footprint">The footprint.</param>
        /// <returns>The reference, or empty.</returns>
        public static string ReferenceOf(KiCadFootprint footprint)
        {
            ArgumentNullException.ThrowIfNull(footprint);
            return footprint.GetPropertyValue("Reference")
                ?? footprint.TextItems.FirstOrDefault(t => t.Type == KiCadTokens.Footprint.TextTypeReference)?.Text
                ?? string.Empty;
        }

        private static string ValueOf(KiCadFootprint footprint) =>
            footprint.GetPropertyValue("Value")
            ?? footprint.TextItems.FirstOrDefault(t => t.Type == KiCadTokens.Footprint.TextTypeValue)?.Text
            ?? string.Empty;

        private static double Normalise(double degrees)
        {
            var a = Math.Round(degrees % 360, 6);
            return a < 0 ? a + 360 : a == 360 ? 0 : a;
        }

        private static string Fixed(double mm) => SpecctraNode.Number(mm * Um);

        private sealed class Writer
        {
            private readonly KiCadBoard _board;
            private readonly SpecctraOptions _options;
            private readonly IReadOnlyList<string> _copper;
            private readonly Dictionary<string, SpecctraNode> _padstacks = new(StringComparer.Ordinal);
            private readonly List<string> _viaOrder = [];
            private readonly Dictionary<string, SpecctraNode> _viaStacks = new(StringComparer.Ordinal);

            internal Writer(KiCadBoard board, SpecctraOptions options)
            {
                _board = board;
                _options = options;
                _copper = CopperGeometry.CopperLayers(board);
                if (_copper.Count == 0)
                {
                    throw new InvalidOperationException("The board has no copper layers, so there is nothing to route on.");
                }
            }

            internal SpecctraNode Build()
            {
                var version = typeof(SpecctraDesign).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
                var pcb = new SpecctraNode("pcb", _options.DesignName);
                pcb.Add(new SpecctraNode("parser",
                    new SpecctraNode("string_quote", "\""),
                    new SpecctraNode("space_in_quoted_tokens", "on"),
                    new SpecctraNode("host_cad", "KiCadSharp"),
                    new SpecctraNode("host_version", version.Split('+')[0])));
                pcb.Add(new SpecctraNode("resolution", "um", 10));
                pcb.Add(new SpecctraNode("unit", "um"));

                // Built in the order that lets each part name what the others registered: the vias
                // of the classes, of the parts and of the wiring all have to exist before the
                // structure lists them by name.
                var nets = NetNames(_board).ToList();
                var classOf = Classes(nets);
                var (placement, library, pins) = Parts();
                var wiring = Wiring();
                var network = new SpecctraNode("network");
                var structure = Structure(network);

                foreach (var net in nets)
                {
                    if (pins.TryGetValue(net, out var list) && list.Count > 0)
                    {
                        network.Add(new SpecctraNode("net", net, new SpecctraNode("pins", list.Cast<object>().ToArray())));
                    }
                }

                foreach (var cls in new[] { _options.DefaultClass }.Concat(_options.Classes))
                {
                    var members = nets.Where(n => ReferenceEquals(classOf[n], cls)).ToList();
                    if (members.Count == 0 && !ReferenceEquals(cls, _options.DefaultClass))
                    {
                        continue;
                    }

                    // Freerouting keeps a class named `default` of its own, and one written with that
                    // name gives it two default via rules. KiCad renames its own for the same reason.
                    var name = ReferenceEquals(cls, _options.DefaultClass) ? "kicad_default" : cls.Name;
                    var node = new SpecctraNode("class", name);
                    node.Add(members.Cast<object>().ToArray());
                    node.Add(new SpecctraNode("circuit", new SpecctraNode("use_via", Via(cls.ViaDiameter, cls.ViaDrill, 0, _copper.Count - 1))));
                    node.Add(new SpecctraNode("rule",
                        new SpecctraNode("width", cls.TrackWidth * Um),
                        new SpecctraNode("clearance", cls.Clearance * Um)));
                    network.Add(node);
                }

                foreach (var name in _viaOrder)
                {
                    library.Add(_viaStacks[name]);
                }

                pcb.Add(structure);
                pcb.Add(placement);
                pcb.Add(library);
                pcb.Add(network);
                pcb.Add(wiring);
                return pcb;
            }

            private Dictionary<string, SpecctraNetClass> Classes(IReadOnlyList<string> nets)
            {
                var map = new Dictionary<string, SpecctraNetClass>(StringComparer.Ordinal);
                foreach (var net in nets)
                {
                    map[net] = _options.Classes.FirstOrDefault(c => c.Nets.Contains(net)) ?? _options.DefaultClass;
                }

                // The default class's via goes first, then the others', so `use_via` finds them.
                Via(_options.DefaultClass.ViaDiameter, _options.DefaultClass.ViaDrill, 0, _copper.Count - 1);
                foreach (var cls in _options.Classes.Where(c => map.Values.Contains(c)))
                {
                    Via(cls.ViaDiameter, cls.ViaDrill, 0, _copper.Count - 1);
                }

                return map;
            }

            private SpecctraNode Structure(SpecctraNode network)
            {
                var structure = new SpecctraNode("structure");
                for (var i = 0; i < _copper.Count; i++)
                {
                    var type = _board.GetLayer(_copper[i])?.Type switch
                    {
                        "power" => "power",
                        "jumper" => "jumper",
                        _ => "signal",
                    };
                    structure.Add(new SpecctraNode("layer", _copper[i],
                        new SpecctraNode("type", type),
                        new SpecctraNode("property", new SpecctraNode("index", i))));
                }

                var outline = BoardOutline.Of(_board, _options.MaxError);
                if (outline.Outers.Count == 0)
                {
                    throw new InvalidOperationException(
                        "The board has no closed outline on Edge.Cuts, so a router has no area to route in.");
                }

                var boundary = new SpecctraNode("boundary");
                foreach (var outer in outline.Outers)
                {
                    boundary.Add(new SpecctraNode("path", "pcb", 0, Points(outer, close: true)));
                }

                structure.Add(boundary);

                var netless = 0;
                foreach (var zone in _board.Zones.Where(z => !z.IsKeepout))
                {
                    var points = zone.Points.Select(CopperGeometry.Point).ToList();
                    if (points.Count < 3)
                    {
                        continue;
                    }

                    foreach (var layer in CopperGeometry.CopperLayersOf(zone.Layers, _copper))
                    {
                        var net = zone.NetName;
                        if (string.IsNullOrEmpty(net))
                        {
                            // KiCad's own device for a zone on no net: a net of its own the router
                            // will never connect anything to.
                            net = "@:no_net_" + netless.ToString(CultureInfo.InvariantCulture);
                            netless++;
                            network.Add(new SpecctraNode("net", net));
                        }

                        structure.Add(new SpecctraNode("plane", net, new SpecctraNode("polygon", layer, 0, Points(points, close: true))));
                    }
                }

                foreach (var zone in _board.Zones.Where(z => z.IsKeepout))
                {
                    var points = zone.Points.Select(CopperGeometry.Point).ToList();
                    if (points.Count < 3)
                    {
                        continue;
                    }

                    var kind = KeepoutKind(zone.Node);
                    foreach (var layer in CopperGeometry.CopperLayersOf(zone.Layers, _copper))
                    {
                        structure.Add(new SpecctraNode(kind, string.Empty, new SpecctraNode("polygon", layer, 0, Points(points, close: true))));
                    }
                }

                foreach (var hole in outline.Holes)
                {
                    structure.Add(new SpecctraNode("keepout", string.Empty, new SpecctraNode("polygon", "signal", 0, Points(hole, close: true))));
                }

                var vias = new SpecctraNode("via");
                vias.Add(_viaOrder.Cast<object>().ToArray());
                structure.Add(vias);

                var fallback = _options.DefaultClass.Clearance;
                structure.Add(new SpecctraNode("rule",
                    new SpecctraNode("width", _options.DefaultClass.TrackWidth * Um),
                    new SpecctraNode("clearance", fallback * Um),
                    // Pads of one part sit closer than any clearance a board would route at; the
                    // router would otherwise report every fine-pitch part as a violation.
                    new SpecctraNode("clearance", fallback * Um / 4, new SpecctraNode("type", "smd_smd"))));
                return structure;
            }

            private (SpecctraNode Placement, SpecctraNode Library, Dictionary<string, List<SpecctraPinRef>> Pins) Parts()
            {
                var placement = new SpecctraNode("placement");
                var library = new SpecctraNode("library");
                var pins = new Dictionary<string, List<SpecctraPinRef>>(StringComparer.Ordinal);
                var components = new Dictionary<string, SpecctraNode>(StringComparer.Ordinal);
                var images = new Dictionary<string, (string Signature, string Name)>(StringComparer.Ordinal);
                var imageCount = new Dictionary<string, int>(StringComparer.Ordinal);
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var footprint in _board.Footprints)
                {
                    var reference = ReferenceOf(footprint);
                    if (reference.Length == 0)
                    {
                        reference = "REF**";
                    }

                    // A session file matches parts by reference, so each must be unique — and the
                    // format compares them without case.
                    var id = reference;
                    for (var n = 1; !used.Add(id); n++)
                    {
                        id = reference + "_" + n.ToString(CultureInfo.InvariantCulture);
                    }

                    var (image, signature, padNets) = Image(footprint);
                    var imageName = footprint.Id;
                    var key = imageName + "\n" + signature;
                    if (images.TryGetValue(key, out var known))
                    {
                        imageName = known.Name;
                    }
                    else
                    {
                        var count = imageCount.GetValueOrDefault(footprint.Id);
                        imageCount[footprint.Id] = count + 1;
                        if (count > 0)
                        {
                            imageName = footprint.Id + "::" + count.ToString(CultureInfo.InvariantCulture);
                        }

                        images[key] = (signature, imageName);
                        var named = new SpecctraNode("image", imageName);
                        named.Add(image.Items.ToArray());
                        library.Add(named);
                    }

                    foreach (var (pin, net) in padNets)
                    {
                        if (!pins.TryGetValue(net, out var list))
                        {
                            pins[net] = list = [];
                        }

                        list.Add(new SpecctraPinRef(id, pin));
                    }

                    if (!components.TryGetValue(imageName, out var component))
                    {
                        components[imageName] = component = new SpecctraNode("component", imageName);
                        placement.Add(component);
                    }

                    var at = footprint.Position;
                    var back = footprint.Layer == KiCadLayerNames.BCu;
                    var rotation = back ? Normalise(180 + at.Rotation) : Normalise(at.Rotation);
                    component.Add(new SpecctraNode("place", id, at.X * Um, -at.Y * Um, back ? "back" : "front", rotation,
                        new SpecctraNode("PN", ValueOf(footprint))));
                }

                foreach (var padstack in _padstacks.Values)
                {
                    library.Add(padstack);
                }

                return (placement, library, pins);
            }

            /// <summary>One footprint's image: its pins and hole keepouts, seen from the top.</summary>
            private (SpecctraNode Image, string Signature, List<(string Pin, string Net)> Nets) Image(KiCadFootprint footprint)
            {
                var image = new SpecctraNode("image");
                var nets = new List<(string, string)>();
                var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
                var at = footprint.Position;
                var origin = new BoardPoint(at.X, at.Y);
                var back = footprint.Layer == KiCadLayerNames.BCu;

                // A part on the back is described as KiCad describes it: flipped top to bottom
                // about its own position first, so the image is always seen from the top.
                var angle = back ? -at.Rotation : at.Rotation;
                BoardPoint Local(BoardPoint board)
                {
                    var p = back ? new BoardPoint(board.X, (2 * origin.Y) - board.Y) : board;
                    var rel = (p - origin).Rotate(-angle);
                    return new BoardPoint(rel.X * Um, -rel.Y * Um);
                }

                foreach (var pad in footprint.Pads)
                {
                    // Flipped to be seen from the top, a part's B.Cu pads are on F.Cu, and an inner
                    // layer on its mirror: In1 of four becomes In2.
                    var layers = CopperGeometry.CopperLayersOf(pad.Layers, _copper);
                    if (back)
                    {
                        layers = layers.Select(l => _copper[_copper.Count - 1 - IndexOf(l)]).OrderBy(IndexOf).ToArray();
                    }

                    var position = CopperGeometry.PadPosition(footprint, pad);
                    var vertex = Local(position);
                    var holeOnly = IsHoleOnly(pad, layers);
                    if (holeOnly || layers.Count == 0)
                    {
                        if (holeOnly && pad.Drill is { } drill)
                        {
                            var diameter = (drill.Width + (2 * _options.HoleClearance)) * Um;
                            foreach (var layer in _copper)
                            {
                                image.Add(new SpecctraNode("keepout", string.Empty,
                                    new SpecctraNode("circle", layer, diameter, vertex.X, vertex.Y)));
                            }
                        }

                        continue;
                    }

                    var padAngle = back ? -pad.Position.Rotation : pad.Position.Rotation;
                    var stack = Padstack(pad, layers, back);
                    var number = pad.Number;
                    var pin = number;
                    if (number.Length == 0 || numbers.ContainsKey(number))
                    {
                        var n = numbers.GetValueOrDefault(number) + 1;
                        numbers[number] = n;
                        pin = number + "@" + n.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        numbers[number] = 0;
                    }

                    var rotation = Normalise(padAngle - angle);
                    image.Add(new SpecctraNode("pin", stack, rotation == 0 ? null : new SpecctraNode("rotate", rotation), pin, vertex.X, vertex.Y));
                    if (!string.IsNullOrEmpty(pad.Net))
                    {
                        nets.Add((pin, pad.Net));
                    }
                }

                foreach (var zone in footprint.Node.GetChildren(KiCadTokens.Board.Zone).Select(n => new KiCadZone(n)).Where(z => z.IsKeepout))
                {
                    var kind = KeepoutKind(zone.Node);
                    var points = zone.Points.Select(p => Local(CopperGeometry.Point(p))).ToList();
                    foreach (var onBoard in CopperGeometry.CopperLayersOf(zone.Layers, _copper))
                    {
                        var layer = back ? _copper[_copper.Count - 1 - IndexOf(onBoard)] : onBoard;
                        var polygon = new SpecctraNode("polygon", layer, 0);
                        foreach (var p in points.Append(points[0]))
                        {
                            polygon.Add(p.X).Add(p.Y);
                        }

                        image.Add(new SpecctraNode(kind, string.Empty, polygon));
                    }
                }

                return (image, image.ToString(), nets);
            }

            /// <summary>
            /// A circular pad whose copper the hole removes entirely, or that has no copper at all:
            /// the router sees a round keepout, not a pin.
            /// </summary>
            private static bool IsHoleOnly(KiCadPad pad, IReadOnlyList<string> layers) =>
                pad.Shape == "circle" && pad.Drill is { } drill && (drill.Width >= pad.Size.Width || layers.Count == 0);

            /// <summary>The padstack for a pad's copper, registered once per distinct geometry.</summary>
            private string Padstack(KiCadPad pad, IReadOnlyList<string> layers, bool back)
            {
                var w = pad.Size.Width;
                var h = pad.Size.Height;
                var offset = pad.Drill?.Offset is { } o ? new BoardPoint(o.X, o.Y) : default;

                // The pad's own frame, as the design sees it from the top: Y up, and mirrored once
                // more for a pad on a part that was flipped to be seen from the top.
                BoardPoint Dsn(BoardPoint p) => new(p.X * Um, (back ? p.Y : -p.Y) * Um);

                var code = layers.Count == _copper.Count
                    ? "A"
                    : string.Concat(layers.Select(l =>
                    {
                        var i = IndexOf(l);
                        return i == 0 ? "T" : i == _copper.Count - 1 ? "B" : i.ToString(CultureInfo.InvariantCulture);
                    }));
                var shift = offset == default ? string.Empty : $"[{Fixed(offset.X)},{Fixed(back ? offset.Y : -offset.Y)}]";

                string name;
                Func<string, SpecctraNode> shape;
                switch (pad.Shape)
                {
                    case "circle":
                    {
                        var c = Dsn(offset);
                        name = $"Round[{code}]Pad_{Fixed(w)}_um{shift}";
                        shape = layer => new SpecctraNode("circle", layer, w * Um, c == default ? null : c.X, c == default ? null : c.Y);
                        break;
                    }

                    case "rect":
                    {
                        var a = Dsn(offset + new BoardPoint(-w / 2, -h / 2));
                        var b = Dsn(offset + new BoardPoint(w / 2, h / 2));
                        name = $"Rect[{code}]Pad_{Fixed(w)}x{Fixed(h)}_um{shift}";
                        shape = layer => new SpecctraNode("rect", layer, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                        break;
                    }

                    case "oval":
                    {
                        // Always a path, even a round one of no length — which is what KiCad writes,
                        // and what the router reads as the same copper as a circle.
                        var half = Math.Abs(w - h) / 2;
                        var a = Dsn(offset + (w >= h ? new BoardPoint(-half, 0) : new BoardPoint(0, -half)));
                        var b = Dsn(offset + (w >= h ? new BoardPoint(half, 0) : new BoardPoint(0, half)));
                        var width = Math.Min(w, h) * Um;
                        name = $"Oval[{code}]Pad_{Fixed(w)}x{Fixed(h)}_um{shift}";
                        shape = layer => new SpecctraNode("path", layer, width, a.X, a.Y, b.X, b.Y);
                        break;
                    }

                    default:
                    {
                        var outline = Outline(pad, w, h).Select(p => Dsn(offset + p)).ToList();
                        var chamfered = CopperGeometry.IsChamfered(pad);
                        var detail = chamfered
                            ? $"Chamfer[{code}]Pad_{Fixed(w)}x{Fixed(h)}_{outline.Count}_{Signature(outline)}_um"
                            : pad.Shape switch
                            {
                                "roundrect" => $"RoundRect[{code}]Pad_{Fixed(w)}x{Fixed(h)}_{Fixed(CopperGeometry.RoundRadius(pad, w, h))}_um",
                                "trapezoid" => $"Trapz[{code}]Pad_{Fixed(w)}x{Fixed(h)}_{string.Join("x", CopperGeometry.Trapezoid(pad, w, h).Select(p => Fixed(p.X) + "," + Fixed(p.Y)))}_um",
                                _ => $"{Capitalise(pad.Shape)}[{code}]Pad_{Fixed(w)}x{Fixed(h)}_{outline.Count}_{Signature(outline)}_um",
                            };

                        // A shape that is not symmetric top to bottom is a different padstack flipped.
                        name = detail + shift + (back && (chamfered || pad.Shape is "trapezoid" or "custom") ? "_back" : string.Empty);
                        shape = layer =>
                        {
                            var polygon = new SpecctraNode("polygon", layer, 0);
                            foreach (var p in outline.Append(outline[0]))
                            {
                                polygon.Add(p.X).Add(p.Y);
                            }

                            return polygon;
                        };
                        break;
                    }
                }

                if (!_padstacks.ContainsKey(name))
                {
                    var node = new SpecctraNode("padstack", name);
                    foreach (var layer in layers)
                    {
                        node.Add(new SpecctraNode("shape", shape(layer)));
                    }

                    node.Add(new SpecctraNode("attach", "off"));
                    _padstacks[name] = node;
                }

                return name;
            }

            /// <summary>A polygon covering a pad of a shape the format has no primitive for, in the pad's frame.</summary>
            private IEnumerable<BoardPoint> Outline(KiCadPad pad, double w, double h)
            {
                if (CopperGeometry.IsChamfered(pad))
                {
                    return CopperGeometry.ChamferedOutline(pad, w, h, _options.MaxError);
                }

                switch (pad.Shape)
                {
                    case "roundrect":
                        return CopperGeometry.CornerOutline(w, h, CopperGeometry.RoundRadius(pad, w, h), 0, new HashSet<string>(), _options.MaxError);
                    case "trapezoid":
                        return CopperGeometry.Trapezoid(pad, w, h);
                    default:
                        return Hull(CopperGeometry.PadLocal(pad, _options.MaxError).SelectMany(Cover));
                }
            }

            private SpecctraNode Wiring()
            {
                var wiring = new SpecctraNode("wiring");
                foreach (var segment in _board.Segments)
                {
                    if (NetOf(_board, segment) is not { } net || IndexOf(segment.Layer) < 0)
                    {
                        continue;
                    }

                    wiring.Add(new SpecctraNode("wire",
                        new SpecctraNode("path", segment.Layer, segment.Width * Um,
                            Points([CopperGeometry.Point(segment.Start), CopperGeometry.Point(segment.End)], close: false)),
                        new SpecctraNode("net", net),
                        new SpecctraNode("type", segment.Locked ? "fix" : "route")));
                }

                foreach (var arc in _board.TrackArcs)
                {
                    if (NetOf(_board, arc) is not { } net || IndexOf(arc.Layer) < 0)
                    {
                        continue;
                    }

                    var points = CopperGeometry.ArcPoints(CopperGeometry.Point(arc.Start), CopperGeometry.Point(arc.Mid), CopperGeometry.Point(arc.End), _options.MaxError);
                    wiring.Add(new SpecctraNode("wire",
                        new SpecctraNode("path", arc.Layer, arc.Width * Um, Points(points, close: false)),
                        new SpecctraNode("net", net),
                        new SpecctraNode("type", arc.Locked ? "fix" : "route")));
                }

                foreach (var via in _board.Vias)
                {
                    if (NetOf(_board, via) is not { } net)
                    {
                        continue;
                    }

                    var layers = CopperGeometry.CopperLayersOf(via.Layers, _copper);
                    var top = layers.Count == 0 ? 0 : IndexOf(layers[0]);
                    var bottom = layers.Count == 0 ? _copper.Count - 1 : IndexOf(layers[^1]);
                    if (via.ViaType == KiCadTokens.Board.ViaThrough)
                    {
                        (top, bottom) = (0, _copper.Count - 1);
                    }

                    var stack = Via(via.Size, via.Drill, top, bottom);
                    wiring.Add(new SpecctraNode("via", stack, via.Position.X * Um, -via.Position.Y * Um,
                        new SpecctraNode("net", net),
                        new SpecctraNode("type", via.Locked ? "fix" : "route")));
                }

                return wiring;
            }

            /// <summary>A via padstack, registered once. The drill is in the name, where KiCad's importer reads it.</summary>
            private string Via(double diameter, double drill, int top, int bottom)
            {
                var name = $"Via[{top}-{bottom}]_{Fixed(diameter)}:{Fixed(drill)}_um";
                if (!_viaStacks.ContainsKey(name))
                {
                    var node = new SpecctraNode("padstack", name);
                    for (var i = top; i <= bottom; i++)
                    {
                        node.Add(new SpecctraNode("shape", new SpecctraNode("circle", _copper[i], diameter * Um)));
                    }

                    node.Add(new SpecctraNode("attach", "off"));
                    _viaStacks[name] = node;
                    _viaOrder.Add(name);
                }

                return name;
            }

            private int IndexOf(string layer)
            {
                for (var i = 0; i < _copper.Count; i++)
                {
                    if (string.Equals(_copper[i], layer, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }

                return -1;
            }
        }

        private static string KeepoutKind(SExpression zone)
        {
            var keepout = zone.GetChild(KiCadTokens.Board.Keepout);
            bool Banned(string what) => string.Equals(keepout?.GetChild(what)?.GetValue(0), KiCadTokens.Board.NotAllowed, StringComparison.Ordinal);
            var tracks = Banned(KiCadTokens.Board.KeepoutTracks);
            var vias = Banned(KiCadTokens.Board.KeepoutVias);
            return tracks && vias ? "keepout" : vias ? "via_keepout" : tracks ? "wire_keepout" : "keepout";
        }

        private static IEnumerable<object> Points(IEnumerable<BoardPoint> points, bool close)
        {
            var list = points.ToList();
            if (close && list.Count > 0 && list[0] != list[^1])
            {
                list.Add(list[0]);
            }

            foreach (var p in list)
            {
                yield return p.X * Um;
                yield return -p.Y * Um;
            }
        }

        private static string Capitalise(string word) =>
            word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].Replace("_", string.Empty, StringComparison.Ordinal);

        private static string Signature(IEnumerable<BoardPoint> points)
        {
            var hash = 17L;
            foreach (var p in points)
            {
                hash = unchecked((hash * 31) + (long)Math.Round(p.X * 10) + ((long)Math.Round(p.Y * 10) * 7919));
            }

            return (hash & 0xFFFFFFFF).ToString("X8", CultureInfo.InvariantCulture);
        }

        /// <summary>Points around a rounded shape, far enough out that their hull covers it.</summary>
        private static IEnumerable<BoardPoint> Cover(RoundedShape part)
        {
            if (part.Radius == 0)
            {
                return part.Core;
            }

            const int steps = 64;
            var reach = part.Radius / Math.Cos(Math.PI / steps);
            return part.Core.SelectMany(c => Enumerable.Range(0, steps)
                .Select(i => new BoardPoint(c.X + (reach * Math.Cos(2 * Math.PI * i / steps)), c.Y + (reach * Math.Sin(2 * Math.PI * i / steps)))));
        }

        /// <summary>The convex hull, counter-clockwise on a Y-up frame (Andrew's monotone chain).</summary>
        private static List<BoardPoint> Hull(IEnumerable<BoardPoint> points)
        {
            var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (sorted.Count < 3)
            {
                return sorted;
            }

            static double Cross(BoardPoint o, BoardPoint a, BoardPoint b) => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
            var hull = new List<BoardPoint>();
            foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
            {
                var start = hull.Count;
                foreach (var p in pass)
                {
                    while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= 0)
                    {
                        hull.RemoveAt(hull.Count - 1);
                    }

                    hull.Add(p);
                }

                hull.RemoveAt(hull.Count - 1);
            }

            return hull;
        }
    }
}
