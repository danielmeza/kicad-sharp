using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using KiCadSharp.Documents;
using KiCadSharp.Geometry;

namespace KiCadSharp.Specctra
{
    /// <summary>One routed wire of a session: a polyline on one layer, or an arc.</summary>
    /// <param name="Net">The net, empty when the session names none.</param>
    /// <param name="Layer">The copper layer.</param>
    /// <param name="Width">Millimetres.</param>
    /// <param name="Points">Board coordinates, Y down. An arc has three: start, a point on it, end.</param>
    /// <param name="IsArc">True for a <c>qarc</c>.</param>
    public sealed record SessionWire(string Net, string Layer, double Width, IReadOnlyList<BoardPoint> Points, bool IsArc = false);

    /// <summary>One via of a session.</summary>
    /// <param name="Net">The net.</param>
    /// <param name="Position">Board coordinates, Y down.</param>
    /// <param name="Diameter">Millimetres.</param>
    /// <param name="Drill">Millimetres, when the padstack's name carries it.</param>
    /// <param name="Layers">The copper layers its padstack has a shape on, top first.</param>
    public sealed record SessionVia(string Net, BoardPoint Position, double Diameter, double? Drill, IReadOnlyList<string> Layers);

    /// <summary>What applying a session did to a board.</summary>
    /// <param name="Removed">Unlocked tracks, arcs and vias taken off to make way for the session's.</param>
    /// <param name="Kept">Locked ones left in place.</param>
    /// <param name="Tracks">Tracks and arcs added.</param>
    /// <param name="Vias">Vias added.</param>
    /// <param name="Skipped">Items the board could not take: an unknown layer or net.</param>
    public sealed record SessionImport(int Removed, int Kept, int Tracks, int Vias, IReadOnlyList<string> Skipped);

    /// <summary>
    /// A Specctra session (<c>.ses</c>) — what an autorouter hands back — and the copper it brings
    /// into a board: what KiCad's File → Import → Specctra Session does, without KiCad.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>KiCad's semantics, measured against KiCad.</b> Applying a session REPLACES every unlocked
    /// track, arc and via on the board with the session's routes, and keeps every locked one: the
    /// design wrote them as <c>fix</c> wiring, the router routed to them, and the session does not
    /// return them. The test compares the board this produces with the one pcbnew 10.0.6 produces
    /// from the same board and session, track by track and via by via.
    /// </para>
    /// <para>
    /// A via's drill is not in a session at all. KiCad's exporter writes it into the padstack's
    /// name — <c>Via[0-3]_600:300_um</c> — and reads it back from there, and so does this; a name
    /// that carries none falls back to the drill the caller gives.
    /// </para>
    /// <para>
    /// The session's <c>placement</c> is not applied: a router does not move parts, and KiCad's
    /// treatment of it would flip a part that the session places on the side it is already on.
    /// </para>
    /// </remarks>
    public sealed class SpecctraSession
    {
        private SpecctraSession(IReadOnlyList<SessionWire> wires, IReadOnlyList<SessionVia> vias)
        {
            Wires = wires;
            Vias = vias;
        }

        /// <summary>Gets every routed wire.</summary>
        public IReadOnlyList<SessionWire> Wires { get; }

        /// <summary>Gets every via.</summary>
        public IReadOnlyList<SessionVia> Vias { get; }

        /// <summary>Reads a session.</summary>
        /// <param name="text">The <c>.ses</c> file's content.</param>
        /// <returns>The session.</returns>
        /// <exception cref="FormatException">It is not a session, or has no routes.</exception>
        public static SpecctraSession Parse(string text)
        {
            var root = SpecctraReader.Parse(text);
            if (root.Head != "session")
            {
                throw new FormatException($"A session file begins with (session …), not ({root.Head} …).");
            }

            var routes = root.Child("routes") ?? throw new FormatException("The session has no (routes …): nothing was routed.");
            var scale = Scale(routes.Child("resolution"));
            BoardPoint Point(double x, double y) => new(x * scale, -y * scale);

            var stacks = new Dictionary<string, (double Diameter, List<string> Layers)>(StringComparer.Ordinal);
            foreach (var padstack in routes.Child("library_out")?.ChildrenNamed("padstack") ?? [])
            {
                var circles = padstack.ChildrenNamed("shape").Select(s => s.Child("circle")).Where(c => c is not null).ToList();
                if (circles.Count > 0)
                {
                    stacks[padstack.Atom(0)!] = (circles[0]!.Number(1) * scale, circles.Select(c => c!.Atom(0)!).ToList());
                }
            }

            var wires = new List<SessionWire>();
            var vias = new List<SessionVia>();
            foreach (var net in routes.Child("network_out")?.ChildrenNamed("net") ?? [])
            {
                var name = net.Atom(0) ?? string.Empty;
                foreach (var wire in net.ChildrenNamed("wire"))
                {
                    if (wire.Child("path") is { } path)
                    {
                        var numbers = path.Atoms.Skip(2).Select(Number).ToList();
                        var points = new List<BoardPoint>();
                        for (var i = 0; i + 1 < numbers.Count; i += 2)
                        {
                            points.Add(Point(numbers[i], numbers[i + 1]));
                        }

                        wires.Add(new SessionWire(name, path.Atom(0)!, path.Number(1) * scale, points));
                    }
                    else if (wire.Child("qarc") is { } arc)
                    {
                        // (qarc layer width x1 y1 x2 y2 cx cy): an arc from start to end about a centre.
                        var n = arc.Atoms.Skip(2).Select(Number).ToList();
                        var start = Point(n[0], n[1]);
                        var end = Point(n[2], n[3]);
                        var centre = Point(n[4], n[5]);
                        wires.Add(new SessionWire(name, arc.Atom(0)!, arc.Number(1) * scale, [start, Mid(start, end, centre), end], IsArc: true));
                    }
                }

                foreach (var via in net.ChildrenNamed("via"))
                {
                    var stack = via.Atom(0)!;
                    var (diameter, layers) = stacks.TryGetValue(stack, out var known) ? known : (0, []);
                    var numbers = via.Atoms.Skip(1).Select(Number).ToList();
                    for (var i = 0; i + 1 < numbers.Count; i += 2)
                    {
                        vias.Add(new SessionVia(name, Point(numbers[i], numbers[i + 1]), diameter, DrillOf(stack), layers));
                    }
                }
            }

            return new SpecctraSession(wires, vias);
        }

        /// <summary>
        /// Replaces the board's unlocked copper with the session's routes, keeping what is locked.
        /// </summary>
        /// <param name="board">The board the session was routed from.</param>
        /// <param name="viaDrill">The drill, millimetres, for a via whose padstack name carries none.</param>
        /// <returns>What changed.</returns>
        public SessionImport ApplyTo(KiCadBoard board, double viaDrill)
        {
            ArgumentNullException.ThrowIfNull(board);
            var copper = CopperGeometry.CopperLayers(board);
            var numbered = board.Nets.Count > 0;
            var skipped = new List<string>();

            var removed = 0;
            var kept = 0;
            foreach (var segment in board.Segments.ToList())
            {
                if (segment.Locked)
                {
                    kept++;
                }
                else
                {
                    board.Segments.Remove(segment);
                    removed++;
                }
            }

            foreach (var arc in board.TrackArcs.ToList())
            {
                if (arc.Locked)
                {
                    kept++;
                }
                else
                {
                    board.TrackArcs.Remove(arc);
                    removed++;
                }
            }

            foreach (var via in board.Vias.ToList())
            {
                if (via.Locked)
                {
                    kept++;
                }
                else
                {
                    board.Vias.Remove(via);
                    removed++;
                }
            }

            bool AssignNet(KiCadTrackItem item, string net, string what)
            {
                if (net.Length == 0)
                {
                    return true;
                }

                if (!numbered)
                {
                    item.NetName = net;
                    return true;
                }

                if (board.GetNet(net) is { } known)
                {
                    item.Net = known.Code;
                    return true;
                }

                skipped.Add($"{what} on net '{net}', which the board does not have");
                return false;
            }

            var tracks = 0;
            foreach (var wire in Wires)
            {
                if (!copper.Contains(wire.Layer))
                {
                    skipped.Add($"a wire on layer '{wire.Layer}', which the board does not have");
                    continue;
                }

                if (wire.IsArc)
                {
                    var arc = new KiCadTrackArc
                    {
                        Start = Position(wire.Points[0]),
                        Mid = Position(wire.Points[1]),
                        End = Position(wire.Points[2]),
                        Width = wire.Width,
                        Layer = wire.Layer,
                    };
                    if (AssignNet(arc, wire.Net, "an arc"))
                    {
                        arc.Uuid = Guid.NewGuid().ToString();
                        board.TrackArcs.Add(arc);
                        tracks++;
                    }

                    continue;
                }

                for (var i = 0; i + 1 < wire.Points.Count; i++)
                {
                    var segment = new KiCadTrackSegment
                    {
                        Start = Position(wire.Points[i]),
                        End = Position(wire.Points[i + 1]),
                        Width = wire.Width,
                        Layer = wire.Layer,
                    };
                    if (AssignNet(segment, wire.Net, "a track"))
                    {
                        segment.Uuid = Guid.NewGuid().ToString();
                        board.Segments.Add(segment);
                        tracks++;
                    }
                }
            }

            var vias = 0;
            foreach (var via in Vias)
            {
                var layers = via.Layers.Where(copper.Contains).OrderBy(l => IndexOf(copper, l)).ToList();
                if (layers.Count == 0)
                {
                    skipped.Add($"a via at ({via.Position.X}, {via.Position.Y}) whose padstack names no layer of this board");
                    continue;
                }

                var node = new KiCadVia
                {
                    Position = Position(via.Position),
                    Size = via.Diameter,
                    Drill = via.Drill ?? viaDrill,
                };

                // One shape, or one on every copper layer, is a through via; anything else is the
                // span its shapes cover — blind if it reaches an outer layer, buried if not.
                var through = layers.Count == 1 || layers.Count == copper.Count;
                node.Layers = through ? [copper[0], copper[^1]] : [layers[0], layers[^1]];
                if (!through)
                {
                    var outer = IndexOf(copper, layers[0]) == 0 || IndexOf(copper, layers[^1]) == copper.Count - 1;
                    node.ViaType = outer ? KiCadTokens.Board.ViaBlind : "buried";
                }

                if (AssignNet(node, via.Net, "a via"))
                {
                    node.Uuid = Guid.NewGuid().ToString();
                    board.Vias.Add(node);
                    vias++;
                }
            }

            return new SessionImport(removed, kept, tracks, vias, skipped);
        }

        private static KiCadPosition Position(BoardPoint p) => new(Math.Round(p.X, 6), Math.Round(p.Y, 6));

        private static int IndexOf(IReadOnlyList<string> layers, string layer)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                if (string.Equals(layers[i], layer, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static double Number(string atom) => double.Parse(atom, NumberStyles.Float, CultureInfo.InvariantCulture);

        /// <summary>Millimetres per session unit: <c>(resolution um 10)</c> is 0.1 µm, 0.0001 mm.</summary>
        private static double Scale(SpecctraNode? resolution)
        {
            if (resolution is null)
            {
                return 0.001;
            }

            var unit = resolution.Atom(0) switch
            {
                "inch" => 25.4,
                "mil" => 0.0254,
                "cm" => 10.0,
                "mm" => 1.0,
                _ => 0.001,
            };
            return unit / resolution.Number(1);
        }

        /// <summary>The drill KiCad's exporter wrote into a via padstack's name, in millimetres.</summary>
        internal static double? DrillOf(string padstack)
        {
            var colon = padstack.IndexOf(':', StringComparison.Ordinal);
            var end = padstack.LastIndexOf('_');
            if (colon < 0 || end <= colon)
            {
                return null;
            }

            return double.TryParse(padstack.AsSpan(colon + 1, end - colon - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var um)
                ? um / 1000
                : null;
        }

        /// <summary>The point halfway along an arc about <paramref name="centre"/>, counter-clockwise as Specctra draws it.</summary>
        private static BoardPoint Mid(BoardPoint start, BoardPoint end, BoardPoint centre)
        {
            var r = start.DistanceTo(centre);
            var a0 = Math.Atan2(start.Y - centre.Y, start.X - centre.X);
            var a1 = Math.Atan2(end.Y - centre.Y, end.X - centre.X);

            // Counter-clockwise in Specctra's Y-up frame is clockwise in the board's Y-down one.
            var sweep = a1 - a0;
            while (sweep >= 0)
            {
                sweep -= 2 * Math.PI;
            }

            var a = a0 + (sweep / 2);
            return new BoardPoint(centre.X + (r * Math.Cos(a)), centre.Y + (r * Math.Sin(a)));
        }
    }
}
