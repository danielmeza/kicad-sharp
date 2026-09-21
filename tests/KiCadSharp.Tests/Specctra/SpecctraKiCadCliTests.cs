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

        using var json = JsonDocument.Parse(File.ReadAllText(report));
        var errors = json.RootElement.GetProperty("violations").EnumerateArray()
            .Select(v => v.GetProperty("type").GetString()).Order(StringComparer.Ordinal);
        return (json.RootElement.GetProperty("unconnected_items").GetArrayLength(), string.Join(",", errors));
    }
}
