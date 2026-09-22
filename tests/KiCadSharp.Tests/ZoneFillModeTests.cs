using System.Diagnostics;

using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// A zone's fill mode is read and written in the words KiCad 10.0.6 uses (#72). Its parser takes
/// <c>(mode hatch)</c>, <c>(mode polygon)</c> and <c>(mode segment)</c>, and refuses the whole board
/// over anything else with "Expecting segment, hatch or polygon" (<c>pcb_io_kicad_sexpr_parser.cpp</c>,
/// lines 8020–8035). Its writer writes <c>(mode hatch)</c> for a hatched zone and nothing for a solid
/// one (<c>pcb_io_kicad_sexpr.cpp</c>, lines 2993–2995). <see cref="KiCadZoneFill.Mode"/> reads
/// <c>solid</c> for that nothing, so <c>"solid"</c> must write it back as nothing, not as
/// <c>(mode solid)</c>.
/// </summary>
/// <remarks>
/// <see cref="KiCadReadsEveryZoneMode"/> hands the result to KiCad itself. It is opt-in, as every test
/// that needs KiCad is: set <c>KICADSHARP_KICAD_CLI</c> to a <c>kicad-cli</c>. Without it the test
/// returns early, because CI has no KiCad.
/// </remarks>
public class ZoneFillModeTests
{
    /// <summary>The one zone's fill in <see cref="TestData.Kicad10Board"/>, which pcbnew 10.0.6 wrote: solid, so no <c>(mode …)</c>.</summary>
    private const string SolidFill =
        "\t\t(fill yes\n\t\t\t(thermal_gap 0.5)\n\t\t\t(thermal_bridge_width 0.5)\n\t\t\t(island_removal_mode 0)\n\t\t)";

    /// <summary>
    /// The same zone as pcbnew 10.0.6 saved it hatched (<c>kicad-cli pcb upgrade</c>): <c>(mode hatch)</c>
    /// first, and the hatch settings at the board's defaults after the rest.
    /// </summary>
    private const string HatchedFill =
        "\t\t(fill yes\n\t\t\t(mode hatch)\n\t\t\t(thermal_gap 0.5)\n\t\t\t(thermal_bridge_width 0.5)\n\t\t\t(island_removal_mode 0)\n" +
        "\t\t\t(hatch_thickness 1)\n\t\t\t(hatch_gap 1.5)\n\t\t\t(hatch_orientation 0)\n" +
        "\t\t\t(hatch_border_algorithm hatch_thickness)\n\t\t\t(hatch_min_hole_area 0.15)\n\t\t)";

    // --------------------------------------------------------------------------------- reading

    [Fact]
    public void ASolidZone_ReadsSolid()
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        Assert.Contains(SolidFill, board.ToText(), StringComparison.Ordinal);

        Assert.Equal("solid", board.Zones[0].Fill!.Mode);
    }

    [Theory]
    [InlineData("hatch")]
    [InlineData("polygon")]
    [InlineData("segment")]
    public void AZoneWithAMode_ReadsTheWordTheFileHas(string word)
    {
        var board = Board(HatchedFill.Replace("(mode hatch)", $"(mode {word})", StringComparison.Ordinal));

        Assert.Equal(word, board.Zones[0].Fill!.Mode);
    }

    // ------------------------------------------------------------------- writing back what was read

    [Fact]
    public void ASolidZone_WrittenBackWithItsOwnMode_KeepsItsBytes()
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        var before = board.ToText();
        var fill = board.Zones[0].Fill!;

        fill.Mode = fill.Mode;

        Assert.Equal(before, board.ToText());
        Assert.DoesNotContain("(mode solid)", board.ToText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hatch")]
    [InlineData("polygon")]
    [InlineData("segment")]
    public void AZoneWithAMode_WrittenBackWithItsOwnMode_KeepsItsBytes(string word)
    {
        var board = Board(HatchedFill.Replace("(mode hatch)", $"(mode {word})", StringComparison.Ordinal));
        var before = board.ToText();
        var fill = board.Zones[0].Fill!;

        fill.Mode = fill.Mode;

        Assert.Equal(before, board.ToText());
    }

    /// <summary>Every zone in every board fixture: 26 fills, none of them hatched, all byte for byte.</summary>
    [Fact]
    public void EveryLoadedZone_WrittenBackWithItsOwnMode_SavesByteForByte()
    {
        var fills = 0;
        foreach (var path in TestData.Boards)
        {
            var board = KiCadBoard.Load(path);
            var before = board.ToText();
            foreach (var fill in board.Zones.Select(z => z.Fill).OfType<KiCadZoneFill>())
            {
                fill.Mode = fill.Mode;
                fills++;
            }

            Assert.Equal(before, board.ToText());
        }

        Assert.Equal(26, fills);
    }

    /// <summary>
    /// A <c>(mode solid)</c> that another writer left, this library's included before #72, reads
    /// <c>solid</c> like any solid zone. Writing that back removes it, which is the form KiCad reads.
    /// </summary>
    [Fact]
    public void AModeSolidThatKiCadRefuses_WrittenBack_IsRemoved()
    {
        var board = Board(SolidFill.Replace("(fill yes\n", "(fill yes\n\t\t\t(mode solid)\n", StringComparison.Ordinal));
        var fill = board.Zones[0].Fill!;
        Assert.Equal("solid", fill.Mode);

        fill.Mode = fill.Mode;

        Assert.Equal(KiCadBoard.Load(TestData.Kicad10Board).ToText(), board.ToText());
    }

    // ------------------------------------------------------------------------------- setting a mode

    [Fact]
    public void SettingHatch_OnASolidZone_WritesModeHatchFirst_AsKiCadDoes()
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        var before = board.ToText();
        var fill = board.Zones[0].Fill!;

        fill.Mode = "hatch";

        var hatched = SolidFill.Replace("(fill yes\n", "(fill yes\n\t\t\t(mode hatch)\n", StringComparison.Ordinal);
        Assert.Equal(before.Replace(SolidFill, hatched, StringComparison.Ordinal), board.ToText());
        Assert.Equal("hatch", fill.Mode);
    }

    /// <summary>
    /// <c>solid</c> removes the <c>(mode …)</c> and nothing else. The hatch settings stay: KiCad reads
    /// them whatever the mode, and leaves them out of its own next save.
    /// </summary>
    [Fact]
    public void SettingSolid_OnAHatchedZone_RemovesOnlyTheMode()
    {
        var board = Board(HatchedFill);
        var before = board.ToText();
        var fill = board.Zones[0].Fill!;

        fill.Mode = "solid";

        Assert.Equal(before.Replace("\t\t\t(mode hatch)\n", string.Empty, StringComparison.Ordinal), board.ToText());
        Assert.Equal("solid", fill.Mode);
        Assert.Equal("1", fill.Node.GetChildValue("hatch_thickness"));
    }

    [Fact]
    public void SettingHatchThenSolid_OnASolidZone_GivesBackItsBytes()
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        var before = board.ToText();
        var fill = board.Zones[0].Fill!;

        fill.Mode = "hatch";
        fill.Mode = "solid";

        Assert.Equal(before, board.ToText());
    }

    [Theory]
    [InlineData("hatch", "polygon")]
    [InlineData("hatch", "segment")]
    [InlineData("polygon", "hatch")]
    [InlineData("segment", "hatch")]
    public void SettingAnotherWordKiCadReads_ChangesOnlyTheModesWord(string from, string to)
    {
        var board = Board(HatchedFill.Replace("(mode hatch)", $"(mode {from})", StringComparison.Ordinal));
        var before = board.ToText();

        board.Zones[0].Fill!.Mode = to;

        Assert.Equal(before.Replace($"(mode {from})", $"(mode {to})", StringComparison.Ordinal), board.ToText());
        Assert.Equal(to, board.Zones[0].Fill!.Mode);
    }

    [Theory]
    [InlineData("hatch")]
    [InlineData("polygon")]
    [InlineData("segment")]
    public void ANewZoneFill_TakesEveryWordKiCadReads(string word)
    {
        var fill = new KiCadZone().RequireFill();

        fill.Mode = word;

        Assert.Equal(word, SExpression.Parse(fill.Node.ToText()).GetChildValue("mode"));
        Assert.Equal(word, fill.Mode);

        fill.Mode = "solid";

        Assert.Null(SExpression.Parse(fill.Node.ToText()).GetChild("mode"));
        Assert.Equal("solid", fill.Mode);
    }

    [Fact]
    public void SettingSolid_OnANewZoneFill_WritesNothing()
    {
        var fill = new KiCadZone().RequireFill();

        fill.Mode = "solid";

        Assert.Equal("(fill)", fill.Node.ToText().Trim());
    }

    [Fact]
    public void ANewZoneFill_TurnedOnAfterItsMode_ReadsBoth()
    {
        var fill = new KiCadZone().RequireFill();

        fill.Mode = "hatch";
        fill.Enabled = true;

        var saved = SExpression.Parse(fill.Node.ToText());
        Assert.Equal(["yes"], saved.Values.ToArray());
        Assert.Equal("hatch", saved.GetChildValue("mode"));
    }

    // ------------------------------------------------------------------------ words KiCad refuses

    [Theory]
    [InlineData("hatched")]
    [InlineData("Hatch")]
    [InlineData("SOLID")]
    [InlineData("Solid")]
    [InlineData("none")]
    [InlineData("yes")]
    [InlineData("cross_hatch")]
    [InlineData("")]
    public void AWordKiCadRefuses_Throws_AndWritesNothing(string word)
    {
        var solid = KiCadBoard.Load(TestData.Kicad10Board);
        var solidBefore = solid.ToText();
        var hatched = Board(HatchedFill);
        var hatchedBefore = hatched.ToText();
        var added = new KiCadZone().RequireFill();

        Assert.Throws<ArgumentException>(() => solid.Zones[0].Fill!.Mode = word);
        Assert.Throws<ArgumentException>(() => hatched.Zones[0].Fill!.Mode = word);
        Assert.Throws<ArgumentException>(() => added.Mode = word);

        Assert.Equal(solidBefore, solid.ToText());
        Assert.Equal(hatchedBefore, hatched.ToText());
        Assert.Equal("(fill)", added.Node.ToText().Trim());
    }

    [Fact]
    public void ANullMode_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new KiCadZone().RequireFill().Mode = null!);
    }

    // --------------------------------------------------------------------------- KiCad reads them

    /// <summary>
    /// The fixture's zone in every mode the API takes, each on its own board, handed to
    /// <c>kicad-cli</c> 10: KiCad must load each board, and its own re-save must hold the fill meant.
    /// It writes a solid zone with no <c>(mode …)</c> and a hatched one with <c>(mode hatch)</c>
    /// (<c>pcb_io_kicad_sexpr.cpp</c>, lines 2993–2995).
    /// </summary>
    [Fact]
    public void KiCadReadsEveryZoneMode()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        using var scratch = TestData.NewScratchDirectory();
        (string Name, Func<KiCadBoard> Load, Action<KiCadZoneFill> Edit, string? Saved)[] cases =
        [
            ("solid-written-back", () => KiCadBoard.Load(TestData.Kicad10Board), f => f.Mode = f.Mode, null),
            ("solid", () => KiCadBoard.Load(TestData.Kicad10Board), f => f.Mode = "solid", null),
            ("hatch", () => KiCadBoard.Load(TestData.Kicad10Board), f => f.Mode = "hatch", "hatch"),
            ("polygon", () => KiCadBoard.Load(TestData.Kicad10Board), f => f.Mode = "polygon", null),
            ("segment", () => KiCadBoard.Load(TestData.Kicad10Board), f => f.Mode = "segment", null),
            ("hatched-written-back", () => Board(HatchedFill), f => f.Mode = f.Mode, "hatch"),
            ("hatched-to-solid", () => Board(HatchedFill), f => f.Mode = "solid", null),
        ];

        foreach (var (name, load, edit, saved) in cases)
        {
            var board = load();
            edit(board.Zones[0].Fill!);
            var path = Path.Combine(scratch, name + ".kicad_pcb");
            board.Save(path);

            Run(cli, scratch, "pcb", "upgrade", "--force", path);

            var upgraded = KiCadBoard.Load(path);
            Assert.Equal("pcbnew", upgraded.Generator); // KiCad loaded it and wrote it back itself
            var fill = Assert.Single(upgraded.Zones).Fill!;
            Assert.Equal(saved, fill.Node.GetChildValue("mode"));
            Assert.Equal(saved ?? "solid", fill.Mode);
            Assert.Equal(saved is not null, fill.Node.GetChild("hatch_thickness") is not null);
        }
    }

    // ------------------------------------------------------------------------------------- helpers

    /// <summary><see cref="TestData.Kicad10Board"/>, with its zone's fill replaced by <paramref name="fill"/>.</summary>
    private static KiCadBoard Board(string fill)
    {
        var text = KiCadBoard.Load(TestData.Kicad10Board).ToText();
        Assert.Contains(SolidFill, text, StringComparison.Ordinal);
        return KiCadBoard.Parse(text.Replace(SolidFill, fill, StringComparison.Ordinal));
    }

    private static void Run(string cli, string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo(cli) { WorkingDirectory = workingDirectory, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"kicad-cli {string.Join(' ', arguments)} exited {process.ExitCode}:\n{stdout.Result}\n{stderr}");
    }
}
