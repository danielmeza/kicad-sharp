namespace KiCadSharp.Tests;

public class KiCadUtilsTests
{
    [Fact]
    public void ValidateSymbolLibrary_AcceptsARealLibrary() =>
        Assert.True(KiCadUtils.ValidateSymbolLibrary(TestData.SymbolLibrary));

    [Fact]
    public void ValidateSymbolLibrary_RejectsAFootprint() =>
        Assert.False(KiCadUtils.ValidateSymbolLibrary(TestData.Footprint));

    [Fact]
    public void ValidateSymbolLibrary_RejectsAMissingFile() =>
        Assert.False(KiCadUtils.ValidateSymbolLibrary(Path.Combine(TestData.NewScratchDirectory(), "absent.kicad_sym")));

    [Fact]
    public void ParseSymbolLibrary_ReadsTheHeader()
    {
        var library = KiCadUtils.ParseSymbolLibrary(TestData.SymbolLibrary);

        Assert.Equal("20241209", library.Version);
        Assert.Equal("orbion-extract-symbols", library.Generator);
        Assert.Equal(35, library.Symbols.Count);
    }

    [Fact]
    public void GetLibraryName_IsTheFileStem() =>
        Assert.Equal("orbion", KiCadUtils.GetLibraryName(TestData.SymbolLibrary));

    // ---------------------------------------------------------------------------- the footprint half

    [Fact]
    public void ParseFootprintLibrary_ReadsAStandaloneKicadMod()
    {
        var library = KiCadUtils.ParseFootprintLibrary(TestData.Footprint);

        Assert.True(library.IsSingleFootprint);
        Assert.Single(library.Footprints);
        Assert.Equal("LED_0603_1608Metric", library.Footprints[0].Id);
    }

    [Fact]
    public void ParseFootprintLibrary_ReadsABoard()
    {
        var library = KiCadUtils.ParseFootprintLibrary(TestData.PowerInputBoard);

        Assert.False(library.IsSingleFootprint);
        Assert.Equal(7, library.Footprints.Count);
    }

    [Fact]
    public void ParseFootprintLibrary_ThrowsForAMissingFile() =>
        Assert.Throws<FileNotFoundException>(
            () => KiCadUtils.ParseFootprintLibrary(Path.Combine(TestData.NewScratchDirectory(), "absent.kicad_mod")));

    [Fact]
    public void ValidateFootprintLibrary_AcceptsEveryRootKiCadWrites()
    {
        Assert.True(KiCadUtils.ValidateFootprintLibrary(TestData.Footprint));
        Assert.True(KiCadUtils.ValidateFootprintLibrary(TestData.PowerInputBoard));

        // KiCad 5 spelling, which carries no version token.
        var legacy = Path.Combine(TestData.NewScratchDirectory(), "legacy.kicad_mod");
        File.WriteAllText(legacy, "(module TEST (layer F.Cu))\n");
        Assert.True(KiCadUtils.ValidateFootprintLibrary(legacy));
    }

    [Fact]
    public void ValidateFootprintLibrary_RejectsASymbolLibraryAndAMissingFile()
    {
        Assert.False(KiCadUtils.ValidateFootprintLibrary(TestData.SymbolLibrary));
        Assert.False(KiCadUtils.ValidateFootprintLibrary(Path.Combine(TestData.NewScratchDirectory(), "absent.kicad_mod")));
    }

    [Fact]
    public void ValidateFootprintLibrary_RejectsAFootprintWithNoVersion()
    {
        var path = Path.Combine(TestData.NewScratchDirectory(), "noversion.kicad_mod");
        File.WriteAllText(path, "(footprint \"X\" (layer \"F.Cu\"))\n");

        Assert.False(KiCadUtils.ValidateFootprintLibrary(path));
    }

    [Fact]
    public void CloneFootprint_DoesNotTouchTheOriginal()
    {
        var original = KiCadUtils.ParseFootprintLibrary(TestData.Footprint).Footprints[0];

        var copy = KiCadUtils.CloneFootprint(original, "LED_0603_Copy");

        Assert.Equal("LED_0603_1608Metric", original.Id);
        Assert.Equal("LED_0603_Copy", copy.Id);
        Assert.Equal(2, copy.Pads.Count);
        Assert.Equal("LED", copy.Tags);
        Assert.Single(copy.Rectangles);
    }

    [Fact]
    public void ExportFootprintToFile_ReproducesTheFootprintsOwnBytes()
    {
        var footprint = KiCadUtils.ParseFootprintLibrary(TestData.Footprint).Footprints[0];
        var output = Path.Combine(TestData.NewScratchDirectory(), "LED.kicad_mod");

        KiCadUtils.ExportFootprintToFile(footprint, output);

        Assert.Equal(File.ReadAllBytes(TestData.Footprint), File.ReadAllBytes(output));
    }

    [Fact]
    public void ParseSymbolLibrary_KeepsTheWholeDocument()
    {
        // It used to go through SExpressionParser.ParseFile, which returns only the first top-level
        // form, so a save could not reproduce the file it came from.
        var library = KiCadUtils.ParseSymbolLibrary(TestData.SymbolLibrary);
        var output = Path.Combine(TestData.NewScratchDirectory(), "orbion.kicad_sym");

        library.Save(output);

        Assert.Equal(108_583, new FileInfo(output).Length);
        Assert.Equal(File.ReadAllBytes(TestData.SymbolLibrary), File.ReadAllBytes(output));
    }
}
