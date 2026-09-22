using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

using KiCadSharp.Documents;
using KiCadSharp.Schematics;

namespace KiCadSharp.Tests;

/// <summary>
/// KiCad stores the angle of a symbol's <c>(text ...)</c> in tenths of a degree, and the angle of a
/// sheet's text in degrees.
/// </summary>
/// <remarks>
/// KiCad 10.0.6 reads a symbol text with <c>TENTHS_OF_A_DEGREE_T</c> and writes it with
/// <c>AsTenthsOfADegree()</c>. Every stock library it ships writes a vertical symbol text as
/// <c>(at x y 900)</c>. Measured with kicad-cli 10.0.6: a symbol text written <c>(at 0 0 90)</c>,
/// which is what <c>new KiCadText(..., rotation: 90)</c> wrote, came back from <c>sym upgrade</c>
/// as <c>90</c>, so KiCad held 9°. In <c>sym export svg</c> it was drawn vertical but sized as 9°,
/// with 48 of its 60 stroke points outside the page.
/// </remarks>
public class SymbolTextAngleTests
{
    /// <summary>A vertical symbol text, as kicad-cli 10.0.6 wrote it back from <c>sym upgrade</c>.</summary>
    private const string KiCadVerticalText = """
        (text "HHHHHHHHHH"
        	(at 0 0 900)
        	(effects
        		(font
        			(size 1.27 1.27)
        		)
        	)
        )
        """;

    [Fact]
    public void RotationDegreesWritesTenthsOfADegree()
    {
        var text = new KiCadText("HHHHHHHHHH", 1.27, -2.54) { RotationDegrees = 90 };

        Assert.Equal("(at 1.27 -2.54 900)", text.Node.GetChild("at")!.ToString());
        Assert.Equal(90, text.RotationDegrees);
    }

    [Fact]
    public void RotationDegreesReadsWhatKiCadWrote()
    {
        var text = new KiCadText(SExpressions.SExpression.Parse(KiCadVerticalText));

        Assert.Equal(90, text.RotationDegrees);
        Assert.Equal(900, text.Position.Rotation);
    }

    [Fact]
    public void RotationDegreesKeepsThePosition()
    {
        var text = new KiCadText("A", 5.08, 2.54) { RotationDegrees = 270 };

        Assert.Equal(new KiCadPosition(5.08, 2.54, 2700), text.Position);
    }

    [Fact]
    public void TheThreeArgumentConstructorWritesAHorizontalText()
    {
        var text = new KiCadText("A", 1, 2);

        Assert.Equal("(at 1 2 0)", text.Node.GetChild("at")!.ToString());
        Assert.Equal(0, text.RotationDegrees);
    }

    [Fact]
    public void TheObsoleteConstructorStillWritesItsAngleUnchanged()
    {
        // Kept on purpose: changing what this writes would change every existing caller's output
        // without a word. The [Obsolete] warning is how they hear about it instead.
#pragma warning disable CS0618
        var text = new KiCadText("A", 1, 2, 90);
#pragma warning restore CS0618

        Assert.Equal("(at 1 2 90)", text.Node.GetChild("at")!.ToString());
        Assert.Equal(9, text.RotationDegrees);
    }

    [Fact]
    public void ReadingRotationDegreesDoesNotAddAnAt()
    {
        var text = new KiCadText(SExpressions.SExpression.Parse("(text \"A\")"));

        Assert.Equal(0, text.RotationDegrees);
        Assert.Null(text.Node.GetChild("at"));
    }

    [Fact]
    public void ASymbolInALibraryReadsItsTextInDegrees()
    {
        var library = KiCadSymbolLibrary.Parse(
            $"(kicad_symbol_lib (version 20251024) (symbol \"S\" (symbol \"S_1_1\" {KiCadVerticalText})))");

        var text = Assert.IsType<KiCadText>(Assert.Single(library.Symbols[0].GraphicalItems));

        Assert.Equal(90, text.RotationDegrees);
    }

    [Fact]
    public void ASheetTextIsInDegrees()
    {
        // KiCad 10.0.6 reads a sheet's (text ...) angle with DEGREES_T (parseSchText).
        var schematic = KiCadSchematic.Parse(
            "(kicad_sch (version 20260306) (generator \"eeschema\") (text \"N\" (at 100 100 90) (uuid \"u\")))");
        var text = schematic.TextItems[0];

        Assert.Equal(90, text.RotationDegrees);
        Assert.Equal(90, text.Position.Rotation);

        text.RotationDegrees = 270;

        Assert.Equal("(at 100 100 270)", text.Node.GetChild("at")!.ToString());
    }

    /// <summary>
    /// KiCad must draw a 90° text written through <see cref="KiCadText.RotationDegrees"/> as
    /// vertical, and size the page for it. A text KiCad reads as 9° is also drawn vertical, but the
    /// page is sized for 9°, so most of the text lies outside the page. Opt-in through
    /// <c>KICADSHARP_KICAD_CLI</c>, like every test here that needs KiCad.
    /// </summary>
    [Fact]
    public void KiCadDrawsA90DegreeTextInsideThePage()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        using var scratch = TestData.NewScratchDirectory();
        var library = new KiCadSymbolLibrary();
        var text = new KiCadText("HHHHHHHHHH", 0, 0) { RotationDegrees = 90 };
        text.RequireFontEffects().Size = new KiCadSize(1.27, 1.27);
        library.AddSymbol("VERTICAL").AddUnit("VERTICAL_1_1").Node.AddChild(text.Node);
        var path = Path.Combine(scratch, "vertical.kicad_sym");
        library.Save(path);

        var svgDirectory = Path.Combine(scratch, "svg");
        Directory.CreateDirectory(svgDirectory);
        Run(cli, scratch, "sym", "export", "svg", "-o", svgDirectory, path);
        var svg = File.ReadAllText(Assert.Single(Directory.GetFiles(svgDirectory, "*.svg")));

        var page = Regex.Match(svg, "viewBox=\"([^\"]+)\"").Groups[1].Value.Split(' ').Select(Number).ToArray();
        var strokes = Regex.Match(svg, "<g class=\"stroked-text\"><desc>HHHHHHHHHH</desc>(.*?)</g>", RegexOptions.Singleline).Groups[1].Value;
        var points = Regex.Matches(strokes, "[ML]([-0-9.]+) ([-0-9.]+)")
            .Select(m => (X: Number(m.Groups[1].Value), Y: Number(m.Groups[2].Value)))
            .ToArray();

        Assert.NotEmpty(points);
        Assert.True(points.Max(p => p.Y) - points.Min(p => p.Y) > points.Max(p => p.X) - points.Min(p => p.X), "the text is not vertical");
        Assert.All(points, p => Assert.True(
            p.X >= page[0] && p.X <= page[0] + page[2] && p.Y >= page[1] && p.Y <= page[1] + page[3],
            $"({p.X}, {p.Y}) is outside the page {string.Join(' ', page)}"));
    }

    private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    private static void Run(string cli, string workingDirectory, params string[] args)
    {
        var info = new ProcessStartInfo(cli)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"kicad-cli {string.Join(' ', args)} failed (exit {process.ExitCode}): {errors}");
    }
}
