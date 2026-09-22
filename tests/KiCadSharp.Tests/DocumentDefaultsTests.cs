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

    /// <summary>
    /// A board-shaped library is a <c>kicad_pcb</c>, so it carries the board's stamp, and its
    /// footprints, which lose their own version on the way in (#73), are read under it. It used to
    /// be KiCad 6's <c>20211014</c>, under which KiCad hid a new footprint's Reference and Value.
    /// </summary>
    [Fact]
    public void TheFootprintLibraryStampIsTheBoardsKiCad9Format()
    {
        Assert.Equal("20241229", new KiCadFootprintLibrary().Version);
        Assert.Equal(new KiCadBoard().Version, new KiCadFootprintLibrary().Version);
    }

    [Fact]
    public void TheSymbolLibraryStampIsTheKiCad6Format() =>
        Assert.Equal("20211014", new KiCadSymbolLibrary().Version);

    [Fact]
    public void TheFootprintStampIsTheKiCad10Format() =>
        Assert.Equal("20260206", new KiCadFootprint("F").Version);

    [Fact]
    public void ANewDocumentIsA4AndOnePointSixMillimetresThick()
    {
        var board = new KiCadBoard();

        Assert.Equal("A4", board.Paper);
        Assert.Equal(1.6, board.General!.Thickness);
    }

    /// <summary>
    /// MEASURED against kicad-cli 10.0.6: a zone with no <c>(connect_pads …)</c> is re-saved by
    /// <c>pcb upgrade</c> with <c>(connect_pads (clearance 0.5))</c> (#102).
    /// </summary>
    [Fact]
    public void AZoneWithNoClearanceIsHalfAMillimetreFromItsPads() =>
        Assert.Equal(0.5, new KiCadZone().ConnectPadsClearance);

    [Fact]
    public void TheGeneratorNamesAreWhatEachDocumentTypeAnnounces()
    {
        Assert.Equal("KiCadSharp", new KiCadBoard().Generator);
        Assert.Equal("KiCad Library Importer", new KiCadFootprintLibrary().Generator);
        Assert.Equal("KiCad Library Importer", new KiCadSymbolLibrary().Generator);
        Assert.Equal("KiCad Library Importer", new KiCadFootprint("F").Node.GetChildValue(KiCadTokens.Common.Generator));
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

    /// <summary>
    /// A board with no thickness reports the thickness a new one is built with, which is also
    /// KiCad's own default, <c>DEFAULT_BOARD_THICKNESS_MM</c> (<c>include/board_design_settings.h</c>,
    /// line 56, at KiCad 10.0.6).
    /// </summary>
    [Fact]
    public void ABoardWithNoThicknessReportsTheThicknessANewOneIsBuiltWith()
    {
        var board = new KiCadBoard();
        board.Node.GetChild(KiCadTokens.Board.General)?.RemoveChild(KiCadTokens.Common.Thickness);

        Assert.Equal(KiCadDefaults.BoardThicknessMm, board.General!.Thickness);
    }

    /// <summary>
    /// The clearance has one half only, the getter's: KiCad writes a clearance on every zone, so no
    /// constructor here puts one in. A zone that lost its <c>(connect_pads …)</c> and a zone built in
    /// memory must still agree with each other, and with <see cref="KiCadZone.MinThickness"/>'s way
    /// of falling back to what KiCad reads.
    /// </summary>
    [Fact]
    public void AZoneWithNoConnectPadsReportsTheClearanceKiCadReads()
    {
        var board = KiCadBoard.Load(TestData.Kicad10Board);
        var zone = board.Zones[0];
        Assert.NotNull(zone.Node.GetChild(KiCadTokens.Board.ConnectPads));
        zone.Node.RemoveChild(KiCadTokens.Board.ConnectPads);

        Assert.Equal(KiCadDefaults.ZoneClearanceMm, zone.ConnectPadsClearance);
        Assert.Equal(KiCadDefaults.ZoneClearanceMm, new KiCadZone().ConnectPadsClearance);
    }

    /// <summary>
    /// The board and the symbol library no longer fall back to their constructors' stamps either, and
    /// no document type falls back to its generator name (#95). A file that declares neither reports
    /// neither, as <see cref="ALibraryWithNoVersionTokenReportsNone"/> does for the footprint library.
    /// </summary>
    [Fact]
    public void ADocumentWithNoVersionOrGeneratorReportsNone()
    {
        var board = new KiCadBoard();
        board.Node.RemoveChild(KiCadTokens.Common.Version);
        board.Node.RemoveChild(KiCadTokens.Common.Generator);
        var symbols = new KiCadSymbolLibrary();
        symbols.Node.RemoveChild(KiCadTokens.Common.Version);
        symbols.Node.RemoveChild(KiCadTokens.Common.Generator);
        var footprints = new KiCadFootprintLibrary();
        footprints.Node.RemoveChild(KiCadTokens.Common.Generator);

        Assert.Null(board.Version);
        Assert.Null(board.Generator);
        Assert.Null(symbols.Version);
        Assert.Null(symbols.Generator);
        Assert.Null(footprints.Generator);
    }

    [Fact]
    public void ANewFootprintCarriesTheFootprintStampAndTheLibraryGenerator()
    {
        var footprint = new KiCadFootprint("F");

        Assert.Equal(KiCadDefaults.FootprintVersion, footprint.Version);
        Assert.Equal(KiCadDefaults.LibraryGenerator, footprint.Node.GetChildValue(KiCadTokens.Common.Generator));
    }

    /// <summary>
    /// The first document type whose getter stopped falling back to its constructor's stamp (#76).
    /// KiCad reads a board-shaped file with no version as <c>20201115</c> and a <c>.kicad_mod</c>
    /// with none as format 0, so reporting <see cref="KiCadDefaults.FootprintLibraryVersion"/> there
    /// named a format neither the file nor KiCad uses.
    /// </summary>
    [Fact]
    public void ALibraryWithNoVersionTokenReportsNone()
    {
        var footprints = new KiCadFootprintLibrary();
        footprints.Node.RemoveChild(KiCadTokens.Common.Version);

        Assert.Null(footprints.Version);
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
        using var scratch = TestData.NewScratchDirectory();
        var hierarchy = SchematicHierarchy.Load(TestData.CopyDuplicateRefs(scratch));

        var power = hierarchy.Placements.First(
            p => p.Symbol.ReferenceProperty?.StartsWith("#PWR", StringComparison.Ordinal) == true);
        var resistor = hierarchy.Placements.First(p => p.Symbol.ReferenceProperty == "R90");

        Assert.True(power.Symbol.IsPowerSymbol);
        Assert.False(resistor.Symbol.IsPowerSymbol);
    }
}
