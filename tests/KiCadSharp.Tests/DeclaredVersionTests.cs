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
