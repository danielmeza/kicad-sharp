using System.Text.Json;

using KiCadSharp.Documents;
using KiCadSharp.Geometry;

namespace KiCadSharp.Tests.Geometry;

/// <summary>
/// <see cref="CopperGeometry"/> against KiCad's own answer, pair by pair, on boards nobody here wrote.
/// </summary>
/// <remarks>
/// <para>
/// <c>data/oracles/*.distances.json</c> holds what pcbnew 10.0.6 itself says the gap is between
/// thousands of pairs of copper items — pads of every shape, tracks, arcs, vias — found by bisecting
/// the clearance argument of <c>GetEffectiveShape(layer).Collide(other, clearance)</c> to 0.1 µm.
/// KiCad is the arbiter of the geometry here, exactly as it is of a pin's position in orbion-kicad:
/// a model written from first principles and checked only against itself would agree with itself.
/// </para>
/// <para>
/// What the tolerance allows, and why it is not looser: the bisection lands within 0.1 µm, and this
/// library covers a track arc with capsules grown by its chord error (0.5 µm by default), so it may
/// read LOW by about that much and must never read HIGH — a clearance check that errs can only err
/// safe. So: never more than 1 µm above KiCad on any pair, never more than 1.5 µm below it on a pair
/// without an arc.
/// </para>
/// <para>
/// <b>An arc pair is allowed 5.5 µm below KiCad, because KiCad is the one approximating there.</b>
/// MEASURED on SNEdge: eight arc/arc pairs of its length-tuning meanders read 1.7 to 3.5 µm below
/// KiCad. Shrinking this library's chord error 25-fold, to 0.02 µm, moved them to 2.0 to 2.5 µm and
/// no further — so the residue is not ours. Sampling both centre lines 4,000 times over with no
/// library code at all gives 1.080561 mm for the first pair; this library says 1.08052 and KiCad
/// 1.08252. KiCad collides arcs as polylines within its <c>ARC_HIGH_DEF</c> of 5 µm, drawn inside
/// the arc, so it reads them HIGH by up to that. <see cref="AnArcPairIsExactWhereKiCadIsNot"/> pins
/// the exact value.
/// </para>
/// <para>
/// <b>A chamfered pad with rounded corners is allowed the same, for the same reason.</b> KiCad's
/// effective shape for it is a polygon, not primitives: MEASURED on <c>pad-shapes.kicad_pcb</c>, pad
/// P15 comes back as 14 vertices at the pad's <c>GetMaxError()</c> of 5 µm with
/// <c>ERROR_INSIDE</c> — every vertex on the true arc, every chord sagging inside it — and the pairs
/// against it read up to 4.4 µm below KiCad, all at rounded corners. A plain round rectangle is
/// drawn exactly by KiCad and is held to the ordinary tolerance.
/// </para>
/// </remarks>
public class CopperGeometryOracleTests
{
    /// <summary>
    /// Each oracle, and the kinds of copper it must have measured — so an oracle regenerated thinner
    /// fails here rather than quietly proving less. <c>pad-shapes.kicad_pcb</c> is a board pcbnew
    /// built itself to carry the shapes no vendored board has: trapezoid, chamfered with and without
    /// rounding, custom with a filled and a stroked primitive, and copper offset from its hole — each
    /// at 0, 30 and 90 degrees, on both sides.
    /// </summary>
    public static TheoryData<string, string, string[]> Oracles => new()
    {
        { "SNEdge.distances.json", TestData.SNEdgeBoard, ["pad rect", "pad roundrect", "segment", "arc", "via"] },
        { "bitaxeGamma.distances.json", TestData.BitaxeGammaBoard, ["pad rect", "pad roundrect", "pad circle", "pad oval", "segment", "via"] },
        {
            "pad-shapes.distances.json", Path.Combine(TestData.Root, "oracles", "pad-shapes.kicad_pcb"),
            ["pad trapezoid", "pad chamfered_rect", "pad custom", "pad oval", "pad rect", "segment", "via"]
        },
    };

    [Theory]
    [MemberData(nameof(Oracles))]
    public void EveryMeasuredGapMatchesKiCad(string oracle, string boardPath, string[] mustCover)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestData.Root, "oracles", oracle)));
        var pairs = document.RootElement.GetProperty("pairs").EnumerateArray().ToList();
        Assert.True(pairs.Count >= 700, $"{oracle} holds {pairs.Count} pairs; a thin oracle proves little.");

        var shapes = Shapes(KiCadBoard.Load(boardPath));
        var worst = 0.0;
        var failures = new List<string>();
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            var a = pair.GetProperty("a").GetString()!;
            var b = pair.GetProperty("b").GetString()!;
            var expected = pair.GetProperty("distance_mm").GetDouble();
            Assert.True(shapes.ContainsKey(a), $"{a} ({pair.GetProperty("a_kind").GetString()}) is not an item this library found");
            Assert.True(shapes.ContainsKey(b), $"{b} ({pair.GetProperty("b_kind").GetString()}) is not an item this library found");
            kinds.Add(shapes[a].Kind);
            kinds.Add(shapes[b].Kind);

            var actual = shapes[a].Shape.DistanceTo(shapes[b].Shape);
            var error = actual - expected;
            worst = Math.Max(worst, Math.Abs(error));
            var arc = shapes[a].Kind is "arc" or "pad chamfered_rect" || shapes[b].Kind is "arc" or "pad chamfered_rect";
            if (error > 0.001 || error < (arc ? -0.0055 : -0.0015))
            {
                failures.Add($"{shapes[a].Kind} {a} / {shapes[b].Kind} {b}: KiCad {expected:F5}, here {actual:F5}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} of {pairs.Count} gaps disagree with KiCad (worst {worst * 1000:F2} um):\n"
            + string.Join("\n", failures.Take(20)));
        Assert.Empty(mustCover.Except(kinds));
    }

    /// <summary>
    /// The first arc pair KiCad reads high, against the value computed with no library code at all.
    /// </summary>
    [Fact]
    public void AnArcPairIsExactWhereKiCadIsNot()
    {
        var shapes = Shapes(KiCadBoard.Load(TestData.SNEdgeBoard));

        var gap = shapes["2b761f67-0b47-477f-8f00-5c10bf0bf770"].Shape
            .DistanceTo(shapes["dd956225-30ef-490c-95b4-078f41d5697b"].Shape);

        Assert.InRange(gap, 1.080561 - (CopperGeometry.DefaultMaxError * 2) - 0.0001, 1.080561 + 0.0001);
    }

    /// <summary>Every copper item on the board by the UUID the file gives it.</summary>
    internal static Dictionary<string, (string Kind, CopperShape Shape)> Shapes(KiCadBoard board)
    {
        var shapes = new Dictionary<string, (string, CopperShape)>(StringComparer.Ordinal);
        foreach (var footprint in board.Footprints)
        {
            foreach (var pad in footprint.Pads)
            {
                if (pad.Node.GetChild("uuid")?.GetValue(0) is { } id)
                {
                    shapes[id] = (CopperGeometry.IsChamfered(pad) ? "pad chamfered_rect" : $"pad {pad.Shape}", CopperGeometry.Pad(footprint, pad));
                }
            }
        }

        foreach (var s in board.Segments)
        {
            shapes[s.Uuid!] = ("segment", CopperGeometry.Segment(s));
        }

        foreach (var a in board.TrackArcs)
        {
            shapes[a.Uuid!] = ("arc", CopperGeometry.Arc(a));
        }

        foreach (var v in board.Vias)
        {
            shapes[v.Uuid!] = ("via", CopperGeometry.Via(v));
        }

        return shapes;
    }
}
