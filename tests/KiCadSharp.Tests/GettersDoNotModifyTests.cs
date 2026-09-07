using KiCadSharp.Documents;
using KiCadSharp.Schematics;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// A getter must never change the document.
/// </summary>
/// <remarks>
/// <para>
/// The views expose sub-forms — a stroke, a fill, text effects, a title block — as views of their
/// own, and those forms are optional in every KiCad file format. Returning one meant deciding what
/// to do when the form is absent, and the first answer was to create it. That is a read that
/// dirties the file: ask a freshly-loaded footprint whether its text is italic and it grows an
/// empty <c>(effects)</c>, which then lands in the next save.
/// </para>
/// <para>
/// It breaks the one property the whole document layer exists to hold — after a save the file keeps
/// its original content and only the part that was <em>intended</em> to change has changed — and it
/// breaks it in the worst way, silently and on read. So the getters return <see langword="null"/>
/// and there is a <c>Require…()</c> beside each one for callers that mean to create.
/// </para>
/// </remarks>
public class GettersDoNotModifyTests
{
    /// <summary>A minimal file of each kind, with every optional sub-form left out.</summary>
    private const string BareFootprint = "(footprint \"BARE\"\n\t(layer \"F.Cu\")\n\t(fp_line\n\t\t(start 0 0)\n\t\t(end 1 1)\n\t)\n\t(fp_rect\n\t\t(start 0 0)\n\t\t(end 2 2)\n\t)\n\t(fp_text user \"x\"\n\t\t(at 0 0 0)\n\t)\n\t(pad \"1\" smd rect\n\t\t(at 0 0)\n\t\t(size 1 1)\n\t)\n)\n";

    private const string BareSymbolLibrary = "(kicad_symbol_lib\n\t(version 20241209)\n\t(symbol \"BARE\"\n\t\t(property \"Reference\" \"U\"\n\t\t\t(at 0 0 0)\n\t\t)\n\t\t(symbol \"BARE_1_1\"\n\t\t\t(rectangle\n\t\t\t\t(start 0 0)\n\t\t\t\t(end 1 1)\n\t\t\t)\n\t\t\t(pin passive line\n\t\t\t\t(at 0 0 0)\n\t\t\t\t(length 1)\n\t\t\t)\n\t\t)\n\t)\n)\n";

    private const string BareSchematic = "(kicad_sch\n\t(version 20250114)\n\t(uuid \"11111111-2222-3333-4444-555555555555\")\n\t(wire\n\t\t(pts\n\t\t\t(xy 0 0)\n\t\t\t(xy 1 0)\n\t\t)\n\t)\n\t(label \"N\"\n\t\t(at 0 0 0)\n\t)\n)\n";

    // ------------------------------------------------------------------------------- the property

    [Fact]
    public void ReadingEveryOptionalSubFormOfAFootprint_LeavesTheFileAlone()
    {
        var library = KiCadFootprintLibrary.Parse(BareFootprint);
        var footprint = library.Footprints[0];

        // Every one of these used to add a form to the file just by being read.
        Assert.Null(footprint.Lines[0].Stroke);
        Assert.Null(footprint.Rectangles[0].Stroke);
        Assert.Null(footprint.Rectangles[0].Fill);
        Assert.Null(footprint.TextItems[0].FontEffects);
        Assert.False(footprint.TextItems[0].Italic);
        Assert.False(footprint.TextItems[0].Hide);
        Assert.Equal(0.15, footprint.TextItems[0].Thickness);

        Assert.False(library.Document.IsModified);
        Assert.Equal(BareFootprint, library.ToText());
    }

    [Fact]
    public void ReadingEveryOptionalSubFormOfASymbol_LeavesTheFileAlone()
    {
        var library = KiCadSymbolLibrary.Parse(BareSymbolLibrary);
        var symbol = library.Symbols[0];

        Assert.Null(symbol.Properties[0].FontEffects);
        Assert.Null(symbol.Units[0].GraphicalItems[0].Stroke);
        Assert.Null(symbol.Units[0].GraphicalItems[0].Fill);

        Assert.False(library.Document.IsModified);
        Assert.Equal(BareSymbolLibrary, library.ToText());
    }

    [Fact]
    public void ReadingEveryOptionalSubFormOfASchematic_LeavesTheFileAlone()
    {
        var schematic = KiCadSchematic.Parse(BareSchematic);

        Assert.Null(schematic.TitleBlock);
        Assert.Empty(schematic.LibrarySymbols);
        Assert.Empty(schematic.SheetInstances);
        Assert.Null(schematic.Wires[0].Stroke);
        Assert.Null(schematic.Labels[0].FontEffects);

        Assert.False(schematic.Document.IsModified);
        Assert.Equal(BareSchematic, schematic.ToText());
    }

    [Fact]
    public void ReadingEveryOptionalSubFormOfABoard_LeavesTheFileAlone()
    {
        var board = KiCadBoard.Load(TestData.PowerInputBoard);
        var before = File.ReadAllBytes(TestData.PowerInputBoard);

        // power-input.kicad_pcb has a (general ...) but no (title_block ...) and no (setup ...) --
        // reading the two it lacks must not add them.
        Assert.NotNull(board.General);
        Assert.Null(board.TitleBlock);
        Assert.Null(board.Setup);
        foreach (var text in board.Texts)
        {
            _ = text.FontEffects;
        }

        Assert.False(board.Document.IsModified);

        var output = Path.Combine(TestData.NewScratchDirectory(), "power-input.kicad_pcb");
        board.Save(output);
        Assert.Equal(before, File.ReadAllBytes(output));
    }

    // ------------------------------------------------------------- and Require… does create, once

    [Fact]
    public void RequireAddsTheFormAndSayingSoTwiceAddsItOnce()
    {
        var library = KiCadFootprintLibrary.Parse(BareFootprint);
        var text = library.Footprints[0].TextItems[0];

        Assert.Null(text.FontEffects);

        var effects = text.RequireFontEffects();
        effects.Italic = true;

        Assert.NotNull(text.FontEffects);
        Assert.True(text.Italic);
        Assert.Same(effects.Node, text.RequireFontEffects().Node);
        Assert.Single(text.Node.GetChildren("effects"));
    }

    [Fact]
    public void RequireTitleBlock_GivesASheetItsFirstTitle()
    {
        var schematic = KiCadSchematic.Parse(BareSchematic);
        Assert.Null(schematic.TitleBlock);

        schematic.RequireTitleBlock().Title = "First";

        Assert.Equal("First", schematic.TitleBlock!.Title);
        Assert.Single(schematic.Node.GetChildren("title_block"));
    }

    [Fact]
    public void RequireSheetInstances_AddsThePageMapOnlyWhenAsked()
    {
        var schematic = KiCadSchematic.Parse(BareSchematic);
        Assert.Empty(schematic.SheetInstances);
        Assert.False(schematic.Document.IsModified);

        var instances = schematic.RequireSheetInstances();
        var path = instances.Add();
        path.Node.AddValue("/", SQuoteStyle.Quoted);
        path.Page = "1";

        Assert.Single(schematic.SheetInstances);
        Assert.Equal("1", schematic.SheetInstances[0].Page);
    }
}
