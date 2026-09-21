using System.Diagnostics;
using System.Text.Json;

using KiCadSharp.Documents;
using KiCadSharp.Specctra;

namespace KiCadSharp.Tests.Specctra;

/// <summary>
/// The board a session import writes, handed to KiCad itself: it must load, and DRC must see the
/// same board it sees after KiCad's own import.
/// </summary>
/// <remarks>
/// Opt-in, like every test here that needs KiCad: set <c>KICADSHARP_KICAD_CLI</c> to a
/// <c>kicad-cli</c>. Without it the test returns early — CI has no KiCad.
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
        var ours = Path.Combine(scratch, "ours.kicad_pcb");
        var board = KiCadBoard.Load(Path.Combine(TestData.Root, "oracles", "SNEdge-unrouted.kicad_pcb"));
        SpecctraSession.Parse(File.ReadAllText(Path.Combine(TestData.Root, "oracles", "SNEdge-unrouted.ses"))).ApplyTo(board, viaDrill: 0.3);
        board.Save(ours);
        var theirs = Path.Combine(scratch, "theirs.kicad_pcb");
        File.Copy(Path.Combine(TestData.Root, "oracles", "SNEdge-kicad-import.kicad_pcb"), theirs);

        var (oursUnconnected, oursErrors) = Drc(cli, ours);
        var (theirsUnconnected, theirsErrors) = Drc(cli, theirs);

        Assert.Equal(theirsUnconnected, oursUnconnected);
        Assert.Equal(theirsErrors, oursErrors);
    }

    private static (int Unconnected, string Errors) Drc(string cli, string board)
    {
        var report = Path.ChangeExtension(board, ".json");
        var info = new ProcessStartInfo(cli) { WorkingDirectory = Path.GetDirectoryName(board)!, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "pcb", "drc", "--format", "json", "--severity-error", "-o", report, board })
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(File.Exists(report), $"kicad-cli wrote no report for {board} (exit {process.ExitCode}): KiCad could not load it");

        using var json = JsonDocument.Parse(File.ReadAllText(report));
        var errors = json.RootElement.GetProperty("violations").EnumerateArray()
            .Select(v => v.GetProperty("type").GetString()).Order(StringComparer.Ordinal);
        return (json.RootElement.GetProperty("unconnected_items").GetArrayLength(), string.Join(",", errors));
    }
}
