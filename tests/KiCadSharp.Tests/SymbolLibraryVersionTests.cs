using System.Diagnostics;

using KiCadSharp.Documents;
using KiCadSharp.Schematics;

namespace KiCadSharp.Tests;

/// <summary>
/// A symbol library's <c>version</c> decides how KiCad reads the symbols in it, so a library that
/// takes its symbols from another library takes that library's version with them.
/// </summary>
/// <remarks>
/// <para>
/// Measured with kicad-cli 10.0.6: <see cref="KiCad10Library"/>'s symbol, copied into a
/// <c>new KiCadSymbolLibrary()</c> that kept its <c>20211014</c> stamp, came back from
/// <c>sym upgrade</c> with an empty value and an empty pin name where the source has <c>~</c>, and
/// with its 270° arc replaced by a 90° one. Stamping every new library with KiCad 10's own
/// <c>20251024</c> is not the answer either: the same measurement over KiCad 9.0.9's stock
/// <c>Device</c> and <c>74xx</c> libraries named every unnamed pin of <c>R</c> <c>~</c> and dropped
/// the De Morgan body style of <c>74LS00</c>. A symbol is read as written only under the stamp it
/// was written under.
/// </para>
/// </remarks>
public class SymbolLibraryVersionTests
{
    /// <summary>
    /// A library kicad-cli 10.0.6 wrote (<c>sym upgrade --force</c> over a hand-written input), kept
    /// verbatim: a value and a pin name that are a literal <c>~</c>, and a 270° arc. Each is read
    /// differently under an older stamp.
    /// </summary>
    private const string KiCad10Library = """
        (kicad_symbol_lib
        	(version 20251024)
        	(generator "kicad_symbol_editor")
        	(generator_version "10.0")
        	(symbol "TILDE"
        		(exclude_from_sim no)
        		(in_bom yes)
        		(on_board yes)
        		(in_pos_files yes)
        		(duplicate_pin_numbers_are_jumpers no)
        		(property "Reference" "U"
        			(at 0 7.62 0)
        			(show_name no)
        			(do_not_autoplace no)
        			(effects
        				(font
        					(size 1.27 1.27)
        				)
        			)
        		)
        		(property "Value" "~"
        			(at 0 -7.62 0)
        			(show_name no)
        			(do_not_autoplace no)
        			(effects
        				(font
        					(size 1.27 1.27)
        				)
        			)
        		)
        		(property "Footprint" ""
        			(at 0 0 0)
        			(show_name no)
        			(do_not_autoplace no)
        			(hide yes)
        			(effects
        				(font
        					(size 1.27 1.27)
        				)
        			)
        		)
        		(property "Datasheet" ""
        			(at 0 0 0)
        			(show_name no)
        			(do_not_autoplace no)
        			(hide yes)
        			(effects
        				(font
        					(size 1.27 1.27)
        				)
        			)
        		)
        		(property "Description" ""
        			(at 0 0 0)
        			(show_name no)
        			(do_not_autoplace no)
        			(hide yes)
        			(effects
        				(font
        					(size 1.27 1.27)
        				)
        			)
        		)
        		(symbol "TILDE_0_1"
        			(arc
        				(start 0 -5)
        				(mid -3.5355 3.5355)
        				(end 5 0)
        				(stroke
        					(width 0.254)
        					(type default)
        				)
        				(fill
        					(type none)
        				)
        			)
        		)
        		(symbol "TILDE_1_1"
        			(pin passive line
        				(at -10.16 0 0)
        				(length 2.54)
        				(name "~"
        					(effects
        						(font
        							(size 1.27 1.27)
        						)
        					)
        				)
        				(number "1"
        					(effects
        						(font
        							(size 1.27 1.27)
        						)
        					)
        				)
        			)
        		)
        		(embedded_fonts no)
        	)
        )
        """;

    /// <summary>The same library under another stamp, for the tests that only need a version.</summary>
    private static string Stamped(string version) =>
        KiCad10Library.Replace("(version 20251024)", $"(version {version})", StringComparison.Ordinal);

    [Fact]
    public void ANewLibraryTakesTheVersionOfTheLibraryItsFirstSymbolComesFrom()
    {
        var source = KiCadSymbolLibrary.Parse(KiCad10Library);
        var library = new KiCadSymbolLibrary();

        library.AddSymbol(source.Symbols[0]);

        Assert.Equal("20251024", library.Version);
        Assert.Contains("(version 20251024)", library.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOlderSourceVersionIsTakenToo()
    {
        // Not "the highest version": an empty library's stamp describes nothing yet, and a KiCad 9
        // symbol is only read as KiCad 9 wrote it under KiCad 9's stamp.
        var source = KiCadSymbolLibrary.Parse(Stamped("20241209"));
        var library = new KiCadSymbolLibrary(version: "20251024");

        library.AddSymbol(source.Symbols[0]);

        Assert.Equal("20241209", library.Version);
    }

    [Fact]
    public void EverySymbolOfTheNaturalCopyLoopArrivesUnderTheSourceVersion()
    {
        var source = KiCadSymbolLibrary.Parse(KiCad10Library);
        source.Node.AddChild(source.Symbols[0].CloneAs("SECOND").Node);
        var library = new KiCadSymbolLibrary();

        foreach (var symbol in source.Symbols)
        {
            library.AddSymbol(symbol);
        }

        Assert.Equal(2, library.Symbols.Count);
        Assert.Equal("20251024", library.Version);
    }

    [Fact]
    public void ALibraryThatAlreadyHoldsSymbolsKeepsItsVersion()
    {
        // Raising it would change how KiCad reads the symbols already there.
        var library = new KiCadSymbolLibrary();
        library.AddSymbol("BUILT_HERE");

        library.AddSymbol(KiCadSymbolLibrary.Parse(KiCad10Library).Symbols[0]);

        Assert.Equal(KiCadDefaults.SymbolLibraryVersion, library.Version);
        Assert.Equal(2, library.Symbols.Count);
    }

    [Fact]
    public void ASymbolBuiltInMemoryLeavesTheVersionAlone()
    {
        var library = new KiCadSymbolLibrary();

        library.AddSymbol("BUILT_HERE");

        Assert.Equal(KiCadDefaults.SymbolLibraryVersion, library.Version);
    }

    [Fact]
    public void AClonedSymbolIntoAnEmptyLibraryLeavesTheVersionAlone()
    {
        // A clone has no parent, so nothing says which version it was written under.
        var library = new KiCadSymbolLibrary();

        library.AddSymbol(KiCadSymbolLibrary.Parse(KiCad10Library).Symbols[0].CloneAs("CLONE"));

        Assert.Equal(KiCadDefaults.SymbolLibraryVersion, library.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASymbolFromASchematicLeavesTheVersionAlone(bool placed)
    {
        // A schematic's version is a schematic format version. kicad-cli 10.0.6 refuses a symbol
        // library stamped with its own schematic version, 20260306: "Unable to load library". A
        // placed symbol sits directly in the (kicad_sch ...) form, next to that version.
        var schematic = KiCadSchematic.Parse(
            "(kicad_sch (version 20260306) (generator \"eeschema\") (lib_symbols (symbol \"Device:R\"))"
            + " (symbol (lib_id \"Device:R\") (at 0 0 0)))");
        var symbol = placed ? new KiCadSymbol(schematic.Symbols[0].Node) : schematic.LibrarySymbols[0];
        var library = new KiCadSymbolLibrary();

        library.AddSymbol(symbol);

        Assert.Equal(KiCadDefaults.SymbolLibraryVersion, library.Version);
    }

    [Theory]
    [InlineData("(kicad_symbol_lib (generator \"x\") (symbol \"A\"))")]
    [InlineData("(kicad_symbol_lib (version \"not-a-number\") (symbol \"A\"))")]
    public void ASourceWithNoUsableVersionLeavesTheVersionAlone(string text)
    {
        var library = new KiCadSymbolLibrary();

        library.AddSymbol(KiCadSymbolLibrary.Parse(text).Symbols[0]);

        Assert.Equal(KiCadDefaults.SymbolLibraryVersion, library.Version);
    }

    [Fact]
    public void TakingTheVersionChangesOnlyTheVersionAtom()
    {
        var library = KiCadSymbolLibrary.Parse("(kicad_symbol_lib\n\t(version 20211014)\n\t(generator \"g\")\n)");
        var before = library.ToText();

        library.AddSymbol(KiCadSymbolLibrary.Parse(KiCad10Library).Symbols[0]);
        library.RemoveSymbol("TILDE");

        Assert.Equal(before.Replace("20211014", "20251024", StringComparison.Ordinal), library.ToText());
    }

    /// <summary>
    /// The copy, handed to KiCad: <c>sym upgrade</c> must write back what it writes back for the
    /// library the symbol came from. Opt-in through <c>KICADSHARP_KICAD_CLI</c>, like every test
    /// here that needs KiCad.
    /// </summary>
    [Fact]
    public void KiCadReadsTheCopyAsItReadsTheSource()
    {
        if (TestData.KiCadCli is not { } cli)
        {
            return;
        }

        var scratch = TestData.NewScratchDirectory();
        var source = Path.Combine(scratch, "source.kicad_sym");
        File.WriteAllText(source, KiCad10Library);
        var copy = new KiCadSymbolLibrary();
        foreach (var symbol in KiCadSymbolLibrary.Load(source).Symbols.ToList())
        {
            copy.AddSymbol(symbol);
        }

        var ours = Path.Combine(scratch, "copy.kicad_sym");
        copy.Save(ours);

        var theirs = Upgrade(cli, source);
        var read = Upgrade(cli, ours);

        Assert.Contains("(property \"Value\" \"~\"", read, StringComparison.Ordinal);
        Assert.Contains("(name \"~\"", read, StringComparison.Ordinal);
        Assert.Contains("(mid -3.5355 3.5355)", read, StringComparison.Ordinal);
        Assert.Equal(theirs, read);
    }

    private static string Upgrade(string cli, string library)
    {
        var output = Path.ChangeExtension(library, ".upgraded.kicad_sym");
        var info = new ProcessStartInfo(cli)
        {
            WorkingDirectory = Path.GetDirectoryName(library)!,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in new[] { "sym", "upgrade", "--force", "-o", output, library })
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0 && File.Exists(output), $"kicad-cli could not upgrade {library} (exit {process.ExitCode}): {errors}");
        return File.ReadAllText(output);
    }
}
