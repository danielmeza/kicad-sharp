using KiCadSharp.Documents;
using KiCadSharp.Schematics;

namespace KiCadSharp.Tests;

/// <summary>
/// The defaults a new document is built with, the ones a document with no such token reports,
/// and the values themselves.
/// </summary>
/// <remarks>
/// <para>
/// Each document type used to carry its format stamp twice — once as its constructor's default
/// parameter and once as the fallback in its <c>Version</c> getter — with nothing making the two
/// agree. Both halves were spelled correctly, so nothing could see them disagree: a document
/// built with the default would report one version and a document parsed from a file with no
/// <c>version</c> token would report another.
/// </para>
/// <para>
/// <b>Two kinds of test, because one is not enough and the first draft only had one.</b> The
/// wiring tests below assert a document against <see cref="KiCadDefaults"/>, which pins that both
/// halves route through the constant — and is self-referential about the VALUE, so bumping a
/// stamp left the suite green. The pinning tests assert the literal, so a bump fails exactly one
/// obviously-named test and its reviewer sees the old and the new stamp side by side in the diff.
/// That is the review a format-version change deserves, and it did not exist: the only literal
/// assertions in the repo, <c>BoardDocumentTests.Board_ReadsTheFileHeader</c>, read a fixture
/// that carries all three tokens, so no default is exercised there at all.
/// </para>
/// </remarks>
public class DocumentDefaultsTests
{
    // ── The values themselves ────────────────────────────────────────────────
    // Deliberately literal. Changing a stamp is a decision about what KiCad opens, so it should
    // cost a test edit that shows both values to whoever reviews it.

    [Fact]
    public void TheBoardStampIsTheKiCad9Format() =>
        Assert.Equal("20241229", new KiCadBoard().Version);

    [Fact]
    public void TheFootprintLibraryStampIsTheKiCad6Format() =>
        Assert.Equal("20211014", new KiCadFootprintLibrary().Version);

    [Fact]
    public void TheSymbolLibraryStampIsTheKiCad6Format() =>
        Assert.Equal("20211014", new KiCadSymbolLibrary().Version);

    [Fact]
    public void ANewDocumentIsA4AndOnePointSixMillimetresThick()
    {
        var board = new KiCadBoard();

        Assert.Equal("A4", board.Paper);
        Assert.Equal(1.6, board.General!.Thickness);
    }

    [Fact]
    public void TheGeneratorNamesAreWhatEachDocumentTypeAnnounces()
    {
        Assert.Equal("KiCadSharp", new KiCadBoard().Generator);
        Assert.Equal("KiCad Library Importer", new KiCadFootprintLibrary().Generator);
        Assert.Equal("KiCad Library Importer", new KiCadSymbolLibrary().Generator);
    }

    // ── The wiring: both halves route through the constant ───────────────────

    [Fact]
    public void ANewBoardCarriesTheDefaultStampPaperAndThickness()
    {
        var board = new KiCadBoard();

        Assert.Equal(KiCadDefaults.BoardVersion, board.Version);
        Assert.Equal(KiCadDefaults.Paper, board.Paper);
        Assert.Equal(KiCadDefaults.BoardGenerator, board.Generator);
        Assert.Equal(
            KiCadDefaults.BoardThicknessMm,
            board.Node.GetChild(KiCadTokens.Board.General)?.GetChildValue(KiCadTokens.Common.Thickness)
                is { } text
                ? double.Parse(text, System.Globalization.CultureInfo.InvariantCulture)
                : double.NaN);
    }

    [Fact]
    public void ABoardWithNoVersionOrThicknessReportsTheSameValuesANewOneIsBuiltWith()
    {
        // The half that could drift. A file KiCad never stamped, and a file this library just
        // created, must not disagree about what format they are or how thick the board is.
        var board = new KiCadBoard();
        board.Node.RemoveChild(KiCadTokens.Common.Version);
        board.Node.GetChild(KiCadTokens.Board.General)?.RemoveChild(KiCadTokens.Common.Thickness);

        Assert.Equal(KiCadDefaults.BoardVersion, board.Version);
        Assert.Equal(KiCadDefaults.BoardThicknessMm, board.General!.Thickness);
    }

    [Fact]
    public void ALibraryWithNoVersionTokenReportsItsOwnStamp()
    {
        var footprints = new KiCadFootprintLibrary();
        footprints.Node.RemoveChild(KiCadTokens.Common.Version);

        Assert.Equal(KiCadDefaults.FootprintLibraryVersion, footprints.Version);
        Assert.Equal(KiCadDefaults.Paper, footprints.Node.GetChildValue(KiCadTokens.Common.Paper));
    }

    // ── The one line of behaviour the prefix constant changed ────────────────

    [Fact]
    public void APowerSymbolIsRecognisedByItsGeneratedReferencePrefix()
    {
        // The first version of this asserted a relation between the constant and a "#PWR001"
        // typed on the line below it, so it held whatever IsPowerSymbol did — review inverted
        // that property to EndsWith and all 200 tests still passed. Assert through the property
        // and the mutation dies.
        var hierarchy = SchematicHierarchy.Load(TestData.CopyDuplicateRefs(out _));

        var power = hierarchy.Placements.First(
            p => p.Symbol.ReferenceProperty?.StartsWith("#PWR", StringComparison.Ordinal) == true);
        var resistor = hierarchy.Placements.First(p => p.Symbol.ReferenceProperty == "R90");

        Assert.True(power.Symbol.IsPowerSymbol);
        Assert.False(resistor.Symbol.IsPowerSymbol);
    }
}
