using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using KiCadSharp.Documents;
using KiCadSharp.Specctra;

namespace KiCadSharp.Tests.Specctra;

/// <summary>
/// The board a session import writes, handed to KiCad itself: it must load, and DRC must see the
/// same board it sees after KiCad's own import.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in, like every test here that needs KiCad: set <c>KICADSHARP_KICAD_CLI</c> to a
/// <c>kicad-cli</c>. Without it the test returns early — CI has no KiCad.
/// </para>
/// <para>
/// <b>The session's copper takes the UUIDs KiCad's import gave the same copper</b> (#65, #71).
/// KiCad's DRC puts two colliding items in UUID order. It reports a contact between an item with a
/// net and an item without one as <c>shorting_items</c> only when the item with the net comes
/// second, and as a <c>clearance</c> violation otherwise
/// (<c>pcbnew/drc/drc_test_provider_copper_clearance.cpp</c> at 10.0.6, lines 248 and 290). Both
/// imports, KiCad's and this one, give the new copper random UUIDs, and on SNEdge two new tracks
/// and a new via touch a copper graphic of J1 or JP1 that has no net. Left with UUIDs of its own,
/// the board matched the oracle's counts in 7 runs of 40. With KiCad's, the two boards differ only
/// in what this library wrote.
/// </para>
/// <para>
/// That rule is pinned by <see cref="KiCadTypesAContactWithNoNetCopperByUuidOrder"/> on the
/// smallest board that shows it. When that test fails, KiCad has changed the rule, and
/// <see cref="TakeKiCadsUuids"/> may no longer be needed.
/// </para>
/// <para>
/// Each kicad-cli call gets an empty <c>KICAD_CONFIG_HOME</c> of its own, so the report does not
/// depend on the settings of whoever runs the test. Unconnected items are compared by count only:
/// which items KiCad names for a missing connection changes from run to run on the same file.
/// </para>
/// </remarks>
public class SpecctraKiCadCliTests
{
    [Fact]
    public void KiCadLoadsTheImportAndCountsWhatItCountsOnItsOwn()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        var scratch = TestData.NewScratchDirectory();
        var theirs = Path.Combine(scratch, "theirs.kicad_pcb");
        File.Copy(Path.Combine(TestData.Root, "oracles", "SNEdge-kicad-import.kicad_pcb"), theirs);
        var ours = Path.Combine(scratch, "ours.kicad_pcb");
        var board = KiCadBoard.Load(Path.Combine(TestData.Root, "oracles", "SNEdge-unrouted.kicad_pcb"));
        SpecctraSession.Parse(File.ReadAllText(Path.Combine(TestData.Root, "oracles", "SNEdge-unrouted.ses"))).ApplyTo(board, viaDrill: 0.3);
        TakeKiCadsUuids(board, KiCadBoard.Load(theirs));
        board.Save(ours);

        var (oursUnconnected, oursErrors) = Drc(cli, ours);
        var (theirsUnconnected, theirsErrors) = Drc(cli, theirs);

        Assert.Equal(theirsUnconnected, oursUnconnected);
        Assert.Equal(theirsErrors, oursErrors);
    }

    /// <summary>
    /// The KiCad behaviour <see cref="TakeKiCadsUuids"/> exists for, on the smallest board that shows
    /// it (#71): one track on a net, ending on a footprint's copper polygon that has no net. The two
    /// boards differ only in which of the two items has the larger UUID.
    /// </summary>
    /// <remarks>
    /// MEASURED against kicad-cli 10.0.6: with the track's UUID below the polygon's, the contact is
    /// a <c>clearance</c> violation, "Clearance violation ( clearance 0.2000 mm; actual 0.0000 mm)";
    /// with it above, the same contact is <c>shorting_items</c>, "Items shorting two nets (nets
    /// &lt;no net&gt; and N1)". Same two items, same distance 0, only the type changes. A board-level
    /// <c>gr_line</c> on copper in place of the footprint polygon is not checked against the track
    /// at all: no violation in either order.
    /// </remarks>
    [Fact]
    public void KiCadTypesAContactWithNoNetCopperByUuidOrder()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        const string Low = "10000000-0000-4000-8000-000000000001";
        const string High = "f0000000-0000-4000-8000-000000000001";
        using var scratch = TestData.NewScratchDirectory();
        var trackLower = Path.Combine(scratch, "track-lower.kicad_pcb");
        var trackHigher = Path.Combine(scratch, "track-higher.kicad_pcb");
        File.WriteAllText(trackLower, TrackEndingOnNoNetCopper(track: Low, polygon: High));
        File.WriteAllText(trackHigher, TrackEndingOnNoNetCopper(track: High, polygon: Low));

        // KiCad names the two items in UUID order, so the pair reads the same both times.
        Assert.Equal([("clearance", $"{Low} {High}")], Violations(cli, trackLower));
        Assert.Equal([("shorting_items", $"{Low} {High}")], Violations(cli, trackHigher));
    }

    /// <summary>
    /// A two-layer board in KiCad 10's spelling: a 0.25 mm track on net <c>N1</c> from (5, 10) to
    /// (15, 10) on F.Cu, and a footprint at (20, 10) whose only copper is a filled <c>fp_poly</c>
    /// on no net from (15, 9.5) to (25, 10.5), so the track's end lies on its edge.
    /// </summary>
    private static string TrackEndingOnNoNetCopper(string track, string polygon) => $"""
        (kicad_pcb
        	(version 20260206)
        	(generator "pcbnew")
        	(generator_version "10.0")
        	(general
        		(thickness 1.6)
        		(legacy_teardrops no)
        	)
        	(paper "A4")
        	(layers
        		(0 "F.Cu" signal)
        		(2 "B.Cu" signal)
        		(25 "Edge.Cuts" user)
        	)
        	(setup
        		(pad_to_mask_clearance 0)
        	)
        	(gr_rect
        		(start 0 0)
        		(end 30 20)
        		(stroke
        			(width 0.05)
        			(type default)
        		)
        		(fill no)
        		(layer "Edge.Cuts")
        		(uuid "00000000-0000-4000-8000-000000000001")
        	)
        	(segment
        		(start 5 10)
        		(end 15 10)
        		(width 0.25)
        		(layer "F.Cu")
        		(net "N1")
        		(uuid "{track}")
        	)
        	(footprint "repro:NONET"
        		(layer "F.Cu")
        		(uuid "00000000-0000-4000-8000-000000000010")
        		(at 20 10)
        		(property "Reference" "J1"
        			(at 0 -2 0)
        			(layer "F.SilkS")
        			(uuid "00000000-0000-4000-8000-000000000011")
        			(effects
        				(font
        					(size 1 1)
        					(thickness 0.15)
        				)
        			)
        		)
        		(property "Value" "NONET"
        			(at 0 2 0)
        			(layer "F.Fab")
        			(uuid "00000000-0000-4000-8000-000000000012")
        			(effects
        				(font
        					(size 1 1)
        					(thickness 0.15)
        				)
        			)
        		)
        		(attr through_hole)
        		(fp_poly
        			(pts
        				(xy -5 -0.5) (xy 5 -0.5) (xy 5 0.5) (xy -5 0.5)
        			)
        			(stroke
        				(width 0)
        				(type solid)
        			)
        			(fill yes)
        			(layer "F.Cu")
        			(uuid "{polygon}")
        		)
        	)
        )

        """;

    /// <summary>Every violation in the DRC report of <paramref name="board"/>: its type and the UUIDs of its items, as KiCad orders them.</summary>
    private static List<(string Type, string Items)> Violations(string cli, string board)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(RunDrc(cli, board)));
        return json.RootElement.GetProperty("violations").EnumerateArray()
            .Select(v => (v.GetProperty("type").GetString()!, string.Join(" ", v.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("uuid").GetString()))))
            .ToList();
    }

    /// <summary>
    /// Gives every track, arc and via on <paramref name="board"/> the UUID of the same copper on
    /// <paramref name="kicad"/>: the same net, layer, width and ends, or for a via the same net,
    /// position, size, drill and layers.
    /// </summary>
    private static void TakeKiCadsUuids(KiCadBoard board, KiCadBoard kicad)
    {
        var uuids = Copper(kicad)
            .GroupBy(item => Describe(kicad, item), item => item.Uuid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new Queue<string?>(g), StringComparer.Ordinal);

        foreach (var item in Copper(board))
        {
            var copper = Describe(board, item);
            Assert.True(uuids.TryGetValue(copper, out var left) && left.Count > 0, $"KiCad's import has no {copper}");
            item.Uuid = left.Dequeue();
        }
    }

    private static List<KiCadTrackItem> Copper(KiCadBoard board) =>
        board.Segments.Concat<KiCadTrackItem>(board.TrackArcs).Concat(board.Vias).ToList();

    private static string Describe(KiCadBoard board, KiCadTrackItem item) => item switch
    {
        KiCadTrackSegment s => $"track {SpecctraDesign.NetOf(board, s)} {s.Layer} {Mm(s.Width)} {Ends(s.Start, s.End)}",
        KiCadTrackArc a => $"arc {SpecctraDesign.NetOf(board, a)} {a.Layer} {Mm(a.Width)} {Ends(a.Start, a.End)} through {Point(a.Mid)}",
        KiCadVia v => $"via {SpecctraDesign.NetOf(board, v)} {Point(v.Position)} {Mm(v.Size)} {Mm(v.Drill)} {string.Join("/", v.Layers)}",
        _ => throw new ArgumentOutOfRangeException(nameof(item), item.GetType().Name, "Not a track, arc or via."),
    };

    private static string Ends(KiCadPosition start, KiCadPosition end) =>
        string.Join(" ", new[] { Point(start), Point(end) }.Order(StringComparer.Ordinal));

    private static string Point(KiCadPosition p) => Mm(p.X) + "," + Mm(p.Y);

    /// <summary>To 0.1 µm, which is the session's own resolution.</summary>
    private static string Mm(double value) => Math.Round(value, 4).ToString("0.0000", CultureInfo.InvariantCulture);

    private static (int Unconnected, string Errors) Drc(string cli, string board)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(RunDrc(cli, board)));
        var errors = json.RootElement.GetProperty("violations").EnumerateArray()
            .Select(v => v.GetProperty("type").GetString()).Order(StringComparer.Ordinal);
        return (json.RootElement.GetProperty("unconnected_items").GetArrayLength(), string.Join(",", errors));
    }

    /// <summary>Runs <c>pcb drc --severity-error</c> on <paramref name="board"/> and returns the path of its JSON report.</summary>
    private static string RunDrc(string cli, string board)
    {
        var report = Path.ChangeExtension(board, ".json");
        var info = new ProcessStartInfo(cli) { WorkingDirectory = Path.GetDirectoryName(board)!, RedirectStandardError = true, RedirectStandardOutput = true };
        info.Environment["KICAD_CONFIG_HOME"] = Directory.CreateDirectory(Path.ChangeExtension(board, ".kicad-config")).FullName;
        foreach (var arg in new[] { "pcb", "drc", "--format", "json", "--severity-error", "-o", report, board })
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(File.Exists(report), $"kicad-cli wrote no report for {board} (exit {process.ExitCode}): KiCad could not load it.\n{stdout.Result}\n{stderr}");
        return report;
    }
}
