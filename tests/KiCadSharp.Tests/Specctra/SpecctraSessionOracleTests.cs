using System.Globalization;

using KiCadSharp.Documents;
using KiCadSharp.Specctra;

namespace KiCadSharp.Tests.Specctra;

/// <summary>
/// <see cref="SpecctraSession"/> against the board pcbnew 10.0.6 makes of the same board and session.
/// </summary>
/// <remarks>
/// <para>
/// The fixtures, all in <c>data/oracles/</c>: <c>SNEdge-unrouted.kicad_pcb</c> is the vendored
/// SNEdge with every track and via removed except the VCC net's, which were LOCKED; its
/// <c>.dsn</c> is KiCad's export of it; <c>SNEdge-unrouted.ses</c> is what Freerouting 2.4.1 routed
/// from that design; and <c>SNEdge-kicad-import.kicad_pcb</c> is what <c>pcbnew.ImportSpecctraSES</c>
/// made of the board and the session — 71 tracks, 13 of them the locked ones kept, and 8 vias.
/// </para>
/// <para>
/// The comparison is of the copper, not of the bytes: every track by net, layer, width, ends and
/// lock; every via by net, place, size, drill, layers and lock, to 0.1 µm. The UUIDs are new on
/// both sides and are not compared.
/// </para>
/// </remarks>
public class SpecctraSessionOracleTests
{
    private static string Oracle(string name) => Path.Combine(TestData.Root, "oracles", name);

    [Fact]
    public void TheCopperIsTheCopperKiCadImports()
    {
        var board = KiCadBoard.Load(Oracle("SNEdge-unrouted.kicad_pcb"));
        var kicad = KiCadBoard.Load(Oracle("SNEdge-kicad-import.kicad_pcb"));

        var result = SpecctraSession.Parse(File.ReadAllText(Oracle("SNEdge-unrouted.ses"))).ApplyTo(board, viaDrill: 0.3);

        Assert.Empty(result.Skipped);
        Assert.Equal(Tracks(kicad), Tracks(board));
        Assert.Equal(Vias(kicad), Vias(board));
        Assert.Equal(13, Tracks(board).Count(t => t.EndsWith("locked", StringComparison.Ordinal)));
        Assert.Equal(8, board.Vias.Count);
    }

    [Fact]
    public void LockedCopperIsKeptAndTheRestReplaced()
    {
        var board = KiCadBoard.Load(Oracle("SNEdge-unrouted.kicad_pcb"));
        var locked = board.Segments.Count(s => s.Locked) + board.TrackArcs.Count(a => a.Locked) + board.Vias.Count(v => v.Locked);
        Assert.True(locked > 0, "the fixture must carry locked copper, or this proves nothing");

        var result = SpecctraSession.Parse(File.ReadAllText(Oracle("SNEdge-unrouted.ses"))).ApplyTo(board, viaDrill: 0.3);

        Assert.Equal(locked, result.Kept);
        Assert.Equal(0, result.Removed);

        // Applied again, the session's own copper is what is removed - and replaced by the same.
        var again = SpecctraSession.Parse(File.ReadAllText(Oracle("SNEdge-unrouted.ses"))).ApplyTo(board, viaDrill: 0.3);
        Assert.Equal(result.Tracks + result.Vias, again.Removed);
        Assert.Equal(locked, again.Kept);
    }

    [Fact]
    public void TheDrillComesFromThePadstackName() =>
        Assert.Equal(0.3, SpecctraSession.Parse("""
            (session s (routes (resolution um 10)
              (library_out (padstack "Via[0-1]_600:300_um" (shape (circle F.Cu 6000 0 0)) (shape (circle B.Cu 6000 0 0))))
              (network_out (net GND (via "Via[0-1]_600:300_um" 1000 -2000)))))
            """).Vias.Single().Drill);

    [Fact]
    public void ASessionWithNoRoutesIsRefused() =>
        Assert.Throws<FormatException>(() => SpecctraSession.Parse("(session s (placement))"));

    private static List<string> Tracks(KiCadBoard board) =>
        board.Segments.Select(s => string.Join(" ",
                SpecctraDesign.NetOf(board, s),
                s.Layer,
                Mm(s.Width),
                string.Join(" ", new[] { Mm(s.Start.X) + "," + Mm(s.Start.Y), Mm(s.End.X) + "," + Mm(s.End.Y) }.Order(StringComparer.Ordinal)),
                s.Locked ? "locked" : "free"))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static List<string> Vias(KiCadBoard board) =>
        board.Vias.Select(v => string.Join(" ",
                SpecctraDesign.NetOf(board, v),
                Mm(v.Position.X) + "," + Mm(v.Position.Y),
                Mm(v.Size),
                Mm(v.Drill),
                string.Join("/", v.Layers),
                v.ViaType,
                v.Locked ? "locked" : "free"))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>To 0.1 µm, which is the session's own resolution.</summary>
    private static string Mm(double value) => Math.Round(value, 4).ToString("0.0000", CultureInfo.InvariantCulture);
}
