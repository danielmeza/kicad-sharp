using System.Diagnostics;

using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// A footprint placed on a board is written without a <c>(version …)</c>, <c>(generator …)</c> or
/// <c>(generator_version …)</c> of its own, as pcbnew writes it (<c>CTL_FOR_BOARD</c> carries
/// <c>CTL_OMIT_FOOTPRINT_VERSION</c>, <c>pcb_io_kicad_sexpr.h</c> line 223), so the board's version is
/// the one KiCad reads the whole file under. A footprint saved as a <c>.kicad_mod</c> keeps its
/// version (#73).
/// </summary>
/// <remarks>
/// <para>
/// KiCad 10.0.6's reader, on a <c>(version N)</c> inside a footprint on a board, keeps the greater of
/// the board's version and the footprint's for the rest of the board
/// (<c>pcb_io_kicad_sexpr_parser.cpp</c>, line 5046). MEASURED against kicad-cli 10.0.6
/// (<c>pcb upgrade</c>) before this change: <c>new KiCadBoard()</c> (<c>20241229</c>) holding
/// <c>new KiCadFootprint("Placed")</c> (<c>20260206</c>) and then a via came back with the via's
/// explicit <c>(capping no)</c>, <c>(covering …)</c>, <c>(plugging …)</c> and <c>(filling no)</c>
/// gone: from <c>20250228</c> on, a missing token means "inherit from the stackup" (line 7422). The
/// same board holding a KiCad 6 footprint (<c>20211014</c>) kept them. Both loaded, exit 0.
/// </para>
/// <para>
/// <see cref="KiCadReadsAViaAfterANewFootprint_UnderTheBoardsVersion"/> hands the board to KiCad
/// itself. It is opt-in, as every test that needs KiCad is: set <c>KICADSHARP_KICAD_CLI</c> to a
/// <c>kicad-cli</c>. Without it the test returns early — CI has no KiCad.
/// </para>
/// </remarks>
public class FootprintOnBoardTests
{
    /// <summary>A KiCad 6 footprint with the whole header a library footprint carries.</summary>
    private const string KiCad6Footprint = """
        (footprint "Old"
        	(version 20211014)
        	(generator pcbnew)
        	(generator_version "6.0")
        	(layer "F.Cu")
        	(fp_text reference "REF**"
        		(at 0 0)
        		(layer "F.SilkS")
        	)
        )

        """;

    /// <summary>The same footprint as pcbnew writes it inside a board: no header at all.</summary>
    private const string KiCad6FootprintOnABoard = """
        (footprint "Old"
        	(layer "F.Cu")
        	(fp_text reference "REF**"
        		(at 0 0)
        		(layer "F.SilkS")
        	)
        )
        """;

    // ------------------------------------------------------------------- placing drops the header

    [Fact]
    public void ANewFootprintAddedToABoard_LosesItsVersionAndGenerator()
    {
        var board = new KiCadBoard();

        var footprint = board.Footprints.Add(new KiCadFootprint("Placed"));

        Assert.Null(footprint.Version);
        Assert.Null(footprint.Node.GetChild(KiCadTokens.Common.Generator));
        Assert.StartsWith(
            "(footprint \"Placed\" (layer \"F.Cu\") (property \"Reference\" \"REF**\"",
            Flatten(footprint.Node.ToText()),
            StringComparison.Ordinal);
        Assert.Equal(KiCadDefaults.BoardVersion, board.Version);
        Assert.Single(AllVersionForms(board.Node));
    }

    [Fact]
    public void ANewFootprintInsertedOnABoard_LosesItsVersionToo()
    {
        var board = new KiCadBoard();

        board.Footprints.Insert(board.Node.Children.Count, new KiCadFootprint("Inserted"));

        var footprint = Assert.Single(board.Footprints);
        Assert.Null(footprint.Version);
        Assert.Null(footprint.Node.GetChild(KiCadTokens.Common.Generator));
        Assert.Single(AllVersionForms(board.Node));
    }

    /// <summary>
    /// The board-shaped library is a <c>kicad_pcb</c> too, and KiCad reads it as one: a version
    /// inside a footprint there raises the version the rest of the file is read under just the same.
    /// </summary>
    [Fact]
    public void ANewFootprintAddedToABoardShapedLibrary_LosesItsVersionToo()
    {
        var library = new KiCadFootprintLibrary();

        var footprint = library.AddFootprint(new KiCadFootprint("InLibrary"));

        Assert.Null(footprint.Version);
        Assert.Null(footprint.Node.GetChild(KiCadTokens.Common.Generator));
        Assert.Equal(KiCadDefaults.FootprintLibraryVersion, library.Version);
        Assert.Single(AllVersionForms(library.Node));
    }

    /// <summary>
    /// A footprint read from a <c>.kicad_mod</c> loses exactly the three header forms on its way onto
    /// the board, and nothing else: the rest of its bytes are the ones it was parsed from.
    /// </summary>
    [Fact]
    public void ALoadedFootprintMovedOntoABoard_LosesOnlyItsHeader()
    {
        var file = KiCadFootprintLibrary.Parse(KiCad6Footprint);
        var footprint = file.Footprints[0];
        var board = new KiCadBoard();

        board.Footprints.Add(footprint);

        Assert.Null(footprint.Version);
        Assert.Null(footprint.Node.GetChild(KiCadTokens.Common.Generator));
        Assert.Null(footprint.Node.GetChild(KiCadTokens.Common.GeneratorVersion));
        Assert.Equal(KiCad6FootprintOnABoard, footprint.Node.ToText());
        Assert.Empty(file.Footprints);
    }

    /// <summary>
    /// A footprint that carries no header, as every footprint on a board pcbnew wrote, moves with its
    /// bytes untouched.
    /// </summary>
    [Fact]
    public void AFootprintWithNoHeader_MovedOntoABoard_KeepsItsBytes()
    {
        var source = KiCadBoard.Load(TestData.Kicad10Board);
        var before = source.Footprints.Select(f => f.Node.ToText()).ToList();
        Assert.NotEmpty(before);
        var destination = new KiCadBoard();

        foreach (var footprint in source.Footprints)
        {
            destination.Footprints.Add(footprint);
        }

        Assert.Equal(before, destination.Footprints.Select(f => f.Node.ToText()));
        Assert.Empty(source.Footprints);
    }

    // ----------------------------------------------------------------- a .kicad_mod keeps its version

    [Fact]
    public void AFootprintSavedAsAKiCadMod_KeepsItsVersion()
    {
        using var scratch = TestData.NewScratchDirectory();
        var path = Path.Combine(scratch, "R_0603.kicad_mod");
        KiCadFootprintLibrary.SaveFootprint(new KiCadFootprint("R_0603"), path);

        Assert.Equal(KiCadDefaults.FootprintVersion, Assert.Single(KiCadFootprintLibrary.Load(path).Footprints).Version);
    }

    /// <summary>
    /// The header goes at the moment of placing, not by a view over the destination: the same node
    /// added to a <c>.kicad_mod</c>-shaped destination is refused as before, and a copy made before
    /// placing still has everything.
    /// </summary>
    [Fact]
    public void ACopyTakenBeforePlacing_KeepsItsVersion()
    {
        var footprint = new KiCadFootprint("Placed");
        var copy = footprint.CloneAs("Copy");

        new KiCadBoard().Footprints.Add(footprint);

        Assert.Null(footprint.Version);
        Assert.Equal(KiCadDefaults.FootprintVersion, copy.Version);
    }

    // --------------------------------------------------------------------------------- KiCad reads it

    /// <summary>
    /// The issue's board, built through the public API and handed to kicad-cli 10.0.6. Before #73 KiCad
    /// read the via after the footprint under the footprint's <c>20260206</c> and wrote it back with
    /// no <c>capping</c>, <c>covering</c>, <c>plugging</c> or <c>filling</c>: the via had lost its
    /// explicit "no" for each. Now it is read under the board's <c>20241229</c>, and KiCad writes
    /// those four back.
    /// </summary>
    [Fact]
    public void KiCadReadsAViaAfterANewFootprint_UnderTheBoardsVersion()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        using var scratch = TestData.NewScratchDirectory();
        var board = new KiCadBoard();
        board.Footprints.Add(new KiCadFootprint("Placed"));
        var via = board.Vias.Add();
        via.Position = new KiCadPosition(5, 5);
        via.Size = 0.6;
        via.Drill = 0.3;
        via.Layers = [KiCadLayerNames.FCu, KiCadLayerNames.BCu];
        via.Net = 0;
        var path = Path.Combine(scratch, "placed.kicad_pcb");
        board.Save(path);

        Run(cli, scratch, "pcb", "upgrade", "--force", path);

        // What KiCad understood, as it wrote it back.
        var upgraded = KiCadBoard.Load(path);
        Assert.Equal("pcbnew", upgraded.Generator);
        var footprint = Assert.Single(upgraded.Footprints);
        Assert.Null(footprint.Version);
        Assert.Equal("REF**", footprint.GetPropertyValue(KiCadPropertyNames.Reference));
        var read = Assert.Single(upgraded.Vias).Node;
        Assert.Equal("no", read.GetChild("capping")?.GetValue(0));
        Assert.Equal("no", read.GetChild("filling")?.GetValue(0));
        Assert.NotNull(read.GetChild("covering"));
        Assert.NotNull(read.GetChild("plugging"));
    }

    // ------------------------------------------------------------------------------------- helpers

    private static IEnumerable<SExpression> AllVersionForms(SExpression node) =>
        node.Children.Where(c => c.Token == KiCadTokens.Common.Version)
            .Concat(node.Children.SelectMany(AllVersionForms));

    private static string Flatten(string text) =>
        string.Join(' ', text.Split(['\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries))
            .Replace("( ", "(", StringComparison.Ordinal)
            .Replace(" )", ")", StringComparison.Ordinal);

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
