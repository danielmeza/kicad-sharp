using System.Diagnostics;
using System.Text.RegularExpressions;

using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// What a footprint file built from scratch holds, and what a footprint file's version reports.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>#74.</b> <c>new KiCadFootprintLibrary()</c> wrote an empty <c>(layers)</c>, and kicad-cli
/// 10.0.6 refused the file, "0 is not a valid layer count", exit 139. It now starts with the layer
/// table a new <see cref="KiCadBoard"/> has.</item>
/// <item><b>#75.</b> <c>new KiCadFootprint(id)</c> wrote <c>(fp_text reference …)</c>,
/// <c>(fp_text value …)</c> and a bare <c>(width w)</c>, forms KiCad 10 no longer writes. It now
/// writes the four fields KiCad gives every footprint as <c>(property …)</c>, and a new shape's
/// width as <c>(stroke (width w) (type solid))</c>. KiCad 10.0.6 reads the two spellings the same;
/// <see cref="KiCadReadsTheNewFormsAsItReadTheOldOnes"/> checks that.</item>
/// <item><b>#76.</b> <see cref="KiCadFootprintLibrary.Version"/> reported <c>20211014</c> for a file
/// with no version, which KiCad reads as format 0 (a <c>.kicad_mod</c>) or <c>20201115</c> (a
/// board). It now reports <see langword="null"/>, as <see cref="KiCadFootprint.Version"/> does.</item>
/// </list>
/// <para>
/// The kicad-cli tests are opt-in, as every test that needs KiCad is: set
/// <c>KICADSHARP_KICAD_CLI</c> to a <c>kicad-cli</c>. Without it they return early — CI has no KiCad.
/// </para>
/// </remarks>
public class NewFootprintFormsTests
{
    /// <summary>A KiCad 6 footprint: text items for its reference and value, and bare widths.</summary>
    private const string KiCad6Footprint = """
        (footprint "Old"
        	(version 20211014)
        	(generator pcbnew)
        	(layer "F.Cu")
        	(fp_text reference "REF**"
        		(at 0 0)
        		(layer "F.SilkS")
        	)
        	(fp_text value "Old"
        		(at 0 1.27)
        		(layer "F.Fab")
        	)
        	(fp_line
        		(start 0 0)
        		(end 1 0)
        		(layer "F.SilkS")
        		(width 0.12)
        	)
        )

        """;

    /// <summary>
    /// What <c>new KiCadFootprint("Same")</c> and one <c>AddLine</c> saved before #75, byte for byte.
    /// </summary>
    private const string OldSpelling = """
        (footprint "Same"
        	(version 20260206)
        	(generator "KiCad Library Importer")
        	(layer "F.Cu")
        	(fp_text reference "REF**"
        		(at 0 0 0)
        		(layer "F.SilkS")
        	)
        	(fp_text value "Same"
        		(at 0 1.27 0)
        		(layer "F.Fab")
        	)
        	(fp_line
        		(start 0 0)
        		(end 1 0)
        		(layer "F.SilkS")
        		(width 0.12)
        	)
        )

        """;

    // ------------------------------------------------------------------ #74: a new board-shaped library

    [Fact]
    public void ANewLibrary_StartsWithTheLayerTableOfANewBoard()
    {
        var library = new KiCadFootprintLibrary();
        var layers = library.Node.GetChild(KiCadTokens.Common.Layers);

        Assert.NotNull(layers);
        Assert.Equal(new KiCadBoard().Node.GetChild(KiCadTokens.Common.Layers)!.ToText(), layers.ToText());
        Assert.Equal(
            [KiCadLayerNames.FCu, KiCadLayerNames.BCu],
            KiCadBoard.Parse(library.ToText()).Layers.Where(l => l.Type == KiCadLayerNames.TypeSignal).Select(l => l.Name));
    }

    // -------------------------------------------------------------------- #75: a new footprint's forms

    /// <summary>
    /// The four fields KiCad 10.0.6 gives every footprint (<c>pcbnew/footprint.cpp</c>, lines
    /// 114–117), written as its writer writes a field.
    /// </summary>
    [Fact]
    public void ANewFootprint_HasTheFourFieldsKiCadWrites_AsProperties()
    {
        var footprint = new KiCadFootprint("R_0603");
        var text = Flatten(footprint.Node.ToText());

        Assert.Equal(
            [KiCadPropertyNames.Reference, KiCadPropertyNames.Value, KiCadPropertyNames.Datasheet, KiCadPropertyNames.Description],
            footprint.Properties.Select(p => p.Key));
        Assert.Contains("(property \"Reference\" \"REF**\" (at 0 0 0) (layer \"F.SilkS\"))", text, StringComparison.Ordinal);
        Assert.Contains("(property \"Value\" \"R_0603\" (at 0 1.27 0) (layer \"F.Fab\"))", text, StringComparison.Ordinal);
        Assert.Contains("(property \"Datasheet\" \"\" (at 0 0 0) (layer \"F.Fab\") (hide yes))", text, StringComparison.Ordinal);
        Assert.Contains("(property \"Description\" \"\" (at 0 0 0) (layer \"F.Fab\") (hide yes))", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fp_text", text, StringComparison.Ordinal);
        Assert.Empty(footprint.TextItems);
    }

    /// <summary>
    /// The point of #75: code that reads a footprint KiCad wrote reads a new one the same way.
    /// </summary>
    [Fact]
    public void GetPropertyValue_FindsTheReferenceAndValue_OnANewFootprint_AsOnOneKiCadWrote()
    {
        var written = Assert.Single(KiCadFootprintLibrary.Load(TestData.Footprint).Footprints);
        var built = new KiCadFootprint("LED_0603_1608Metric");

        foreach (var key in new[] { KiCadPropertyNames.Reference, KiCadPropertyNames.Value })
        {
            Assert.Equal(written.GetPropertyValue(key), built.GetPropertyValue(key));
        }

        Assert.DoesNotContain(written.TextItems, t => t.Type == KiCadTokens.Footprint.TextTypeReference);
        Assert.Empty(built.TextItems);
    }

    [Fact]
    public void ANewShape_WritesItsWidthAsAStroke_BeforeItsLayer()
    {
        var footprint = new KiCadFootprint("F");
        var line = footprint.AddLine(0, 0, 1, 0, KiCadLayerNames.FSilkS);
        var circle = footprint.AddCircle(0, 0, 0.5, 0, KiCadLayerNames.FFab, 0.1);
        var arc = footprint.Arcs.Add();
        arc.Width = 0.15;

        Assert.Equal("(fp_line (start 0 0) (end 1 0) (stroke (width 0.12) (type solid)) (layer \"F.SilkS\"))", Flatten(line.Node.ToText()));
        Assert.Equal("(fp_circle (center 0 0) (end 0.5 0) (stroke (width 0.1) (type solid)) (layer \"F.Fab\"))", Flatten(circle.Node.ToText()));
        Assert.Equal("(fp_arc (stroke (width 0.15) (type solid)))", Flatten(arc.Node.ToText()));
        Assert.Equal([0.12, 0.1, 0.15], new KiCadFpItem[] { line, circle, arc }.Select(i => i.Width));
    }

    /// <summary>
    /// A footprint read from a file keeps its forms: its text items stay text items, and a width
    /// written back to a bare <c>(width w)</c> stays bare, so only the number changes.
    /// </summary>
    [Fact]
    public void ALoadedFootprint_KeepsItsFormsWhenWrittenTo()
    {
        var library = KiCadFootprintLibrary.Parse(KiCad6Footprint);
        var footprint = Assert.Single(library.Footprints);

        Assert.Equal([KiCadTokens.Footprint.TextTypeReference, KiCadTokens.Footprint.TextTypeValue], footprint.TextItems.Select(t => t.Type));
        Assert.Empty(footprint.Properties);
        Assert.Equal(KiCad6Footprint, library.ToText());

        Assert.Single(footprint.Lines).Width = 0.2;

        Assert.Equal(KiCad6Footprint.Replace("(width 0.12)", "(width 0.2)", StringComparison.Ordinal), library.ToText());
    }

    [Fact]
    public void AShapeThatHasAStroke_TakesTheWidthInIt()
    {
        var line = new KiCadFpLine(SExpression.Parse("(fp_line (start 0 0) (end 1 0) (stroke (width 0.1) (type dash)) (layer \"F.SilkS\"))"));

        line.Width = 0.3;

        Assert.Equal("(fp_line (start 0 0) (end 1 0) (stroke (width 0.3) (type dash)) (layer \"F.SilkS\"))", Flatten(line.Node.ToText()));
    }

    // -------------------------------------------------------------- #76: the version a file declares

    public static TheoryData<string> FilesWithNoVersion => new()
    {
        "(footprint \"F\" (layer \"F.Cu\"))",
        "(module Legacy (layer F.Cu) (tedit 5B307E4C))",
        "(kicad_pcb (generator \"x\") (general) (paper \"A4\"))",
    };

    /// <summary>
    /// A file that declares no version reports none, rather than a stamp neither it nor KiCad uses,
    /// and for a <c>.kicad_mod</c> the library and its footprint agree.
    /// </summary>
    [Theory]
    [MemberData(nameof(FilesWithNoVersion))]
    public void AFileWithNoVersion_ReportsNone(string text)
    {
        var library = KiCadFootprintLibrary.Parse(text);

        Assert.Null(library.Version);
        if (library.IsSingleFootprint)
        {
            Assert.Null(Assert.Single(library.Footprints).Version);
        }

        Assert.Equal(text, library.ToText());
    }

    [Fact]
    public void AKiCadMod_ReportsTheVersionItsFootprintDeclares()
    {
        var library = KiCadFootprintLibrary.Load(TestData.Footprint);

        Assert.Equal("20260206", library.Version);
        Assert.Equal(library.Version, Assert.Single(library.Footprints).Version);
    }

    [Fact]
    public void Version_Null_RemovesIt_AndANewOneGoesFirst()
    {
        var library = KiCadFootprintLibrary.Parse("(footprint \"F\" (version 20211014) (layer \"F.Cu\") (fp_arc (start 0 0) (mid 1 1) (end 2 0)))");

        library.Version = null;
        Assert.Null(library.Version);
        Assert.Null(library.Node.GetChild(KiCadTokens.Common.Version));

        library.Version = "20260206";
        Assert.Equal(KiCadTokens.Common.Version, library.Node.Children[0].Token);
        Assert.Equal("20260206", Assert.Single(library.Footprints).Version);
    }

    // --------------------------------------------------------------------------------- KiCad reads it

    /// <summary>
    /// <c>new KiCadFootprintLibrary()</c>, empty and holding a new footprint, handed to kicad-cli
    /// 10.0.6. Before #74 both were refused, "0 is not a valid layer count".
    /// </summary>
    [Fact]
    public void KiCadLoadsANewLibrary()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        var scratch = TestData.NewScratchDirectory();
        var empty = Path.Combine(scratch, "empty.kicad_pcb");
        new KiCadFootprintLibrary().Save(empty);
        var holding = Path.Combine(scratch, "holding.kicad_pcb");
        var library = new KiCadFootprintLibrary();
        library.AddFootprint(new KiCadFootprint("InLibrary"));
        library.Save(holding);

        Run(cli, scratch, "pcb", "upgrade", "--force", empty);
        Run(cli, scratch, "pcb", "upgrade", "--force", holding);

        Assert.Equal("pcbnew", KiCadBoard.Load(empty).Generator); // KiCad loaded it and wrote it back itself
        var board = KiCadBoard.Load(holding);
        Assert.Equal("pcbnew", board.Generator);
        Assert.Equal(2, board.Layers.Count(l => l.Type == KiCadLayerNames.TypeSignal));
        var footprint = Assert.Single(board.Footprints);
        Assert.Equal("REF**", footprint.GetPropertyValue(KiCadPropertyNames.Reference));
        Assert.Equal("InLibrary", footprint.GetPropertyValue(KiCadPropertyNames.Value));
    }

    /// <summary>
    /// kicad-cli 10.0.6 reads a new footprint in the forms #75 writes exactly as it read the same
    /// footprint in the forms before: its own re-save of the two is the same file, apart from the
    /// UUIDs it assigns.
    /// </summary>
    [Fact]
    public void KiCadReadsTheNewFormsAsItReadTheOldOnes()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        var scratch = TestData.NewScratchDirectory();
        var footprint = new KiCadFootprint("Same");
        footprint.AddLine(0, 0, 1, 0, KiCadLayerNames.FSilkS);

        var newPretty = Directory.CreateDirectory(Path.Combine(scratch, "New.pretty")).FullName;
        KiCadFootprintLibrary.SaveFootprint(footprint, Path.Combine(newPretty, "Same.kicad_mod"));
        var oldPretty = Directory.CreateDirectory(Path.Combine(scratch, "Old.pretty")).FullName;
        File.WriteAllText(Path.Combine(oldPretty, "Same.kicad_mod"), OldSpelling);

        var newOut = Path.Combine(scratch, "NewOut.pretty");
        var oldOut = Path.Combine(scratch, "OldOut.pretty");
        Run(cli, scratch, "fp", "upgrade", "--force", "-o", newOut, newPretty);
        Run(cli, scratch, "fp", "upgrade", "--force", "-o", oldOut, oldPretty);

        var fromNew = File.ReadAllText(Path.Combine(newOut, "Same.kicad_mod"));
        var fromOld = File.ReadAllText(Path.Combine(oldOut, "Same.kicad_mod"));
        Assert.Equal(WithoutUuids(fromOld), WithoutUuids(fromNew));

        var read = Assert.Single(KiCadFootprintLibrary.Parse(fromNew).Footprints);
        Assert.Equal("REF**", read.GetPropertyValue(KiCadPropertyNames.Reference));
        Assert.Equal(0.12, Assert.Single(read.Lines).Width);
    }

    // ------------------------------------------------------------------------------------- helpers

    private static string WithoutUuids(string text) => Regex.Replace(text, "\\(uuid \"[^\"]*\"\\)", "(uuid)");

    private static string Flatten(string text) =>
        string.Join(' ', text.Split(['\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries))
            .Replace("( ", "(", StringComparison.Ordinal)
            .Replace(" )", ")", StringComparison.Ordinal);

    private static void Run(string cli, string workingDirectory, params string[] arguments)
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
        Assert.True(process.ExitCode == 0, $"kicad-cli {string.Join(' ', arguments)} exited {process.ExitCode}:\n{stdout.Result}\n{stderr}");
    }
}
