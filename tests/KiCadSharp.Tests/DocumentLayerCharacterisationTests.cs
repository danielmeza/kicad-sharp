using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// What the document layer does <em>today</em>, measured — not what it should do.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here is a bug from the README's "Pending" section, pinned with the number it
/// actually produces. They exist so the fix has something to flip: the change that reworks the
/// document layer rewrites this file, and the diff is the before/after.
/// </para>
/// <para>
/// The one thing they all share is a cause. The document layer is a parallel object model, not a
/// view: <c>Load</c> copies the tokens it knows about out of the s-expression tree, and <c>Save</c>
/// rebuilds the tree from that model. Anything the model does not have a field for is not carried
/// across the round trip, so the file loses it.
/// </para>
/// </remarks>
public class DocumentLayerCharacterisationTests
{
    // --------------------------------------------------------------------- the fixtures themselves

    [Fact]
    public void SymbolLibraryFixture_IsTheMeasuredFile()
    {
        Assert.Equal(108_583, new FileInfo(TestData.SymbolLibrary).Length);

        // Counted from the s-expression tree, which has no model in between to lose anything.
        var root = SExpression.Load(TestData.SymbolLibrary);
        Assert.Equal("kicad_symbol_lib", root.Token);
        Assert.Equal(35, root.GetChildren("symbol").Count());
        Assert.Equal(67, root.GetChildren("symbol").SelectMany(s => s.GetChildren("symbol")).Count());
        Assert.Equal(112, root.Descendants("pin").Count());
    }

    // ------------------------------------------------------------------ sub-units are not modelled

    [Fact]
    public void SymbolLibrary_ReadsNoPins_BecauseSubUnitsAreNotModelled()
    {
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);

        Assert.Equal(35, library.Symbols.Count);

        // KiCad 6+ puts pins and graphics inside a nested (symbol "R_0_1" ...) unit. KiCadSymbol
        // only looks at its own direct children, so it finds none of the 112.
        Assert.Equal(0, library.Symbols.Sum(s => s.Pins.Count));
    }

    [Fact]
    public void SymbolLibrary_Save_DestroysTwoThirdsOfTheFile()
    {
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var output = Path.Combine(TestData.NewScratchDirectory(), "orbion.kicad_sym");

        library.Save(output);

        // 108,583 bytes in, 38,055 out. The 35 symbols keep their names and properties; every
        // sub-unit, pin and graphic is gone.
        Assert.Equal(38_055, new FileInfo(output).Length);

        var saved = SExpression.Load(output);
        Assert.Equal(35, saved.GetChildren("symbol").Count());
        Assert.Empty(saved.Descendants("pin"));
        Assert.Empty(saved.Descendants("rectangle"));
    }

    // ------------------------------------------------- a standalone .kicad_mod is not recognised

    [Fact]
    public void FootprintLibrary_Load_FindsNothingInAStandaloneKicadMod()
    {
        // The fallback in KiCadFootprintLibrary only fires when the root token is `module`, which is
        // the KiCad 5 spelling. KiCad 6+ writes `footprint`.
        Assert.Equal("footprint", SExpression.Load(TestData.Footprint).Token);

        var library = KiCadFootprintLibrary.Load(TestData.Footprint);
        Assert.Empty(library.Footprints);
    }

    // -------------------------------------------------- a footprint keeps only 8 kinds of child

    [Fact]
    public void Footprint_Save_DropsEveryTokenTheModelDoesNotKnow()
    {
        var source = SExpression.Load(TestData.Footprint);
        Assert.NotEmpty(source.GetChildren("fp_rect"));
        Assert.NotEmpty(source.GetChildren("property"));
        Assert.NotNull(source.GetChild("descr"));
        Assert.NotNull(source.GetChild("tags"));

        // Constructing the model from the node and asking for the s-expression back is exactly what
        // Save does for every footprint in a library.
        var rebuilt = new KiCadFootprint(source).ToSExpression();

        Assert.Empty(rebuilt.GetChildren("fp_rect"));
        Assert.Empty(rebuilt.GetChildren("property"));
        Assert.Null(rebuilt.GetChild("descr"));
        Assert.Null(rebuilt.GetChild("tags"));
    }
}
