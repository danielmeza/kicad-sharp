using System.Diagnostics;

using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// A footprint built in memory carries the format <c>version</c> and the <c>generator</c> KiCad 10
/// writes, so KiCad reads it by the rules it was built for. A footprint read from a file keeps the
/// version it has (#63).
/// </summary>
/// <remarks>
/// <para>
/// KiCad's footprint reader starts every file at format 0 and raises it only when the footprint
/// declares a <c>version</c> (KiCad 10.0.6, <c>pcb_io_kicad_sexpr_parser.cpp</c> lines 104 and
/// 5039). Measured with kicad-cli 10.0.6 before this change: <c>new KiCadFootprint(id)</c> with one
/// <c>fp_arc</c> made <c>fp upgrade</c> and <c>fp export svg</c> exit 2, "Unable to load library",
/// because format 0 reads an arc as KiCad 5's <c>start</c>/<c>end</c>/<c>angle</c> (line 3262). The
/// same constructor with no arc loaded, and came back with <c>(attr through_hole)</c>: format 0
/// makes a footprint with no <c>attr</c> a through-hole one (line 5683).
/// </para>
/// <para>
/// <see cref="KiCadLoadsAFootprintBuiltInMemory"/> hands a new footprint to KiCad itself. It is
/// opt-in, as every test that needs KiCad is: set <c>KICADSHARP_KICAD_CLI</c> to a
/// <c>kicad-cli</c>. Without it the test returns early — CI has no KiCad.
/// </para>
/// </remarks>
public class FootprintVersionTests
{
    /// <summary>A KiCad 6 footprint, with its own stamp.</summary>
    private const string KiCad6Footprint = """
        (footprint "Old"
        	(version 20211014)
        	(generator pcbnew)
        	(layer "F.Cu")
        	(fp_text reference "REF**"
        		(at 0 0)
        		(layer "F.SilkS")
        	)
        )

        """;

    /// <summary>A KiCad 5 footprint, which has no version and draws its arc by centre and angle.</summary>
    private const string KiCad5Module = """
        (module Legacy (layer F.Cu) (tedit 5B307E4C)
          (fp_arc (start 0 0) (end 1 0) (angle 90) (layer F.SilkS) (width 0.12))
        )

        """;

    // ------------------------------------------------------------------------ a footprint built here

    /// <summary>
    /// The version and generator come first, in the order and the spelling KiCad 10.0.6's own writer
    /// uses: <c>(version 20260206)</c> as a bare number, then the generator as a quoted string
    /// (<c>pcb_io_kicad_sexpr.cpp</c>, lines 1210–1214).
    /// </summary>
    [Fact]
    public void ANewFootprint_StartsWithTheVersionAndGeneratorKiCad10Writes()
    {
        var footprint = new KiCadFootprint("R_0603");

        Assert.StartsWith(
            "(footprint \"R_0603\" (version 20260206) (generator \"KiCad Library Importer\") (layer \"F.Cu\") (property \"Reference\" \"REF**\"",
            Flatten(footprint.Node.ToText()),
            StringComparison.Ordinal);
        Assert.Equal(KiCadDefaults.FootprintVersion, footprint.Version);
    }

    /// <summary>
    /// The stamp is the one pcbnew 10 writes, read out of boards it wrote rather than out of this
    /// library's own constant. KiCad stamps a board and a footprint with the same
    /// <c>SEXPR_BOARD_FILE_VERSION</c> (<c>pcb_io_kicad_sexpr.cpp</c>, lines 338 and 1212).
    /// </summary>
    [Fact]
    public void TheStamp_IsTheOnePcbnew10Writes()
    {
        Assert.Equal(KiCadDefaults.FootprintVersion, KiCadBoard.Load(TestData.Kicad10Board).Version);
        Assert.Equal(KiCadDefaults.FootprintVersion, KiCadBoard.Load(TestData.SNEdgeBoard).Version);
    }

    [Fact]
    public void ANewFootprint_SavedAsAKiCadMod_ReadsBackItsVersionAndGenerator()
    {
        using var scratch = TestData.NewScratchDirectory();
        var path = Path.Combine(scratch, "R_0603.kicad_mod");
        KiCadFootprintLibrary.SaveFootprint(new KiCadFootprint("R_0603"), path);

        var library = KiCadFootprintLibrary.Load(path);

        Assert.True(library.IsSingleFootprint);
        Assert.Equal(KiCadDefaults.FootprintVersion, library.Version);
        Assert.Equal(KiCadDefaults.LibraryGenerator, library.Generator);
        Assert.Equal(KiCadDefaults.FootprintVersion, Assert.Single(library.Footprints).Version);
    }

    /// <summary>
    /// Whatever order the caller then builds in, the version stays first, and a version set later
    /// replaces the value rather than adding a second one.
    /// </summary>
    [Fact]
    public void ANewFootprint_KeepsOneVersion_First_WhenBuiltOnAndRestamped()
    {
        var footprint = new KiCadFootprint("F");
        var arc = footprint.Arcs.Add();
        arc.Layer = KiCadLayerNames.FSilkS;
        arc.Start = new KiCadPosition(0, 0);
        footprint.Version = "20241229";

        Assert.Equal("version", footprint.Node.Children[0].Token);
        Assert.Single(footprint.Node.GetChildren(KiCadTokens.Common.Version));
        Assert.Equal("20241229", footprint.Version);
    }

    /// <summary>
    /// <see langword="null"/> removes the version, and setting one on a footprint that has none puts
    /// it first, where KiCad reads it before anything its value changes.
    /// </summary>
    [Fact]
    public void Version_Null_RemovesIt_AndANewOneGoesFirst()
    {
        var footprint = new KiCadFootprint("F") { Version = null };
        footprint.Arcs.Add().Start = new KiCadPosition(0, 0);

        Assert.Null(footprint.Version);
        Assert.Null(footprint.Node.GetChild(KiCadTokens.Common.Version));

        footprint.Version = "20260206";

        Assert.Equal("version", footprint.Node.Children[0].Token);
    }

    // --------------------------------------------------------------------- a footprint read from a file

    public static TheoryData<string, string?> LoadedFootprints => new()
    {
        { KiCad6Footprint, "20211014" },
        { KiCad5Module, null },
    };

    /// <summary>
    /// A footprint read from a file reports the version it has, or none, and nothing here gives it
    /// another: the file still saves byte for byte. A KiCad 5 module has to stay without one, because
    /// KiCad reads its <c>start</c>/<c>end</c>/<c>angle</c> arc only under format 20210925 or older.
    /// </summary>
    [Theory]
    [MemberData(nameof(LoadedFootprints))]
    public void ALoadedFootprint_KeepsItsOwnVersion_AndSavesByteForByte(string text, string? version)
    {
        var library = KiCadFootprintLibrary.Parse(text);
        var footprint = Assert.Single(library.Footprints);

        Assert.Equal(version, footprint.Version);
        Assert.Equal(text, library.ToText());

        using var scratch = TestData.NewScratchDirectory();
        var path = Path.Combine(scratch, "loaded.kicad_mod");
        KiCadFootprintLibrary.SaveFootprint(footprint, path);
        Assert.Equal(text, File.ReadAllText(path));
    }

    [Fact]
    public void AFootprintKiCadWrote_KeepsItsVersion()
    {
        var bytes = File.ReadAllText(TestData.Footprint);
        var library = KiCadFootprintLibrary.Load(TestData.Footprint);

        Assert.Equal("20260206", Assert.Single(library.Footprints).Version);
        Assert.Equal(bytes, library.ToText());
    }

    /// <summary>
    /// KiCad writes a footprint inside a board without a version (<c>CTL_FOR_BOARD</c>,
    /// <c>pcb_io_kicad_sexpr.h</c> line 223), and reading one there adds none.
    /// </summary>
    [Fact]
    public void AFootprintOnABoardKiCadWrote_HasNoVersion_AndReadingItAddsNone()
    {
        var bytes = File.ReadAllText(TestData.Kicad10Board);
        var board = KiCadBoard.Load(TestData.Kicad10Board);

        Assert.NotEmpty(board.Footprints);
        Assert.All(board.Footprints, f => Assert.Null(f.Version));
        Assert.Equal(bytes, board.ToText());
    }

    /// <summary>A copy is the footprint it copies, version included: it is not built here.</summary>
    [Fact]
    public void ACopy_KeepsTheVersionOfTheFootprintItCopies()
    {
        var source = Assert.Single(KiCadFootprintLibrary.Parse(KiCad6Footprint).Footprints);

        Assert.Equal("20211014", source.CloneAs("Copy").Version);
        Assert.Equal("20211014", new KiCadFootprint(source.Node.Clone()).Version);
        Assert.Null(Assert.Single(KiCadFootprintLibrary.Parse(KiCad5Module).Footprints).CloneAs("Copy").Version);
    }

    // --------------------------------------------------------------------------------- KiCad reads it

    /// <summary>
    /// A footprint built entirely in memory, with a modern <c>fp_arc</c> and an SMD pad, handed to
    /// kicad-cli 10.0.6 as it comes out of <see cref="KiCadFootprintLibrary.SaveFootprint"/>. Before
    /// #63 both commands exited 2, "Unable to load library".
    /// </summary>
    [Fact]
    public void KiCadLoadsAFootprintBuiltInMemory()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        using var scratch = TestData.NewScratchDirectory();
        var footprint = new KiCadFootprint("InMemory");
        var arc = footprint.Arcs.Add();
        arc.Start = new KiCadPosition(0, 0);
        arc.Mid = new KiCadPosition(0.292893, 0.707107);
        arc.End = new KiCadPosition(1, 1);
        arc.Layer = KiCadLayerNames.FSilkS;
        arc.Width = 0.12;
        footprint.AddPad("1", "smd", "rect", -1, 0, 1, 1, [KiCadLayerNames.FCu, KiCadLayerNames.FPaste, KiCadLayerNames.FMask]);

        var pretty = Directory.CreateDirectory(Path.Combine(scratch, "InMemory.pretty")).FullName;
        KiCadFootprintLibrary.SaveFootprint(footprint, Path.Combine(pretty, "InMemory.kicad_mod"));

        var prettyOut = Path.Combine(scratch, "InMemoryOut.pretty");
        Run(cli, scratch, "fp", "upgrade", "--force", "-o", prettyOut, pretty);
        var svg = Directory.CreateDirectory(Path.Combine(scratch, "svg")).FullName;
        Run(cli, scratch, "fp", "export", "svg", "-o", svg, pretty);

        // What KiCad understood, as it wrote it back.
        var upgraded = KiCadFootprintLibrary.Load(Path.Combine(prettyOut, "InMemory.kicad_mod"));
        Assert.Equal("pcbnew", upgraded.Generator);
        var read = Assert.Single(upgraded.Footprints);
        Assert.Equal(new KiCadPosition(0.292893, 0.707107), Assert.Single(read.Arcs).Mid);
        Assert.Equal("smd", Assert.Single(read.Pads).Type);
        Assert.Empty(read.Attributes); // format 0 would have made it (attr through_hole)
        Assert.True(new FileInfo(Path.Combine(svg, "InMemory.svg")).Length > 0);
    }

    // ------------------------------------------------------------------------------------- helpers

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
