using System.Diagnostics;

using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// A zone's pad connection (#89) and outline hatch (#90) are written the way KiCad 10.0.6 reads them.
/// </summary>
/// <remarks>
/// <para>
/// <c>(connect_pads …)</c>: KiCad writes <c>yes</c>, <c>no</c>, <c>thru_hole_only</c>, or no word for
/// thermal reliefs (<c>pcb_io_kicad_sexpr.cpp</c>, lines 2925–2949), and its parser refuses any other
/// word, <c>""</c> included, with "Expecting yes, no, or clearance" (<c>pcb_io_kicad_sexpr_parser.cpp</c>,
/// lines 7959–7989). <see cref="KiCadZone.ConnectPadsMode"/> reads <c>""</c> for no word, so writing
/// that back must write no word.
/// </para>
/// <para>
/// <c>(hatch …)</c>: KiCad writes <c>(hatch none|edge|full pitch)</c> (<c>pcb_io_kicad_sexpr.cpp</c>,
/// lines 2898–2910), and its parser needs one of those words and then the pitch (lines 7936–7952).
/// A zone with no <c>(hatch …)</c> reads <c>none</c> and 0.5 mm (7868, 7870).
/// </para>
/// <para>
/// <see cref="KiCadReadsEveryPadConnectionAndHatch"/> hands the results to KiCad itself. It is opt-in,
/// as every test that needs KiCad is: set <c>KICADSHARP_KICAD_CLI</c> to a <c>kicad-cli</c>.
/// </para>
/// </remarks>
public class ZonePadsAndHatchTests
{
    /// <summary>The fixture's zone hatch, as pcbnew 10.0.6 wrote it.</summary>
    private const string Hatch = "\t\t(hatch edge 0.5)\n";

    /// <summary>The fixture's zone pad connection, as pcbnew 10.0.6 wrote it: thermal reliefs, so no word.</summary>
    private const string ConnectPads = "\t\t(connect_pads\n\t\t\t(clearance 0.5)\n\t\t)\n";

    // --------------------------------------------------------------------- connect_pads: reading

    [Fact]
    public void AThermalZone_ReadsNoWord_AndWritingItBack_KeepsItsBytes()
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        var before = board.ToText();
        Assert.Contains(ConnectPads, before, StringComparison.Ordinal);
        var zone = board.Zones[0];
        Assert.Equal(string.Empty, zone.ConnectPadsMode);

        zone.ConnectPadsMode = zone.ConnectPadsMode;

        Assert.Equal(before, board.ToText());
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("thru_hole_only")]
    public void AZoneWithAWord_ReadsIt_AndWritingItBack_KeepsItsBytes(string word)
    {
        var board = Board(WithWord(word));
        var before = board.ToText();
        var zone = board.Zones[0];
        Assert.Equal(word, zone.ConnectPadsMode);

        zone.ConnectPadsMode = zone.ConnectPadsMode;

        Assert.Equal(before, board.ToText());
    }

    /// <summary>
    /// Every zone in every board fixture, each of its pad connection, hatch style and hatch pitch
    /// written back: 26 zones, 14 of them <c>(connect_pads yes …)</c>, all byte for byte.
    /// </summary>
    [Fact]
    public void EveryLoadedZone_WrittenBack_SavesByteForByte()
    {
        var zones = 0;
        var solid = 0;
        foreach (var path in TestData.Boards)
        {
            var board = KiCadBoard.Load(path);
            var before = board.ToText();
            foreach (var zone in board.Zones)
            {
                zone.ConnectPadsMode = zone.ConnectPadsMode;
                zone.HatchStyle = zone.HatchStyle;
                zone.HatchPitch = zone.HatchPitch;
                zones++;
                solid += zone.ConnectPadsMode == "yes" ? 1 : 0;
            }

            Assert.Equal(before, board.ToText());
        }

        Assert.Equal(26, zones);
        Assert.Equal(14, solid);
    }

    // --------------------------------------------------------------------- connect_pads: setting

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("thru_hole_only")]
    public void EveryWordKiCadReads_IsWrittenBeforeTheClearance_AsKiCadWritesIt(string word)
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        var before = board.ToText();

        board.Zones[0].ConnectPadsMode = word;

        Assert.Equal(before.Replace(ConnectPads, WithWord(word), StringComparison.Ordinal), board.ToText());
        Assert.Equal(word, board.Zones[0].ConnectPadsMode);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("thru_hole_only")]
    public void SettingNoWord_RemovesOnlyTheWord(string word)
    {
        var board = Board(WithWord(word));

        board.Zones[0].ConnectPadsMode = string.Empty;

        Assert.Equal(KiCadBoard.Load(TestData.Kicad10Board).ToText(), board.ToText());
        Assert.Equal(0.5, board.Zones[0].ConnectPadsClearance);
    }

    /// <summary>
    /// <c>(connect_pads "" …)</c>, which this library wrote before #89 when given back the
    /// <c>""</c> it read, reads <c>""</c>. Writing that back removes it, and KiCad reads the result.
    /// </summary>
    [Fact]
    public void AQuotedEmptyWordThatKiCadRefuses_WrittenBack_IsRemoved()
    {
        var board = Board("\t\t(connect_pads \"\"\n\t\t\t(clearance 0.5)\n\t\t)\n");
        var zone = board.Zones[0];
        Assert.Equal(string.Empty, zone.ConnectPadsMode);

        zone.ConnectPadsMode = zone.ConnectPadsMode;

        Assert.Equal(KiCadBoard.Load(TestData.Kicad10Board).ToText(), board.ToText());
    }

    [Fact]
    public void OnAZoneWithNoConnectPads_NoWordWritesNothing_AndAWordWritesOnlyItself()
    {
        var zone = new KiCadZone();

        zone.ConnectPadsMode = string.Empty;

        Assert.Null(zone.Node.GetChild("connect_pads"));
        Assert.Equal(string.Empty, zone.ConnectPadsMode);

        zone.ConnectPadsMode = "yes";

        var pads = SExpression.Parse(zone.Node.ToText()).GetChild("connect_pads")!;
        Assert.Equal(["yes"], pads.Values.ToArray());
        Assert.Empty(pads.Children);
    }

    [Theory]
    [InlineData("thermal")]
    [InlineData("Yes")]
    [InlineData("full")]
    [InlineData("none")]
    [InlineData("solid")]
    [InlineData("thru_hole")]
    [InlineData(" ")]
    public void AWordKiCadRefuses_AsAPadConnection_Throws_AndWritesNothing(string word)
    {
        var thermal = KiCadBoard.Load(TestData.Kicad10Board);
        var thermalBefore = thermal.ToText();
        var solid = Board(WithWord("yes"));
        var solidBefore = solid.ToText();
        var added = new KiCadZone();

        Assert.Throws<ArgumentException>(() => thermal.Zones[0].ConnectPadsMode = word);
        Assert.Throws<ArgumentException>(() => solid.Zones[0].ConnectPadsMode = word);
        Assert.Throws<ArgumentException>(() => added.ConnectPadsMode = word);

        Assert.Equal(thermalBefore, thermal.ToText());
        Assert.Equal(solidBefore, solid.ToText());
        Assert.Equal("(zone)", added.Node.ToText().Trim());
    }

    [Fact]
    public void ANullPadConnection_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new KiCadZone().ConnectPadsMode = null!);
    }

    // ----------------------------------------------------------------------------------- hatch

    [Theory]
    [InlineData("none")]
    [InlineData("edge")]
    [InlineData("full")]
    public void EveryHatchStyleKiCadReads_ChangesOnlyTheStyle(string style)
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        var before = board.ToText();

        board.Zones[0].HatchStyle = style;

        Assert.Equal(before.Replace(Hatch, $"\t\t(hatch {style} 0.5)\n", StringComparison.Ordinal), board.ToText());
        Assert.Equal(style, board.Zones[0].HatchStyle);
    }

    [Fact]
    public void HatchPitch_ChangesOnlyThePitch()
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        var before = board.ToText();

        board.Zones[0].HatchPitch = 0.3;

        Assert.Equal(before.Replace(Hatch, "\t\t(hatch edge 0.3)\n", StringComparison.Ordinal), board.ToText());
        Assert.Equal(0.3, board.Zones[0].HatchPitch);
    }

    [Fact]
    public void AZoneWithNoHatch_ReadsWhatKiCadReads()
    {
        var zone = new KiCadZone();

        Assert.Equal("none", zone.HatchStyle);
        Assert.Equal(0.5, zone.HatchPitch);
        Assert.Equal("(zone)", zone.Node.ToText().Trim());
    }

    [Fact]
    public void SettingTheStyle_OnAZoneWithNoHatch_WritesAWholeHatch_WithKiCadsPitch()
    {
        var zone = new KiCadZone();

        zone.HatchStyle = "edge";

        Assert.Equal(["edge", "0.5"], Saved(zone).Values.ToArray());
        Assert.Equal(0.5, zone.HatchPitch);
    }

    [Fact]
    public void SettingThePitch_OnAZoneWithNoHatch_WritesAWholeHatch_WithKiCadsStyle()
    {
        var zone = new KiCadZone();

        zone.HatchPitch = 0.3;

        Assert.Equal(["none", "0.3"], Saved(zone).Values.ToArray());
        Assert.Equal("none", zone.HatchStyle);
    }

    /// <summary>
    /// <c>(hatch edge)</c>, which this library wrote before #90 when only the style was set, has no
    /// pitch, and KiCad refuses it. Writing the style back fills in the pitch KiCad would have used.
    /// </summary>
    [Fact]
    public void AHatchWithNoPitch_WrittenBack_GetsKiCadsPitch()
    {
        var board = Board(ConnectPads, "\t\t(hatch edge)\n");
        var zone = board.Zones[0];
        Assert.Equal(0.5, zone.HatchPitch);

        zone.HatchStyle = zone.HatchStyle;

        Assert.Equal(KiCadBoard.Load(TestData.Kicad10Board).ToText(), board.ToText());
    }

    [Theory]
    [InlineData("diagonal")]
    [InlineData("Edge")]
    [InlineData("NONE")]
    [InlineData("hatch")]
    [InlineData("")]
    public void AWordKiCadRefuses_AsAHatchStyle_Throws_AndWritesNothing(string word)
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        var before = board.ToText();
        var added = new KiCadZone();

        Assert.Throws<ArgumentException>(() => board.Zones[0].HatchStyle = word);
        Assert.Throws<ArgumentException>(() => added.HatchStyle = word);

        Assert.Equal(before, board.ToText());
        Assert.Equal("(zone)", added.Node.ToText().Trim());
    }

    [Fact]
    public void ANullHatchStyle_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new KiCadZone().HatchStyle = null!);
    }

    // ------------------------------------------------------------------------------- clearance

    /// <summary>
    /// A zone with no <c>(connect_pads …)</c> used to read a clearance of 0, which KiCad never uses
    /// (#102). Setting the clearance just read writes the <c>(connect_pads (clearance 0.5))</c> KiCad
    /// itself writes for such a zone: no word, so thermal reliefs, and the clearance. It goes at
    /// the end of the zone, where a child KiCad reads in any order goes, not after the
    /// <c>(hatch …)</c> where pcbnew puts it.
    /// </summary>
    [Fact]
    public void AZoneWithNoConnectPads_ReadsTheClearanceKiCadReads_AndWritingItBack_WritesWhatKiCadWrites()
    {
        var board = Board(string.Empty);
        var zone = board.Zones[0];
        Assert.Null(zone.Node.GetChild("connect_pads"));
        Assert.Equal(0.5, zone.ConnectPadsClearance);
        Assert.Equal(0.5, new KiCadZone().ConnectPadsClearance);

        zone.ConnectPadsClearance = zone.ConnectPadsClearance;

        var pads = SExpression.Parse(zone.Node.ToText()).GetChild("connect_pads")!;
        Assert.Empty(pads.Values);
        var clearance = Assert.Single(pads.Children);
        Assert.Equal("clearance", clearance.Token);
        Assert.Equal(["0.5"], clearance.Values.ToArray());
        Assert.Equal(string.Empty, zone.ConnectPadsMode);
        Assert.Equal(0.5, zone.ConnectPadsClearance);
    }

    // --------------------------------------------------------------------------- KiCad reads them

    /// <summary>
    /// The fixture's zone with its <c>(connect_pads …)</c> removed, handed to <c>kicad-cli</c> 10:
    /// KiCad's own re-save must carry the clearance <see cref="KiCadZone.ConnectPadsClearance"/>
    /// read before the re-save. The second board has a legacy <c>(setup (zone_clearance 0.3))</c>,
    /// which KiCad reads as the board's default instead; a zone view does not know its board, so it
    /// reads 0.5 there, and that exception is documented on the property rather than resolved.
    /// </summary>
    [Fact]
    public void KiCadReadsHalfAMillimetre_ForAZoneWithNoClearance()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        var scratch = TestData.NewScratchDirectory();
        var bare = Board(string.Empty);
        var legacy = KiCadBoard.Parse(bare.ToText().Replace("\t(setup\n", "\t(setup\n\t\t(zone_clearance 0.3)\n", StringComparison.Ordinal));
        Assert.Contains("(zone_clearance 0.3)", legacy.ToText(), StringComparison.Ordinal);
        (string Name, KiCadBoard Board, double Expected)[] cases =
        [
            ("no-clearance", bare, 0.5),
            ("legacy-clearance", legacy, 0.3),
        ];

        foreach (var (name, board, expected) in cases)
        {
            Assert.Equal(0.5, board.Zones[0].ConnectPadsClearance);
            var path = Path.Combine(scratch, name + ".kicad_pcb");
            board.Save(path);

            Run(cli, scratch, "pcb", "upgrade", "--force", path);

            var upgraded = KiCadBoard.Load(path);
            Assert.Equal("pcbnew", upgraded.Generator);
            var zone = Assert.Single(upgraded.Zones);
            Assert.Equal((name, expected), (name, zone.ConnectPadsClearance));
            Assert.NotNull(zone.Node.GetChild("connect_pads")?.GetChild("clearance"));
        }
    }

    /// <summary>
    /// The fixture's zone with each pad connection and hatch the API writes, each on its own board,
    /// handed to <c>kicad-cli</c> 10: KiCad must load each board, and its own re-save must hold the
    /// value meant. The last cases start from a zone with neither <c>(hatch …)</c> nor
    /// <c>(connect_pads …)</c>, as a zone built in memory has.
    /// </summary>
    [Fact]
    public void KiCadReadsEveryPadConnectionAndHatch()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        var scratch = TestData.NewScratchDirectory();
        Func<KiCadBoard> fixture = () => KiCadBoard.Load(TestData.Kicad10Board);
        Func<KiCadBoard> bare = () => Board(string.Empty, string.Empty);
        (string Name, Func<KiCadBoard> Load, Action<KiCadZone> Edit, string Pads, string Style, double Pitch)[] cases =
        [
            ("pads-written-back", fixture, z => z.ConnectPadsMode = z.ConnectPadsMode, "", "edge", 0.5),
            ("pads-yes", fixture, z => z.ConnectPadsMode = "yes", "yes", "edge", 0.5),
            ("pads-no", fixture, z => z.ConnectPadsMode = "no", "no", "edge", 0.5),
            ("pads-thru-hole-only", fixture, z => z.ConnectPadsMode = "thru_hole_only", "thru_hole_only", "edge", 0.5),
            ("pads-yes-then-thermal", fixture, z => { z.ConnectPadsMode = "yes"; z.ConnectPadsMode = ""; }, "", "edge", 0.5),
            ("hatch-written-back", fixture, z => { z.HatchStyle = z.HatchStyle; z.HatchPitch = z.HatchPitch; }, "", "edge", 0.5),
            ("hatch-none", fixture, z => z.HatchStyle = "none", "", "none", 0.5),
            ("hatch-full-1mm", fixture, z => { z.HatchStyle = "full"; z.HatchPitch = 1; }, "", "full", 1),
            ("bare-written-back", bare, z => { z.ConnectPadsMode = z.ConnectPadsMode; z.HatchStyle = z.HatchStyle; }, "", "none", 0.5),
            ("bare-style", bare, z => z.HatchStyle = "edge", "", "edge", 0.5),
            ("bare-pitch", bare, z => z.HatchPitch = 0.3, "", "none", 0.3),
            ("bare-pads-yes", bare, z => z.ConnectPadsMode = "yes", "yes", "none", 0.5),
        ];

        foreach (var (name, load, edit, pads, style, pitch) in cases)
        {
            var board = load();
            edit(board.Zones[0]);
            var path = Path.Combine(scratch, name + ".kicad_pcb");
            board.Save(path);

            Run(cli, scratch, "pcb", "upgrade", "--force", path);

            var upgraded = KiCadBoard.Load(path);
            Assert.Equal("pcbnew", upgraded.Generator); // KiCad loaded it and wrote it back itself
            var zone = Assert.Single(upgraded.Zones);
            Assert.Equal((name, pads, style, pitch), (name, zone.ConnectPadsMode, zone.HatchStyle, zone.HatchPitch));
        }
    }

    // ------------------------------------------------------------------------------------- helpers

    /// <summary>The fixture's <c>(connect_pads …)</c> with <paramref name="word"/> where KiCad writes one.</summary>
    private static string WithWord(string word) =>
        ConnectPads.Replace("(connect_pads\n", $"(connect_pads {word}\n", StringComparison.Ordinal);

    /// <summary>
    /// <see cref="TestData.Kicad10Board"/>, with its zone's <c>(connect_pads …)</c> replaced by
    /// <paramref name="connectPads"/> and its <c>(hatch …)</c> by <paramref name="hatch"/>.
    /// </summary>
    private static KiCadBoard Board(string connectPads, string hatch = Hatch)
    {
        var text = KiCadBoard.Load(TestData.Kicad10Board).ToText();
        Assert.Contains(Hatch + ConnectPads, text, StringComparison.Ordinal);
        return KiCadBoard.Parse(text.Replace(Hatch + ConnectPads, hatch + connectPads, StringComparison.Ordinal));
    }

    /// <summary>The zone's <c>(hatch …)</c> as its saved text reads back.</summary>
    private static SExpression Saved(KiCadZone zone) =>
        SExpression.Parse(zone.Node.ToText()).GetChild("hatch") ?? throw new InvalidOperationException($"no hatch in {zone.Node.ToText()}");

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
