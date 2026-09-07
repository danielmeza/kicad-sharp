using KiCadSharp.Documents;
using KiCadSharp.Schematics;

namespace KiCadSharp.Tests;

/// <summary>
/// Reading a schematic must not write to it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SchematicViewTests.Schematic_Save_AfterOnlyReading_IsByteIdentical"/> asserts the same
/// property, but it cannot fail: all three vendored fixtures carry a <c>(title_block ...)</c>, a
/// <c>(lib_symbols ...)</c> and a <c>(sheet_instances ...)</c>, and every wire in them carries a
/// stroke, so no getter there is ever asked for a form that is not present. The interesting case is
/// the sheet that omits one.
/// </para>
/// <para>
/// It is not a rare case. Of the 134 <c>.kicad_sch</c> files KiCad 10.0.6 itself ships — its demo
/// projects and its project templates — <strong>30 carry no <c>(title_block ...)</c></strong>
/// (KiCad writes the form only when at least one field is filled) and <strong>79 carry no
/// <c>(sheet_instances ...)</c></strong> (a child sheet in a hierarchy has none). Before the fix
/// this file guards, a read-only pass over that corpus dirtied 100 of the 134 documents and changed
/// the bytes of all 100 on save, one of them by 1,714 bytes and 603 lines.
/// </para>
/// </remarks>
public class SchematicGettersDoNotModifyTests
{
    [Fact]
    public void ReadingEverything_OnASheetThatOmitsTheOptionalForms_AddsNothing()
    {
        var schematic = KiCadSchematic.Parse(ChildSheet);

        Assert.False(schematic.IsModified);
        ReadEverything(schematic);

        Assert.False(schematic.IsModified);
        Assert.Equal(ChildSheet, schematic.ToText());
        Assert.Equal(ChildSheet.Length, schematic.ToText().Length);
    }

    [Fact]
    public void ReadingEverything_OnFormsThatCarryNoStrokeFillOrEffects_AddsNothing()
    {
        var schematic = KiCadSchematic.Parse(BareForms);

        Assert.False(schematic.IsModified);
        ReadEverything(schematic);

        Assert.False(schematic.IsModified);
        Assert.Equal(BareForms, schematic.ToText());
    }

    [Fact]
    public void TheOptionalFormsReadAsAbsent_RatherThanAsAnEmptyOneThatWasJustCreated()
    {
        var schematic = KiCadSchematic.Parse(ChildSheet);

        Assert.Null(schematic.TitleBlock);
        Assert.Empty(schematic.SheetInstances);
        Assert.NotNull(schematic.Wires[0].Stroke);
        Assert.NotNull(schematic.Labels[0].FontEffects);

        // Absent is not the same as empty-and-now-in-the-file.
        Assert.False(schematic.IsModified);
        Assert.DoesNotContain("(title_block", schematic.ToText(), StringComparison.Ordinal);
        Assert.DoesNotContain("(sheet_instances", schematic.ToText(), StringComparison.Ordinal);

        var bare = KiCadSchematic.Parse(BareForms);

        Assert.Null(bare.Wires[0].Stroke);
        Assert.Null(bare.Buses[0].Stroke);
        Assert.Null(bare.BusEntries[0].Stroke);
        Assert.Null(bare.Labels[0].FontEffects);
        Assert.Null(bare.TextItems[0].FontEffects);
        Assert.Null(bare.Rectangles[0].Stroke);
        Assert.Null(bare.Rectangles[0].Fill);
        Assert.Null(bare.TextBoxes[0].FontEffects);
        Assert.Null(bare.Sheets[0].Pins[0].FontEffects);
        Assert.Empty(bare.LibrarySymbols);

        Assert.False(bare.IsModified);
        Assert.Equal(BareForms, bare.ToText());
    }

    [Fact]
    public void RequireTitleBlock_AddsTheFormOnce_AndOnlyWhenAsked()
    {
        var schematic = KiCadSchematic.Parse(ChildSheet);
        Assert.Null(schematic.TitleBlock);

        var titleBlock = schematic.RequireTitleBlock();
        titleBlock.Title = "Given a title on purpose";

        Assert.True(schematic.IsModified);
        Assert.NotNull(schematic.TitleBlock);
        Assert.Equal("Given a title on purpose", schematic.TitleBlock!.Title);

        // Asking twice does not add a second form.
        Assert.Same(schematic.RequireTitleBlock().Node, schematic.RequireTitleBlock().Node);
        Assert.Single(Occurrences(schematic.ToText(), "(title_block"));
    }

    [Fact]
    public void RequireSheetInstancesAndLibrarySymbols_AddTheirFormsOnlyWhenAsked()
    {
        var schematic = KiCadSchematic.Parse(ChildSheet);

        Assert.Empty(schematic.SheetInstances);
        Assert.False(schematic.IsModified);

        var instances = schematic.RequireSheetInstances();
        instances.Add().Page = "3";

        Assert.Single(schematic.SheetInstances);
        Assert.Equal("3", schematic.SheetInstances[0].Page);
        Assert.Single(Occurrences(schematic.ToText(), "(sheet_instances"));

        // lib_symbols is present here, so requiring it must not add a second one.
        Assert.Equal(schematic.LibrarySymbols.Count, schematic.RequireLibrarySymbols().Count);
        Assert.Single(Occurrences(schematic.ToText(), "(lib_symbols"));
    }

    [Fact]
    public void AddingToAListWhoseFormIsMissing_FailsLoudly_RatherThanWritingSilently()
    {
        var schematic = KiCadSchematic.Parse(ChildSheet);

        var error = Assert.Throws<InvalidOperationException>(() => schematic.SheetInstances.Add());
        Assert.Contains("Require", error.Message, StringComparison.Ordinal);

        // The failure did not leave a half-written form behind.
        Assert.False(schematic.IsModified);
        Assert.Equal(ChildSheet, schematic.ToText());
    }

    private static void ReadEverything(KiCadSchematic schematic)
    {
        _ = schematic.Version;
        _ = schematic.Generator;
        _ = schematic.GeneratorVersion;
        _ = schematic.Paper;
        _ = schematic.Uuid;
        _ = schematic.EmbeddedFonts;

        var titleBlock = schematic.TitleBlock;
        _ = titleBlock?.Title;
        _ = titleBlock?.Date;
        _ = titleBlock?.Revision;
        _ = titleBlock?.Company;
        _ = titleBlock?.CommentNumbers;
        _ = titleBlock?.GetComment(1);

        foreach (var line in schematic.Wires.Cast<KiCadSchematicLine>().Concat(schematic.Buses))
        {
            _ = line.Points;
            _ = line.Start;
            _ = line.End;
            _ = line.Stroke?.Width;
            _ = line.Uuid;
        }

        foreach (var entry in schematic.BusEntries)
        {
            _ = entry.Position;
            _ = entry.Size;
            _ = entry.Stroke?.Type;
        }

        foreach (var label in schematic.Labels.Cast<KiCadSchematicLabel>()
                     .Concat(schematic.GlobalLabels)
                     .Concat(schematic.HierarchicalLabels)
                     .Concat(schematic.NetClassFlags))
        {
            _ = label.Text;
            _ = label.Position;
            _ = label.FontEffects?.Size;
            _ = label.Uuid;
        }

        foreach (var text in schematic.TextItems)
        {
            _ = text.Text;
            _ = text.Position;
            _ = text.FontEffects?.Bold;
            _ = text.ExcludeFromSim;
        }

        foreach (var box in schematic.TextBoxes)
        {
            _ = box.Text;
            _ = box.Size;
            _ = box.Stroke?.Width;
            _ = box.Fill?.Type;
            _ = box.FontEffects?.Size;
        }

        foreach (var item in schematic.Polylines.Cast<KiCadGraphicalItem>()
                     .Concat(schematic.Rectangles)
                     .Concat(schematic.Circles)
                     .Concat(schematic.Arcs)
                     .Concat(schematic.Beziers))
        {
            _ = item.Stroke?.Width;
            _ = item.Fill?.Type;
        }

        foreach (var junction in schematic.Junctions)
        {
            _ = junction.Position;
            _ = junction.Diameter;
            _ = junction.Color;
        }

        foreach (var noConnect in schematic.NoConnects)
        {
            _ = noConnect.Position;
        }

        foreach (var alias in schematic.BusAliases)
        {
            _ = alias.Name;
            _ = alias.Members;
        }

        foreach (var image in schematic.Images)
        {
            _ = image.Position;
            _ = image.Scale;
            _ = image.Data;
        }

        foreach (var symbol in schematic.Symbols)
        {
            _ = symbol.LibId;
            _ = symbol.Unit;
            _ = symbol.ReferenceProperty;
            foreach (var property in symbol.Properties)
            {
                _ = property.Key;
                _ = property.Value;
                _ = property.Position;
                _ = property.FontEffects?.Size;
            }
        }

        foreach (var librarySymbol in schematic.LibrarySymbols)
        {
            _ = librarySymbol.Id;
            foreach (var item in librarySymbol.GraphicalItems)
            {
                _ = item.Stroke?.Width;
                _ = item.Fill?.Type;
            }

            foreach (var pin in librarySymbol.Pins)
            {
                _ = pin.Number;
                _ = pin.Position;
            }
        }

        foreach (var sheet in schematic.Sheets)
        {
            _ = sheet.SheetName;
            _ = sheet.SheetFile;
            foreach (var pin in sheet.Pins)
            {
                _ = pin.Name;
                _ = pin.Shape;
                _ = pin.FontEffects?.Size;
            }
        }

        foreach (var instance in schematic.SheetInstances)
        {
            _ = instance.Path;
            _ = instance.Page;
        }
    }

    private static IEnumerable<int> Occurrences(string text, string needle)
    {
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, StringComparison.Ordinal))
        {
            yield return i;
        }
    }

    /// <summary>
    /// A child sheet in KiCad 10's own spelling: no <c>(title_block ...)</c>, no
    /// <c>(sheet_instances ...)</c> and no <c>(embedded_fonts ...)</c>, which is what eeschema
    /// writes for a sub-sheet whose drawing frame is empty. Copied in shape from
    /// <c>demos/multichannel/channel_strip.kicad_sch</c> as KiCad 10.0.6 ships it.
    /// </summary>
    private static readonly string ChildSheet = string.Join('\n',
        "(kicad_sch",
        "\t(version 20250114)",
        "\t(generator \"eeschema\")",
        "\t(generator_version \"10.0\")",
        "\t(uuid \"5a73122b-848b-428e-af45-cd70deba9234\")",
        "\t(paper \"A4\")",
        "\t(lib_symbols)",
        "\t(junction",
        "\t\t(at 120.65 81.28)",
        "\t\t(diameter 0)",
        "\t\t(color 0 0 0 0)",
        "\t\t(uuid \"009061c1-c9e3-4b72-bfad-e90302ff3801\")",
        "\t)",
        "\t(wire",
        "\t\t(pts",
        "\t\t\t(xy 120.65 68.58) (xy 120.65 81.28)",
        "\t\t)",
        "\t\t(stroke",
        "\t\t\t(width 0)",
        "\t\t\t(type default)",
        "\t\t)",
        "\t\t(uuid \"009061c1-c9e3-4b72-bfad-e90302ff38a2\")",
        "\t)",
        "\t(label \"IN_L\"",
        "\t\t(at 120.65 68.58 90)",
        "\t\t(fields_autoplaced yes)",
        "\t\t(effects",
        "\t\t\t(font",
        "\t\t\t\t(size 1.27 1.27)",
        "\t\t\t)",
        "\t\t\t(justify left bottom)",
        "\t\t)",
        "\t\t(uuid \"009061c1-c9e3-4b72-bfad-e90302ff3803\")",
        "\t)",
        "\t(hierarchical_label \"OUT_L\"",
        "\t\t(shape output)",
        "\t\t(at 146.05 81.28 0)",
        "\t\t(effects",
        "\t\t\t(font",
        "\t\t\t\t(size 1.27 1.27)",
        "\t\t\t)",
        "\t\t\t(justify left)",
        "\t\t)",
        "\t\t(uuid \"009061c1-c9e3-4b72-bfad-e90302ff3804\")",
        "\t)",
        ")",
        string.Empty);

    /// <summary>
    /// The same forms with every optional sub-form left out. The s-expression format permits it and
    /// this library's own <c>Wires.Add()</c> produces exactly this, so a consumer will meet it —
    /// and a getter that filled the gaps in would rewrite whatever it was handed.
    /// </summary>
    private static readonly string BareForms = string.Join('\n',
        "(kicad_sch",
        "\t(version 20250114)",
        "\t(generator \"eeschema\")",
        "\t(uuid \"5a73122b-848b-428e-af45-cd70deba9235\")",
        "\t(paper \"A4\")",
        "\t(wire",
        "\t\t(pts",
        "\t\t\t(xy 0 0) (xy 2.54 0)",
        "\t\t)",
        "\t\t(uuid \"009061c1-c9e3-4b72-bfad-e90302ff3810\")",
        "\t)",
        "\t(bus",
        "\t\t(pts",
        "\t\t\t(xy 0 5.08) (xy 2.54 5.08)",
        "\t\t)",
        "\t\t(uuid \"009061c1-c9e3-4b72-bfad-e90302ff3811\")",
        "\t)",
        "\t(bus_entry",
        "\t\t(at 2.54 5.08)",
        "\t\t(size 2.54 2.54)",
        "\t\t(uuid \"009061c1-c9e3-4b72-bfad-e90302ff3812\")",
        "\t)",
        "\t(label \"NAKED\"",
        "\t\t(at 0 10.16 0)",
        "\t\t(uuid \"009061c1-c9e3-4b72-bfad-e90302ff3813\")",
        "\t)",
        "\t(text \"no effects here\"",
        "\t\t(at 0 15.24 0)",
        "\t\t(uuid \"009061c1-c9e3-4b72-bfad-e90302ff3814\")",
        "\t)",
        "\t(rectangle",
        "\t\t(start 0 0)",
        "\t\t(end 10.16 10.16)",
        "\t\t(uuid \"009061c1-c9e3-4b72-bfad-e90302ff3815\")",
        "\t)",
        "\t(text_box \"bare box\"",
        "\t\t(at 20.32 20.32 0)",
        "\t\t(size 10.16 5.08)",
        "\t\t(uuid \"009061c1-c9e3-4b72-bfad-e90302ff3816\")",
        "\t)",
        "\t(sheet",
        "\t\t(at 30.48 30.48)",
        "\t\t(size 20.32 10.16)",
        "\t\t(uuid \"009061c1-c9e3-4b72-bfad-e90302ff3817\")",
        "\t\t(property \"Sheetname\" \"Bare\")",
        "\t\t(property \"Sheetfile\" \"bare.kicad_sch\")",
        "\t\t(pin \"OUT_L\" output",
        "\t\t\t(at 50.8 35.56 0)",
        "\t\t\t(uuid \"009061c1-c9e3-4b72-bfad-e90302ff3818\")",
        "\t\t)",
        "\t)",
        ")",
        string.Empty);
}
