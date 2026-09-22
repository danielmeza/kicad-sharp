using System.Diagnostics;

using KiCadSharp.Documents;

namespace KiCadSharp.Tests;

/// <summary>
/// <c>Version</c> and <c>Generator</c> on a board, a footprint library and a symbol library report
/// what the file declares, and <see langword="null"/> when it declares nothing (#95).
/// </summary>
/// <remarks>
/// <para>
/// They used to report the stamp and name a new document is written with. A board with no version
/// said <c>20241229</c> and a symbol library said <c>20211014</c>, formats neither the file nor KiCad
/// uses. KiCad 10.0.6 reads a board's or a symbol library's version only as the first child. When the
/// first child is something else, it assumes a version (<c>20201115</c> for a board, <c>20251024</c>
/// for a library) and loses that child. <see cref="KiCadDoesNotReadAFileWithNoVersion_UntilItIsGivenOne"/>
/// measures that, which is why neither <c>Version</c> can be set to <see langword="null"/>.
/// </para>
/// <para>
/// A board-shaped <see cref="KiCadFootprintLibrary"/> is the same file, a <c>kicad_pcb</c>, so its
/// <c>Version</c> cannot be set to <see langword="null"/> either (#109). A <c>.kicad_mod</c> can: a
/// footprint's parser reads <c>version</c> anywhere, and reads a footprint with none as format 0.
/// <see cref="KiCadDoesNotReadABoardShapedLibraryWithNoVersion_TheFileVersionNullUsedToWrite"/>
/// measures the board-shaped case.
/// </para>
/// <para>
/// The kicad-cli test is opt-in, as every test that needs KiCad is: set <c>KICADSHARP_KICAD_CLI</c>
/// to a <c>kicad-cli</c>. Without it the test returns early, because CI has no KiCad.
/// </para>
/// </remarks>
public class DeclaredVersionTests
{
    private const string Layers = "(layers (0 \"F.Cu\" signal) (2 \"B.Cu\" signal) (25 \"Edge.Cuts\" user))";

    private const string Rectangle = "(gr_rect (start 0 0) (end 10 10) (stroke (width 0.1) (type solid)) (layer \"Edge.Cuts\"))";

    private const string Symbol = "(symbol \"S\" (property \"Reference\" \"U\" (at 0 0 0) (effects (font (size 1.27 1.27)))) (property \"Value\" \"~\" (at 0 0 0) (effects (font (size 1.27 1.27)))))";

    // ---------------------------------------------------------------------- what a file declares

    public static TheoryData<string> BoardsWithNoVersion => new()
    {
        $"(kicad_pcb (generator \"x\") (general (thickness 1.6)) {Layers} {Rectangle})",
        $"(kicad_pcb (general) {Layers})",
        "(kicad_pcb)",
    };

    [Theory]
    [MemberData(nameof(BoardsWithNoVersion))]
    public void ABoardWithNoVersion_ReportsNone_AndReadingItChangesNothing(string text)
    {
        var board = KiCadBoard.Parse(text);

        Assert.Null(board.Version);
        Assert.Equal(text, board.ToText());
    }

    public static TheoryData<string> SymbolLibrariesWithNoVersion => new()
    {
        $"(kicad_symbol_lib (generator \"x\") {Symbol})",
        $"(kicad_symbol_lib {Symbol})",
        "(kicad_symbol_lib)",
    };

    [Theory]
    [MemberData(nameof(SymbolLibrariesWithNoVersion))]
    public void ASymbolLibraryWithNoVersion_ReportsNone_AndReadingItChangesNothing(string text)
    {
        var library = KiCadSymbolLibrary.Parse(text);

        Assert.Null(library.Version);
        Assert.Equal(text, library.ToText());
    }

    [Fact]
    public void AFileThatNamesNoGenerator_ReportsNone()
    {
        const string Board = "(kicad_pcb (version 20241229) (general))";
        const string Library = "(kicad_symbol_lib (version 20251024))";
        const string Module = "(module Legacy (layer F.Cu) (tedit 5B307E4C))";
        const string Footprint = "(footprint \"F\" (version 20260206) (layer \"F.Cu\"))";

        Assert.Null(KiCadBoard.Parse(Board).Generator);
        Assert.Null(KiCadSymbolLibrary.Parse(Library).Generator);
        Assert.Null(KiCadFootprintLibrary.Parse(Module).Generator);
        Assert.Null(KiCadFootprintLibrary.Parse(Footprint).Generator);
        Assert.Equal(Board, KiCadBoard.Parse(Board).ToText());
    }

    /// <summary>Files KiCad and other tools wrote report exactly what they declare, and keep their bytes.</summary>
    [Fact]
    public void AFileThatDeclaresBoth_ReportsThem_AndKeepsItsBytes()
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var footprints = KiCadFootprintLibrary.Load(TestData.Footprint);

        Assert.Equal(("20260206", "pcbnew"), (board.Version, board.Generator));
        Assert.Equal(("20241209", "orbion-extract-symbols"), (library.Version, library.Generator));
        Assert.Equal(("20260206", "kicad-footprint-generator"), (footprints.Version, footprints.Generator));

        Assert.Equal(File.ReadAllText(TestData.Kicad10Board), board.ToText());
        Assert.Equal(File.ReadAllText(TestData.SymbolLibrary), library.ToText());
        Assert.Equal(File.ReadAllText(TestData.Footprint), footprints.ToText());
    }

    // ------------------------------------------------------------------------------------ setting

    [Fact]
    public void AVersionCannotBeSetToNone_OnABoardOrASymbolLibrary()
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var boardBefore = board.ToText();
        var libraryBefore = library.ToText();

        Assert.Throws<ArgumentNullException>(() => board.Version = null!);
        Assert.Throws<ArgumentNullException>(() => library.Version = null!);

        Assert.Equal(boardBefore, board.ToText());
        Assert.Equal(libraryBefore, library.ToText());
    }

    /// <summary>
    /// A board-shaped footprint library is a <c>kicad_pcb</c>, and KiCad reads its version the same
    /// way, so it refuses <see langword="null"/> as <see cref="KiCadBoard.Version"/> does (#109). It
    /// used to remove the version, which wrote a board KiCad 10.0.6 refuses.
    /// </summary>
    [Theory]
    [InlineData("built")]
    [InlineData("loaded")]
    public void AVersionCannotBeSetToNone_OnABoardShapedFootprintLibrary(string origin)
    {
        var library = origin == "built" ? NewLibraryHoldingOneFootprint() : KiCadFootprintLibrary.Load(TestData.Kicad10Board);
        Assert.False(library.IsSingleFootprint);
        var before = library.ToText();

        Assert.Throws<ArgumentNullException>(() => library.Version = null);

        Assert.Equal(before, library.ToText());
        Assert.NotNull(library.Version);
    }

    /// <summary>
    /// A <c>.kicad_mod</c> keeps the behaviour #76 gave it: <see langword="null"/> removes the
    /// version, for a KiCad 6+ <c>footprint</c> root and a KiCad 5 <c>module</c> root alike.
    /// </summary>
    [Theory]
    [InlineData("(footprint \"F\" (version 20211014) (layer \"F.Cu\"))", "(footprint \"F\" (layer \"F.Cu\"))")]
    [InlineData("(module M (version 20211014) (layer F.Cu))", "(module M (layer F.Cu))")]
    public void AVersionSetToNone_OnAKiCadMod_StillRemovesIt(string text, string expected)
    {
        var library = KiCadFootprintLibrary.Parse(text);
        Assert.True(library.IsSingleFootprint);

        library.Version = null;

        Assert.Null(library.Version);
        Assert.Null(Assert.Single(library.Footprints).Version);
        Assert.Equal(expected, library.ToText());
    }

    [Fact]
    public void AVersionSetOnAFileWithNone_GoesFirst()
    {
        var board = KiCadBoard.Parse($"(kicad_pcb (generator \"x\") (general (thickness 1.6)) {Layers})");
        var library = KiCadSymbolLibrary.Parse($"(kicad_symbol_lib {Symbol})");

        board.Version = "20241229";
        library.Version = "20251024";

        Assert.Equal("20241229", board.Version);
        Assert.Equal(KiCadTokens.Common.Version, board.Node.Children[0].Token);
        Assert.Equal("20251024", library.Version);
        Assert.Equal(KiCadTokens.Common.Version, library.Node.Children[0].Token);
    }

    [Fact]
    public void AGeneratorSetToNone_IsRemoved_AndOneSetOnAFileWithNone_IsAdded()
    {
        var board = new KiCadBoard();
        var library = new KiCadSymbolLibrary();
        var footprints = new KiCadFootprintLibrary();

        board.Generator = null;
        library.Generator = null;
        footprints.Generator = null;

        Assert.Null(board.Node.GetChild(KiCadTokens.Common.Generator));
        Assert.Null(library.Node.GetChild(KiCadTokens.Common.Generator));
        Assert.Null(footprints.Node.GetChild(KiCadTokens.Common.Generator));

        board.Generator = "a";
        library.Generator = "b";
        footprints.Generator = "c";

        Assert.Equal(("a", "b", "c"), (board.Generator, library.Generator, footprints.Generator));
    }

    // --------------------------------------------------------------------------- KiCad reads it

    /// <summary>
    /// A board and a symbol library that declare no version, each with a <c>(generator …)</c> first,
    /// handed to kicad-cli 10.0.6. KiCad reads neither as written: it refuses both. With the
    /// <see cref="KiCadBoard.Version"/> or <see cref="KiCadSymbolLibrary.Version"/> set through the
    /// API, which puts it first, KiCad loads each file and keeps what is in it.
    /// </summary>
    [Fact]
    public void KiCadDoesNotReadAFileWithNoVersion_UntilItIsGivenOne()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        var scratch = TestData.NewScratchDirectory();

        var board = KiCadBoard.Parse($"(kicad_pcb (generator \"x\") (general (thickness 1.6)) (paper \"A4\") {Layers} {Rectangle})");
        Assert.Null(board.Version);
        var boardPath = Path.Combine(scratch, "board.kicad_pcb");
        board.Save(boardPath);
        var (boardExit, boardErrors) = Run(cli, scratch, "pcb", "upgrade", "--force", boardPath);
        Assert.NotEqual(0, boardExit);
        Assert.Contains("Failed to load board", boardErrors, StringComparison.Ordinal);

        board.Version = "20241229";
        board.Save(boardPath);
        Assert.Equal(0, Run(cli, scratch, "pcb", "upgrade", "--force", boardPath).Exit);
        var upgraded = KiCadBoard.Load(boardPath);
        Assert.Equal("pcbnew", upgraded.Generator); // KiCad loaded it and wrote it back itself
        Assert.Single(upgraded.GraphicRectangles);

        var library = KiCadSymbolLibrary.Parse($"(kicad_symbol_lib (generator \"x\") {Symbol})");
        Assert.Null(library.Version);
        var libraryPath = Path.Combine(scratch, "library.kicad_sym");
        var libraryOut = Path.Combine(scratch, "library.upgraded.kicad_sym");
        library.Save(libraryPath);
        Assert.NotEqual(0, Run(cli, scratch, "sym", "upgrade", "--force", "-o", libraryOut, libraryPath).Exit);

        library.Version = "20251024";
        library.Save(libraryPath);
        Assert.Equal(0, Run(cli, scratch, "sym", "upgrade", "--force", "-o", libraryOut, libraryPath).Exit);
        var symbol = Assert.Single(KiCadSymbolLibrary.Load(libraryOut).Symbols);
        Assert.Equal("~", symbol.GetPropertyValue(KiCadPropertyNames.Value)); // read as 20251024, where ~ is ~
    }

    /// <summary>
    /// The three files a board-shaped <see cref="KiCadFootprintLibrary"/> holding one footprint can
    /// be saved as, handed to kicad-cli 10.0.6 (#109). Each version-less file is written by removing
    /// the token from the tree, which is what <c>Version = null</c> did before this test.
    /// <list type="bullet">
    /// <item><c>(generator …)</c> first, the file <c>Version = null</c> wrote: refused, "Expecting '('".</item>
    /// <item><c>(general)</c> first, the file <c>Generator = null</c> then <c>Version = null</c> wrote:
    /// loads, but KiCad's re-save has no footprint. The empty form ended the board.</item>
    /// <item>The version the setter now keeps: loads, and the footprint is kept.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void KiCadDoesNotReadABoardShapedLibraryWithNoVersion_TheFileVersionNullUsedToWrite()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        using var scratch = TestData.NewScratchDirectory();

        var generatorFirst = Path.Combine(scratch, "generator-first.kicad_pcb");
        var library = NewLibraryHoldingOneFootprint();
        library.Node.RemoveChild(KiCadTokens.Common.Version);
        Assert.Null(library.Version);
        library.Save(generatorFirst);
        var (exit, errors) = Run(cli, scratch, "pcb", "upgrade", "--force", generatorFirst);
        Assert.NotEqual(0, exit);
        Assert.Contains("Failed to load board", errors, StringComparison.Ordinal);
        Assert.Contains("Expecting ''(''", errors, StringComparison.Ordinal);

        var generalFirst = Path.Combine(scratch, "general-first.kicad_pcb");
        library = NewLibraryHoldingOneFootprint();
        library.Generator = null;
        library.Node.RemoveChild(KiCadTokens.Common.Version);
        library.Save(generalFirst);
        Assert.Equal(0, Run(cli, scratch, "pcb", "upgrade", "--force", generalFirst).Exit);
        var truncated = KiCadBoard.Load(generalFirst);
        Assert.Equal("pcbnew", truncated.Generator); // KiCad loaded it and wrote it back itself
        Assert.Empty(truncated.Footprints);

        var stamped = Path.Combine(scratch, "stamped.kicad_pcb");
        library = NewLibraryHoldingOneFootprint();
        Assert.Throws<ArgumentNullException>(() => library.Version = null);
        Assert.Equal(KiCadDefaults.FootprintLibraryVersion, library.Version);
        library.Save(stamped);
        Assert.Equal(0, Run(cli, scratch, "pcb", "upgrade", "--force", stamped).Exit);
        var kept = KiCadBoard.Load(stamped);
        Assert.Equal("pcbnew", kept.Generator);
        Assert.Equal("F", Assert.Single(kept.Footprints).GetPropertyValue(KiCadPropertyNames.Value));
    }

    private static KiCadFootprintLibrary NewLibraryHoldingOneFootprint()
    {
        var library = new KiCadFootprintLibrary();
        library.AddFootprint(new KiCadFootprint("F"));
        return library;
    }

    private static (int Exit, string Errors) Run(string cli, string workingDirectory, params string[] arguments)
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
        return (process.ExitCode, stdout.Result + stderr);
    }
}
