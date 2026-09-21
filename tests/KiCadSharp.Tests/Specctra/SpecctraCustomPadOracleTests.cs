using System.Globalization;

using KiCadSharp.Documents;
using KiCadSharp.Specctra;

namespace KiCadSharp.Tests.Specctra;

/// <summary>
/// A custom pad's padstack against the one pcbnew 10.0.6 writes for it, vertex by vertex.
/// </summary>
/// <remarks>
/// <para>
/// Both writers put a convex hull in the padstack. KiCad's is the hull of the polygon
/// <c>PAD::MergePrimitivesAsPolygon</c> draws for the pad (<c>EXPORT_CUSTOM_PADS_CONVEX_HULL</c> is
/// defined in <c>specctra_export.cpp</c>), and that polygon lies INSIDE the pad by up to the pad's
/// <c>GetMaxError()</c> of 5 µm: every arc, circle and pen end is drawn with <c>ERROR_INSIDE</c>,
/// and a Bézier is cut into chords on the curve. This library's hull is of the primitives
/// themselves, grown to cover them. So two things are asserted, not equality: every vertex of
/// KiCad's hull lies inside this one, and no vertex of this one lies more than 6 µm outside
/// KiCad's — its 5 µm, this library's 0.5 µm chord error, and the 0.12 % by which a round part's
/// covering points reach past it.
/// </para>
/// <para>
/// <see cref="SpecctraDesignOracleTests"/> compares every padstack by its extent within 2.5 µm,
/// which a custom pad's round end only meets by luck, so <c>custom-primitives.dsn</c> is compared
/// here and only here.
/// </para>
/// </remarks>
public class SpecctraCustomPadOracleTests
{
    private const double Covered = 0.01;
    private const double Tight = 6.0;

    /// <summary>
    /// A stroked rectangle whose corner radius is half its side is a circle, and KiCad 10.0.6 draws
    /// it as a dot the width of the pen. <c>ROUNDRECT::TransformToPolygon</c> makes its outline one
    /// 360° arc; <c>TransformArcToPolygon</c> finds no chord between its equal ends
    /// (<c>ARC_CHORD_PARAMS::Compute</c>) and draws a pen stroke from the start to the start. Its
    /// DRC shape is the whole ring, and <see cref="Geometry.CopperGeometryOracleTests"/> measures
    /// that; its hull here can only be checked for covering KiCad's.
    /// </summary>
    private static readonly string[] KiCadDrawsADot = ["RRECT_RING_"];

    [Theory]
    [InlineData("pad-shapes")]
    [InlineData("custom-primitives")]
    public void ACustomPadIsKiCadsHullAndCoversIt(string name)
    {
        var board = KiCadBoard.Load(Path.Combine(TestData.Root, "oracles", name + ".kicad_pcb"));
        var dsn = SpecctraReader.Parse(File.ReadAllText(Path.Combine(TestData.Root, "oracles", name + ".dsn")));
        var parser = dsn.Child("parser")!;
        Assert.Equal("KiCad's Pcbnew", parser.Child("host_cad")!.Atom(0));
        Assert.StartsWith("10.0.6", parser.Child("host_version")!.Atom(0), StringComparison.Ordinal);
        var ours = SpecctraDesign.Build(board, new SpecctraOptions());

        var custom = board.Footprints
            .Where(f => f.Pads.Any(p => p.Shape == "custom"))
            .Select(f => f.GetPropertyValue("Reference")!)
            .ToHashSet(StringComparer.Ordinal);
        var theirs = Polygons(dsn, custom);
        var mine = Polygons(ours, custom);
        var theirView = DsnView.Of(dsn);
        var myView = DsnView.Of(ours);

        Assert.NotEmpty(theirs);
        Assert.Equal(theirs.Keys.Order(), mine.Keys.Order());
        var failures = new List<string>();
        var (worstOutside, worstBeyond) = (double.MinValue, double.MinValue);
        foreach (var ((reference, pin), kicad) in theirs)
        {
            var at = theirView.Components[reference].Pins[pin];
            var mineAt = myView.Components[reference].Pins[pin];
            Assert.True(
                at.Rotation == mineAt.Rotation && Math.Abs(at.X - mineAt.X) <= 1.5 && Math.Abs(at.Y - mineAt.Y) <= 1.5,
                $"{reference}-{pin} sits at {mineAt.X},{mineAt.Y} rot {mineAt.Rotation}; KiCad puts it at {at.X},{at.Y} rot {at.Rotation}");

            var hull = mine[(reference, pin)];
            var outside = kicad.Max(p => Outside(p, hull));
            worstOutside = Math.Max(worstOutside, outside);
            if (outside > Covered)
            {
                failures.Add($"{reference}-{pin}: a vertex of KiCad's hull lies {outside:F3} um outside this one");
            }

            if (KiCadDrawsADot.Any(p => reference.StartsWith(p, StringComparison.Ordinal)))
            {
                continue;
            }

            // KiCad's hull is convex only to within a few nanometres, and a vertex that far in turns
            // its neighbours' edges through the polygon; measure against the hull of its vertices.
            var beyond = hull.Max(p => Outside(p, Hull(kicad)));
            worstBeyond = Math.Max(worstBeyond, beyond);
            if (beyond > Tight)
            {
                failures.Add($"{reference}-{pin}: a vertex of this hull lies {beyond:F3} um outside KiCad's");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} of {theirs.Count} custom pads (worst: {worstOutside:F3} um outside, {worstBeyond:F3} um beyond):\n"
            + string.Join("\n", failures.Take(20)));
    }

    /// <summary>Each custom pad's polygon, in its image's frame, by component and pin.</summary>
    private static Dictionary<(string Reference, string Pin), List<(double X, double Y)>> Polygons(SpecctraNode pcb, HashSet<string> references)
    {
        var library = pcb.Child("library")!;
        var stacks = library.ChildrenNamed("padstack").ToDictionary(p => p.Atom(0)!, StringComparer.Ordinal);
        var images = library.ChildrenNamed("image").ToDictionary(i => i.Atom(0)!, StringComparer.Ordinal);
        var polygons = new Dictionary<(string, string), List<(double, double)>>();
        foreach (var component in pcb.Child("placement")!.ChildrenNamed("component"))
        {
            foreach (var place in component.ChildrenNamed("place").Where(p => references.Contains(p.Atom(0)!)))
            {
                foreach (var pin in images[component.Atom(0)!].ChildrenNamed("pin"))
                {
                    var shape = stacks[pin.Atom(0)!].ChildrenNamed("shape").First().Children.First();
                    Assert.Equal("polygon", shape.Head);
                    var numbers = shape.Atoms.Skip(2).Select(a => double.Parse(a, CultureInfo.InvariantCulture)).ToList();
                    polygons[(place.Atom(0)!, pin.Atoms.Skip(1).First())] = Enumerable.Range(0, numbers.Count / 2)
                        .Select(i => (numbers[2 * i], numbers[(2 * i) + 1]))
                        .ToList();
                }
            }
        }

        return polygons;
    }

    /// <summary>The convex hull, by Andrew's monotone chain.</summary>
    private static List<(double X, double Y)> Hull(List<(double X, double Y)> points)
    {
        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        var hull = new List<(double X, double Y)>();
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

    /// <summary>How far a point lies outside a convex polygon, negative when inside.</summary>
    private static double Outside((double X, double Y) p, List<(double X, double Y)> convex)
    {
        var turn = 0.0;
        for (var i = 0; i < convex.Count; i++)
        {
            var (a, b) = (convex[i], convex[(i + 1) % convex.Count]);
            turn += (a.X * b.Y) - (b.X * a.Y);
        }

        var worst = double.MinValue;
        for (var i = 0; i < convex.Count; i++)
        {
            var (a, b) = (convex[i], convex[(i + 1) % convex.Count]);
            var length = Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
            if (length > 0)
            {
                var left = (((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X))) / length;
                worst = Math.Max(worst, turn > 0 ? -left : left);
            }
        }

        return worst;
    }
}
