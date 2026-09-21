using System.Globalization;

using KiCadSharp.Specctra;

namespace KiCadSharp.Tests.Specctra;

/// <summary>
/// A <c>.dsn</c> reduced to what a router reads from it, so two writers can be compared item by item
/// rather than byte by byte, with numbers compared as numbers.
/// </summary>
/// <remarks>
/// Padstack NAMES are resolved away: a pin is compared by the geometry its padstack holds on each
/// layer, because a name is a label the file resolves internally and two writers may choose
/// different ones for the same copper. Wiring is split into single segments, because KiCad joins
/// consecutive segments of one net into one path and this library does not. Numbers are micrometres
/// and are compared within a tolerance, never by a rounded key: a coordinate that lands on half a
/// micrometre rounds one way in one writer and the other way in the next.
/// </remarks>
internal sealed class DsnView
{
    private DsnView(SpecctraNode pcb)
    {
        var structure = pcb.Child("structure")!;
        var library = pcb.Child("library")!;

        Layers = structure.ChildrenNamed("layer").Select(l => (l.Atom(0)!, l.Child("type")!.Atom(0)!)).ToList();
        Boundary = structure.Child("boundary")!.ChildrenNamed("path").Select(p => Points(p, skip: 2)).ToList();
        Planes = structure.ChildrenNamed("plane")
            .Select(p => new Shape($"plane {p.Atom(0)}", p.Child("polygon")!.Atom(0)!, Flatten(Points(p.Child("polygon")!, skip: 2))))
            .ToList();
        Keepouts = structure.Children
            .Where(c => c.Head is "keepout" or "via_keepout" or "wire_keepout")
            .Select(k => new Shape(k.Head, k.Children.First().Atom(0)!, Flatten(Points(k.Children.First(), skip: 2))))
            .ToList();

        var padstacks = library.ChildrenNamed("padstack").ToDictionary(
            p => p.Atom(0)!,
            p => p.ChildrenNamed("shape").Select(s => Describe(s.Children.First())).OrderBy(s => s.Layer, StringComparer.Ordinal).ToList(),
            StringComparer.Ordinal);
        var images = library.ChildrenNamed("image").ToDictionary(i => i.Atom(0)!, i => i, StringComparer.Ordinal);

        foreach (var component in pcb.Child("placement")!.ChildrenNamed("component"))
        {
            var image = images[component.Atom(0)!];
            foreach (var place in component.ChildrenNamed("place"))
            {
                var pins = image.ChildrenNamed("pin").ToDictionary(
                    p => p.Atoms.Skip(1).First(),
                    p => new Pin(padstacks[p.Atom(0)!], Rotation(p.Child("rotate")), p.Number(p.Atoms.Count() - 2), p.Number(p.Atoms.Count() - 1)),
                    StringComparer.Ordinal);
                var keepouts = image.Children.Where(c => c.Head.EndsWith("keepout", StringComparison.Ordinal))
                    .Select(k => Describe(k.Children.First()) with { Kind = k.Head + " " + k.Children.First().Head })
                    .ToList();
                Components[place.Atom(0)!] = new Placed(place.Number(1), place.Number(2), place.Atom(3)!, Rotation(place.Number(4)), pins, keepouts);
            }
        }

        var network = pcb.Child("network")!;
        foreach (var net in network.ChildrenNamed("net"))
        {
            Nets[net.Atom(0)!] = net.Child("pins")?.Atoms.ToHashSet(StringComparer.Ordinal) ?? [];
        }

        foreach (var cls in network.ChildrenNamed("class"))
        {
            var via = cls.Child("circuit")?.Child("use_via")?.Atom(0);
            var rule = cls.Child("rule")!;
            Classes[cls.Atom(0)!] = (
                rule.Child("width")!.Number(0),
                rule.Child("clearance")!.Number(0),
                via is null ? [] : padstacks[via],
                via is null ? double.NaN : Drill(via),
                cls.Atoms.Skip(1).ToHashSet(StringComparer.Ordinal));
        }

        var wiring = pcb.Child("wiring")!;
        foreach (var wire in wiring.ChildrenNamed("wire"))
        {
            var path = wire.Child("path")!;
            var points = Points(path, skip: 2);
            var head = $"{wire.Child("net")?.Atom(0)} {path.Atom(0)} w{path.Number(1).ToString("0.###", CultureInfo.InvariantCulture)} {wire.Child("type")?.Atom(0)}";
            for (var i = 0; i + 1 < points.Count; i++)
            {
                Segments.Add(new Shape(head, "", [points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y]));
            }
        }

        foreach (var via in wiring.ChildrenNamed("via"))
        {
            var stack = padstacks[via.Atom(0)!];
            Vias.Add(new Shape(
                $"{via.Child("net")?.Atom(0)} {via.Child("type")?.Atom(0)} {string.Join(" ", stack.Select(s => s.Layer))}",
                "",
                [via.Number(1), via.Number(2), stack[0].Numbers[0], Drill(via.Atom(0)!)]));
        }
    }

    public List<(string Name, string Type)> Layers { get; }

    public List<List<(double X, double Y)>> Boundary { get; }

    public List<Shape> Planes { get; }

    public List<Shape> Keepouts { get; }

    public Dictionary<string, Placed> Components { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, HashSet<string>> Nets { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, (double Width, double Clearance, List<Shape> Via, double Drill, HashSet<string> Nets)> Classes { get; } = new(StringComparer.Ordinal);

    public List<Shape> Segments { get; } = [];

    public List<Shape> Vias { get; } = [];

    public static DsnView Load(string path) => new(SpecctraReader.Parse(File.ReadAllText(path)));

    public static DsnView Of(SpecctraNode pcb) => new(pcb);

    /// <summary>A shape as a kind, a layer and its numbers — for a polygon, its extent.</summary>
    internal sealed record Shape(string Kind, string Layer, double[] Numbers)
    {
        public bool Matches(Shape other, double tolerance) =>
            Kind == other.Kind && Layer == other.Layer && Numbers.Length == other.Numbers.Length
            && Numbers.Zip(other.Numbers).All(p => Math.Abs(p.First - p.Second) <= tolerance);

        public override string ToString() =>
            $"{Kind} {Layer} [{string.Join(" ", Numbers.Select(n => n.ToString("0.#", CultureInfo.InvariantCulture)))}]";
    }

    internal sealed record Pin(List<Shape> Padstack, double Rotation, double X, double Y)
    {
        public bool Matches(Pin other, double tolerance) =>
            Rotation == other.Rotation && Math.Abs(X - other.X) <= tolerance && Math.Abs(Y - other.Y) <= tolerance
            && Padstack.Count == other.Padstack.Count
            && Padstack.Zip(other.Padstack).All(p => p.First.Matches(p.Second, p.First.Kind == "polygon" ? 2.5 : tolerance));

        public override string ToString() => $"rot {Rotation} at {X},{Y}: {string.Join(" | ", Padstack)}";
    }

    internal sealed record Placed(double X, double Y, string Side, double Rotation, Dictionary<string, Pin> Pins, List<Shape> Keepouts);

    private static Shape Describe(SpecctraNode shape)
    {
        var numbers = shape.Atoms.Skip(1).Select(a => double.Parse(a, CultureInfo.InvariantCulture)).ToList();
        switch (shape.Head)
        {
            case "circle":
                return new Shape("circle", shape.Atom(0)!, [numbers[0], numbers.ElementAtOrDefault(1), numbers.ElementAtOrDefault(2)]);
            case "rect":
                return new Shape("rect", shape.Atom(0)!,
                    [Math.Min(numbers[0], numbers[2]), Math.Min(numbers[1], numbers[3]), Math.Max(numbers[0], numbers[2]), Math.Max(numbers[1], numbers[3])]);
            case "path":
                // The two ends in a canonical order: which one a writer names first is not copper.
                var ends = new[] { (numbers[1], numbers[2]), (numbers[3], numbers[4]) }.OrderBy(e => e.Item1).ThenBy(e => e.Item2).ToList();
                return new Shape("oval", shape.Atom(0)!, [numbers[0], ends[0].Item1, ends[0].Item2, ends[1].Item1, ends[1].Item2]);
            default:
            {
                // A polygon by its extent: two writers approximate a rounded corner with different
                // vertices, and both have only to cover it.
                var xs = numbers.Skip(1).Where((_, i) => i % 2 == 0).ToList();
                var ys = numbers.Skip(1).Where((_, i) => i % 2 == 1).ToList();
                return new Shape("polygon", shape.Atom(0)!, [xs.Min(), ys.Min(), xs.Max(), ys.Max()]);
            }
        }
    }

    private static double Drill(string viaName)
    {
        var colon = viaName.IndexOf(':', StringComparison.Ordinal);
        var end = viaName.LastIndexOf('_');
        return colon < 0 || end < colon ? double.NaN : double.Parse(viaName[(colon + 1)..end], CultureInfo.InvariantCulture);
    }

    private static List<(double X, double Y)> Points(SpecctraNode node, int skip)
    {
        var numbers = node.Atoms.Skip(skip).Select(a => double.Parse(a, CultureInfo.InvariantCulture)).ToList();
        var points = new List<(double, double)>();
        for (var i = 0; i + 1 < numbers.Count; i += 2)
        {
            points.Add((numbers[i], numbers[i + 1]));
        }

        return points;
    }

    private static double[] Flatten(List<(double X, double Y)> points) => points.SelectMany(p => new[] { p.X, p.Y }).ToArray();

    private static double Rotation(SpecctraNode? rotate) => rotate is null ? 0 : Rotation(rotate.Number(0));

    private static double Rotation(double degrees)
    {
        var a = Math.Round(degrees % 360, 3);
        return a < 0 ? a + 360 : a == 360 ? 0 : a;
    }
}
