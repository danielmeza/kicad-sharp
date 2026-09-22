using System.Diagnostics;

using KiCadSharp.Documents;
using KiCadSharp.Schematics;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// A renamed symbol, and a symbol built from nothing, held to what KiCad 10 checks when it loads the
/// library they are saved in.
/// </summary>
/// <remarks>
/// <para>
/// KiCad names every sub-unit after its symbol and rejects one that is not: <c>parseLibSymbol</c>
/// throws "Invalid symbol unit name prefix". A derived symbol's <c>(extends …)</c> must name a symbol
/// in the same library, or <c>updateParentSymbolLinks</c> throws "No parent for extended symbol".
/// Since 10.0, <c>SCH_IO_KICAD_SEXPR_LIB_CACHE::Load</c> turns even one skipped symbol into a failed
/// library, which <c>kicad-cli</c> reports as "Unable to load library" and nothing more.
/// </para>
/// <para>
/// Setting <see cref="KiCadSymbol.Id"/> used to rename the symbol and nothing else, and that is the
/// whole of what an importer does to a vendor's library before saving it (#45). Eight of these
/// tests fail on the code before this fix. Of the rest, two guard what the fix must not change (a
/// rename to the same name, and a derived symbol's own parent) and one pins the forms 0.2.0 stopped
/// writing. <see cref="KiCadRuleViolations"/> restates KiCad's two checks so the suite can fail
/// without KiCad; the last two tests hand the output to KiCad itself when
/// <c>KICADSHARP_KICAD_CLI</c> is set.
/// </para>
/// </remarks>
public class SymbolRenameTests
{
    // ------------------------------------------------------------------------------- sub-units

    [Fact]
    public void RenamingASymbol_RenamesItsSubUnits()
    {
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);
        var resistor = library.GetSymbol("R")!;

        resistor.Id = "UL_R";

        Assert.Equal(new[] { "UL_R_0_1", "UL_R_1_1" }, resistor.Units.Select(u => u.Id));
        Assert.Empty(KiCadRuleViolations(library.Node));
    }

    [Fact]
    public void RenamingASymbol_ChangesItsNameAndItsSubUnitNames_AndNothingElse()
    {
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);

        library.GetSymbol("Conn_01x02")!.Id = "UL_Conn_01x02";

        var expected = File.ReadAllText(TestData.SymbolLibrary)
            .Replace("(symbol \"Conn_01x02\"", "(symbol \"UL_Conn_01x02\"", StringComparison.Ordinal)
            .Replace("(symbol \"Conn_01x02_1_1\"", "(symbol \"UL_Conn_01x02_1_1\"", StringComparison.Ordinal);
        Assert.Equal(expected, library.ToText());
    }

    [Fact]
    public void RenamingASymbol_ToTheNameItHas_ChangesNoBytes()
    {
        var library = KiCadSymbolLibrary.Load(TestData.SymbolLibrary);

        library.GetSymbol("R")!.Id = "R";

        Assert.Equal(File.ReadAllText(TestData.SymbolLibrary), library.ToText());
    }

    [Fact]
    public void RenamingASymbol_LeavesAnotherSymbolsSubUnitsAlone_EvenWhenTheyShareItsPrefix()
    {
        var library = KiCadSymbolLibrary.Load(TestData.DerivedSymbolLibrary);

        library.GetSymbol("BASE")!.Id = "UL_BASE";

        Assert.Equal(new[] { "UL_BASE_0_1", "UL_BASE_1_1" }, library.GetSymbol("UL_BASE")!.Units.Select(u => u.Id));
        Assert.Equal("BASE_X_1_1", library.GetSymbol("BASE_X")!.Units.Single().Id);
    }

    // --------------------------------------------------------------------------------- extends

    [Fact]
    public void RenamingAParent_RepointsTheSymbolsThatExtendIt()
    {
        var library = KiCadSymbolLibrary.Load(TestData.DerivedSymbolLibrary);

        library.GetSymbol("BASE")!.Id = "UL_BASE";

        Assert.Equal("UL_BASE", Parent(library.GetSymbol("DERIVED")!));
        Assert.Empty(KiCadRuleViolations(library.Node));
    }

    [Fact]
    public void RenamingADerivedSymbol_KeepsItsParent()
    {
        var library = KiCadSymbolLibrary.Load(TestData.DerivedSymbolLibrary);

        library.GetSymbol("DERIVED")!.Id = "UL_DERIVED";

        Assert.Equal("BASE", Parent(library.GetSymbol("UL_DERIVED")!));
        Assert.Empty(KiCadRuleViolations(library.Node));
    }

    // ---------------------------------------------------------- a schematic's cached symbols

    [Fact]
    public void RenamingACachedSchematicSymbol_NamesItsSubUnitsWithoutTheNickname()
    {
        // KiCad keys sub-units on LIB_ID's item name: "orbion:+3V3" owns "+3V3_0_1".
        var schematic = KiCadSchematic.Load(TestData.Rs485Bridge);
        var cached = schematic.LibrarySymbols.Single(s => s.Id == "orbion:+3V3");

        cached.Id = "orbion:P3V3";

        Assert.Equal(new[] { "P3V3_0_1", "P3V3_1_1" }, cached.Units.Select(u => u.Id));
    }

    [Fact]
    public void CloneAs_OfACachedSchematicSymbol_RenamesItsSubUnits()
    {
        // CloneAs matched sub-units against "orbion:+3V3_", which none of them starts with.
        var schematic = KiCadSchematic.Load(TestData.Rs485Bridge);

        var copy = schematic.LibrarySymbols.Single(s => s.Id == "orbion:+3V3").CloneAs("orbion:P3V3");

        Assert.Equal(new[] { "P3V3_0_1", "P3V3_1_1" }, copy.Units.Select(u => u.Id));
        Assert.Equal(new[] { "+3V3_0_1", "+3V3_1_1" }, schematic.LibrarySymbols.Single(s => s.Id == "orbion:+3V3").Units.Select(u => u.Id));
    }

    // ------------------------------------------------------------------ what an importer does

    [Theory]
    [InlineData("orbion.kicad_sym", 35, 67, 112)]
    [InlineData("derived-symbols.kicad_sym", 3, 3, 3)]
    public void LoadRenameAddSave_WritesALibraryKiCadAccepts(string fixture, int symbols, int units, int pins)
    {
        var output = Path.Combine(TestData.NewScratchDirectory(), fixture);

        ImportWithPrefix(Path.Combine(TestData.Root, fixture), output, "UL_");

        var reloaded = KiCadSymbolLibrary.Load(output);
        Assert.Equal(symbols, reloaded.Symbols.Count);
        Assert.Equal(units, reloaded.Symbols.Sum(s => s.Units.Count));
        Assert.Equal(pins, reloaded.Symbols.Sum(s => s.Pins.Count));
        Assert.All(reloaded.Symbols, s => Assert.StartsWith("UL_", s.Id, StringComparison.Ordinal));
        Assert.Empty(KiCadRuleViolations(reloaded.Node));
    }

    // ----------------------------------------------------------------------- built from nothing

    [Fact]
    public void ASymbolBuiltFromNothing_WritesNoneOfTheFormsKiCadRefusedFrom011()
    {
        var library = BuildFromNothing();
        var text = library.ToText();
        var fill = library.Symbols[0].GraphicalItems.OfType<KiCadRectangle>().Single().Node.GetChild(KiCadTokens.Common.Fill)!;

        // KiCadSharp 0.1.1 wrote each of these, and kicad-cli 10.0.6 refuses a library holding any
        // one of them: a property at "(at x y)" with no angle (parseProperty reads one), a
        // "(position …)" inside "(effects …)" (parseEDA_TEXT knows font, justify, hide, href),
        // "(fill none)" (parseFill wants "(type …)"), and a bare pin number. KiCad's lexer reads a
        // bare 1 as a number where it wants a string, and so it reads a bare value of 100.
        Assert.Contains("(at 0 5.08 0)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("(position", text, StringComparison.Ordinal);
        Assert.Empty(fill.Values);
        Assert.Equal("background", fill.GetChild(KiCadTokens.Common.Type)?.GetValue(0));
        Assert.Contains("(number \"1\")", text, StringComparison.Ordinal);
        Assert.Contains("(property \"Value\" \"100\")", text, StringComparison.Ordinal);
        Assert.Contains("(symbol \"TEST_COMPONENT\"", text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------- KiCad itself

    [Fact]
    public void KiCadLoadsWhatAnImportWrites()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        var scratch = TestData.NewScratchDirectory();
        foreach (var fixture in new[] { TestData.SymbolLibrary, TestData.DerivedSymbolLibrary })
        {
            var output = Path.Combine(scratch, "UL_" + Path.GetFileName(fixture));
            ImportWithPrefix(fixture, output, "UL_");
            AssertKiCadLoads(cli, output);
        }
    }

    [Fact]
    public void KiCadLoadsASymbolBuiltFromNothing()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        var output = Path.Combine(TestData.NewScratchDirectory(), "built.kicad_sym");
        BuildFromNothing().Save(output);
        AssertKiCadLoads(cli, output);
    }

    // ----------------------------------------------------------------------------------- helpers

    /// <summary>
    /// What an importer does with a vendor's library: every symbol renamed with a prefix and moved
    /// into a library of its own. danielmeza/kicad-ultra's <c>KiCadImportEngine</c> is this loop.
    /// </summary>
    /// <remarks>
    /// The list is copied first. <see cref="KiCadSymbolLibrary.AddSymbol(KiCadSymbol)"/> moves the
    /// node out of the library it came from, so enumerating the live <c>Symbols</c> while adding
    /// skips every other symbol: 18 of <c>orbion.kicad_sym</c>'s 35 arrive.
    /// </remarks>
    private static void ImportWithPrefix(string source, string output, string prefix)
    {
        var target = new KiCadSymbolLibrary("kicad-sharp-tests");
        foreach (var symbol in KiCadSymbolLibrary.Load(source).Symbols.ToList())
        {
            symbol.Id = prefix + symbol.Id;
            target.AddSymbol(symbol);
        }

        target.Save(output);
    }

    /// <summary>
    /// The symbol kicad-ultra's <c>SampleConsole --test-parser</c> builds, with its reference placed,
    /// its value a number and its body filled, so each of those forms is written.
    /// </summary>
    private static KiCadSymbolLibrary BuildFromNothing()
    {
        var library = new KiCadSymbolLibrary("kicad-sharp-tests");
        var symbol = library.AddSymbol("TEST_COMPONENT");
        symbol.AddProperty("Value", "100");
        symbol.AddProperty("Manufacturer", "Test Manufacturer");
        symbol.Properties.Single(p => p.Key == "Reference").Position = new KiCadPosition(0, 5.08);
        symbol.AddPin(new KiCadPin("input", "line", new KiCadPosition(0, 0, 0), 2.54, "VCC", "1"));
        symbol.AddPin(new KiCadPin("output", "line", new KiCadPosition(0, -2.54, 0), 2.54, "OUT", "2"));
        symbol.AddPin(new KiCadPin("power_in", "line", new KiCadPosition(0, -5.08, 0), 2.54, "GND", "3"));
        var body = new KiCadRectangle(2.54, 2.54, 12.7, -7.62);
        body.RequireFill().Type = "background";
        symbol.AddGraphicalItem(body);
        return library;
    }

    private static string? Parent(KiCadSymbol symbol) => symbol.Node.GetChild(KiCadTokens.Symbol.Extends)?.GetValue(0);

    /// <summary>LIB_ID's item name: what follows the first colon, or the whole name.</summary>
    private static string ItemName(string id) => id.IndexOf(':', StringComparison.Ordinal) is var colon and >= 0 ? id[(colon + 1)..] : id;

    /// <summary>
    /// KiCad 10.0.6's two checks on these names, restated from its source. A sub-unit must start
    /// with its symbol's item name, and what follows the next character must be two numbers split by
    /// an underscore (<c>parseLibSymbol</c>). An <c>(extends …)</c> must name the item name of a
    /// symbol in the same library (<c>updateParentSymbolLinks</c>).
    /// </summary>
    /// <returns>One line per violation; empty when KiCad would accept every name.</returns>
    private static List<string> KiCadRuleViolations(SExpression library)
    {
        var violations = new List<string>();
        var symbols = library.GetChildren(KiCadTokens.Common.Symbol).ToList();
        var names = symbols.Select(s => ItemName(s.GetValue(0)!)).ToHashSet(StringComparer.Ordinal);

        foreach (var symbol in symbols)
        {
            var name = ItemName(symbol.GetValue(0)!);
            foreach (var unit in symbol.GetChildren(KiCadTokens.Common.Symbol))
            {
                var unitName = unit.GetValue(0)!;
                var parts = unitName.StartsWith(name, StringComparison.Ordinal) && unitName.Length > name.Length
                    ? unitName[(name.Length + 1)..].Split('_')
                    : null;
                if (parts is not { Length: 2 } || !parts.All(p => long.TryParse(p, out _)))
                {
                    violations.Add($"{symbol.GetValue(0)}: sub-unit \"{unitName}\"");
                }
            }

            if (symbol.GetChild(KiCadTokens.Symbol.Extends)?.GetValue(0) is { } parent && !names.Contains(parent))
            {
                violations.Add($"{symbol.GetValue(0)}: extends \"{parent}\", which is not in the library");
            }
        }

        return violations;
    }

    /// <summary>
    /// Plots every symbol in <paramref name="library"/> with <c>kicad-cli sym export svg</c>. That
    /// has to load the library first, and on a failed load kicad-cli prints "Unable to load library"
    /// and exits 2.
    /// </summary>
    private static void AssertKiCadLoads(string cli, string library)
    {
        var svg = Path.Combine(Path.GetDirectoryName(library)!, Path.GetFileNameWithoutExtension(library) + "-svg");
        var info = new ProcessStartInfo(cli) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "sym", "export", "svg", "-o", svg, library })
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"kicad-cli could not load {library} (exit {process.ExitCode}):\n{stdout}{stderr.Result}");
        foreach (var symbol in KiCadSymbolLibrary.Load(library).Symbols)
        {
            Assert.NotEmpty(Directory.GetFiles(svg, $"{symbol.Id}_unit*.svg"));
        }
    }
}
