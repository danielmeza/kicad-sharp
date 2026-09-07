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
}
