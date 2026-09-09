using KiCadSharp.Documents;

namespace KiCadSharp.Tests;

/// <summary>
/// The defaults a new document is built with, and the ones a document with no such token reports.
/// </summary>
/// <remarks>
/// <para>
/// Each document type used to carry its format stamp twice — once as its constructor's default
/// parameter and once as the fallback in its <c>Version</c> getter — with nothing making the two
/// agree. Both halves were spelled correctly, so nothing could see them disagree: a document built
/// with the default would report one version and a document parsed from a file with no
/// <c>version</c> token would report another, and only a reader comparing two files would notice.
/// </para>
/// <para>
/// These tests are what makes that impossible to reintroduce. They assert the two halves against
/// <see cref="KiCadDefaults"/> rather than against a literal, so a stamp is bumped in one place or
/// the suite fails.
/// </para>
/// </remarks>
public class DocumentDefaultsTests
{
    [Fact]
    public void ANewBoardCarriesTheDefaultStampPaperAndThickness()
    {
        var board = new KiCadBoard();

        Assert.Equal(KiCadDefaults.BoardVersion, board.Version);
        Assert.Equal(KiCadDefaults.Paper, board.Paper);
        Assert.Equal(
            KiCadDefaults.BoardThickness,
            board.Node.GetChild(KiCadTokens.Board.General)?.GetChildValue(KiCadTokens.Common.Thickness));
    }

    [Fact]
    public void ABoardWithNoVersionTokenReportsTheSameStampANewOneIsBuiltWith()
    {
        // The half that used to be able to drift. A file KiCad never stamped, and a file this
        // library just created, must not disagree about what format they are.
        var board = new KiCadBoard();
        board.Node.RemoveChild(KiCadTokens.Common.Version);

        Assert.Equal(KiCadDefaults.BoardVersion, board.Version);
    }

    [Fact]
    public void ANewFootprintLibraryCarriesItsOwnStamp()
    {
        var library = new KiCadFootprintLibrary();

        Assert.Equal(KiCadDefaults.FootprintLibraryVersion, library.Version);
        Assert.Equal(KiCadDefaults.Paper, library.Node.GetChildValue(KiCadTokens.Common.Paper));
    }

    [Fact]
    public void ANewSymbolLibraryCarriesItsOwnStamp() =>
        Assert.Equal(KiCadDefaults.SymbolLibraryVersion, new KiCadSymbolLibrary().Version);

    [Fact]
    public void TheGeneratedReferencePrefixIsWhatAPowerSymbolAnswersTo() =>
        Assert.StartsWith(
            KiCadDefaults.GeneratedReferencePrefix.ToString(),
            "#PWR001",
            StringComparison.Ordinal);
}
