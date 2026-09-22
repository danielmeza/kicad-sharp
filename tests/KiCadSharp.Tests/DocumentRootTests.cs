using System.Text;

using KiCadSharp.Documents;
using KiCadSharp.Schematics;

using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// Every document loader checks the root token before it hands anything back (#49).
/// </summary>
/// <remarks>
/// <para>
/// Before this, <see cref="KiCadSymbolLibrary"/> and <see cref="KiCadFootprintLibrary"/> took the
/// first form of any file. A <c>(kicad_pcb …)</c> read as a symbol library was a library with no
/// symbols, and a caller that added one and saved replaced the board with it.
/// </para>
/// <para>
/// Each loader is driven through all three of its entry points, <c>Load</c>, <c>LoadAsync</c> and
/// <c>Parse</c>, because each one used to reach the constructor on its own. The root token a
/// fixture really has is read off the raw tree rather than assumed from its extension.
/// </para>
/// </remarks>
public class DocumentRootTests
{
    private const string LegacyModuleText = "(module TEST (layer F.Cu))\n";

    /// <summary>A loader and the root tokens it accepts, in the order its message names them.</summary>
    /// <remarks>Each entry point returns the loaded document's text, so an accepted file can be compared with the original.</remarks>
    private sealed record Loader(
        string[] Roots,
        Func<string, string> Load,
        Func<string, Task<string>> LoadAsync,
        Func<string, string> Parse);

    private static readonly Dictionary<string, Loader> Loaders = new(StringComparer.Ordinal)
    {
        [nameof(KiCadSymbolLibrary)] = new(
            ["kicad_symbol_lib"],
            p => KiCadSymbolLibrary.Load(p).ToText(),
            async p => (await KiCadSymbolLibrary.LoadAsync(p)).ToText(),
            t => KiCadSymbolLibrary.Parse(t).ToText()),
        [nameof(KiCadFootprintLibrary)] = new(
            ["footprint", "module", "kicad_pcb"],
            p => KiCadFootprintLibrary.Load(p).ToText(),
            async p => (await KiCadFootprintLibrary.LoadAsync(p)).ToText(),
            t => KiCadFootprintLibrary.Parse(t).ToText()),
        [nameof(KiCadBoard)] = new(
            ["kicad_pcb"],
            p => KiCadBoard.Load(p).ToText(),
            async p => (await KiCadBoard.LoadAsync(p)).ToText(),
            t => KiCadBoard.Parse(t).ToText()),
        [nameof(KiCadSchematic)] = new(
            ["kicad_sch"],
            p => KiCadSchematic.Load(p).ToText(),
            async p => (await KiCadSchematic.LoadAsync(p)).ToText(),
            t => KiCadSchematic.Parse(t).ToText()),
    };

    private static readonly string[] EntryPoints = ["Load", "LoadAsync", "Parse"];

    /// <summary>One file of each root KiCad writes, including KiCad 5's <c>module</c>.</summary>
    private static readonly string[] Fixtures = ["symbol library", "footprint", "legacy module", "board", "schematic"];

    /// <summary>
    /// Files with no s-expression form in them at all. Each was checked to parse with no error and
    /// no root form, so the loader, not the parser, is what refuses them.
    /// </summary>
    private static readonly Dictionary<string, byte[]> FilesWithNoForm = new(StringComparer.Ordinal)
    {
        ["empty"] = [],
        ["whitespace"] = Encoding.UTF8.GetBytes("  \n\t\n"),
        ["prose"] = Encoding.UTF8.GetBytes("This is not a KiCad file.\n"),

        // A KiCad 5 symbol library: the file a .kicad_sym path is most plausibly holding by mistake.
        ["kicad 5 .lib"] = Encoding.UTF8.GetBytes(
            "EESchema-LIBRARY Version 2.4\n#encoding utf-8\n#\n# R\n#\nDEF R R 0 0 N Y 1 F N\n"
            + "F0 \"R\" 80 0 50 V V C CNN\nF1 \"R\" 0 0 50 V V C CNN\nDRAW\nS -40 -100 40 100 0 1 10 N\n"
            + "X ~ 1 0 150 50 D 50 50 1 1 P\nX ~ 2 0 -150 50 U 50 50 1 1 P\nENDDRAW\nENDDEF\n#\n#End Library\n"),

        // A .kicad_pro is JSON, and it sits next to every board and schematic.
        ["kicad_pro"] = File.ReadAllBytes(Path.Combine(TestData.Root, "duplicate-refs", "duplicate-refs.kicad_pro")),

        // The first bytes of a PNG, followed by bytes that are not valid UTF-8.
        ["binary"] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0xFF, 0xFE, 0x00],
    };

    /// <summary>Text the s-expression parser itself rejects, before any loader sees a root.</summary>
    private static readonly Dictionary<string, string> MalformedText = new(StringComparer.Ordinal)
    {
        ["unclosed"] = "(kicad_symbol_lib (version 20241209)\n",
        ["stray close"] = ")\n",
    };

    // ------------------------------------------------------------------------------- theory data

    public static TheoryData<string, string, string> AcceptedRoots() =>
        Pairs(accepted: true);

    public static TheoryData<string, string, string> RefusedRoots() =>
        Pairs(accepted: false);

    public static TheoryData<string, string, string> NoFormCases() =>
        Cross(FilesWithNoForm.Keys);

    public static TheoryData<string, string, string> MalformedCases() =>
        Cross(MalformedText.Keys);

    private static TheoryData<string, string, string> Pairs(bool accepted)
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (name, loader) in Loaders)
        {
            foreach (var fixture in Fixtures)
            {
                if (loader.Roots.Contains(RootTokenOf(fixture), StringComparer.Ordinal) != accepted)
                {
                    continue;
                }

                foreach (var entry in EntryPoints)
                {
                    data.Add(name, fixture, entry);
                }
            }
        }

        return data;
    }

    private static TheoryData<string, string, string> Cross(IEnumerable<string> cases)
    {
        var data = new TheoryData<string, string, string>();
        foreach (var name in Loaders.Keys)
        {
            foreach (var @case in cases)
            {
                foreach (var entry in EntryPoints)
                {
                    data.Add(name, @case, entry);
                }
            }
        }

        return data;
    }

    // ------------------------------------------------------------------------- the right root loads

    [Fact]
    public void TheMatrix_CoversEveryLoaderBothWays()
    {
        // A theory over an empty set passes and proves nothing. Of the twenty (loader, fixture)
        // pairs, six load: one each for the symbol library, board and schematic loaders, and three
        // for the footprint loader, which reads footprint, module and kicad_pcb. The other fourteen
        // must refuse.
        Assert.Equal(6 * EntryPoints.Length, AcceptedRoots().Count);
        Assert.Equal(14 * EntryPoints.Length, RefusedRoots().Count);
    }

    [Theory]
    [MemberData(nameof(AcceptedRoots))]
    public async Task Loader_AcceptsItsOwnRootAndKeepsEveryByte(string loader, string fixture, string entry)
    {
        var path = PathOf(fixture);

        var text = await Run(Loaders[loader], entry, path);

        Assert.Equal(File.ReadAllText(path), text);
    }

    // ------------------------------------------------------------------------ any other root throws

    [Theory]
    [MemberData(nameof(RefusedRoots))]
    public async Task Loader_RefusesAnyOtherRoot(string loader, string fixture, string entry)
    {
        var path = PathOf(fixture);
        var actual = RootTokenOf(fixture);

        var error = Assert.IsType<KiCadDocumentTypeException>(await Attempt(Loaders[loader], entry, path));

        Assert.Equal(actual, error.ActualRootToken);
        Assert.Equal(Loaders[loader].Roots, error.ExpectedRootTokens);
        Assert.Contains($"its root form is ({actual} ...)", error.Message, StringComparison.Ordinal);
        foreach (var root in Loaders[loader].Roots)
        {
            Assert.Contains($"({root} ...)", error.Message, StringComparison.Ordinal);
        }

        AssertNamesTheSource(error, entry, path);
    }

    [Fact]
    public void SymbolLibrary_RefusesABoardSavedUnderALibraryName()
    {
        // The downstream case in #49: a board sitting where the importer expects its symbol library.
        // It used to load as an empty library, and the importer's next save replaced the board.
        using var scratch = TestData.NewScratchDirectory();
        var path = Path.Combine(scratch, "orbion.kicad_sym");
        File.Copy(TestData.StackupBoard, path);
        var before = File.ReadAllBytes(path);

        var error = Assert.Throws<KiCadDocumentTypeException>(() => KiCadSymbolLibrary.Load(path));
        Assert.Throws<KiCadDocumentTypeException>(() => KiCadUtils.ParseSymbolLibrary(path));

        Assert.Equal(
            $"'{path}' is not a KiCad symbol library: expected a (kicad_symbol_lib ...) form at the root, but its root form is (kicad_pcb ...).",
            error.Message);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void FootprintLibrary_NamesAllThreeRootsItAccepts()
    {
        var error = Assert.Throws<KiCadDocumentTypeException>(() => KiCadFootprintLibrary.Load(TestData.SymbolLibrary));
        Assert.Throws<KiCadDocumentTypeException>(() => KiCadUtils.ParseFootprintLibrary(TestData.SymbolLibrary));

        Assert.Equal(
            $"'{TestData.SymbolLibrary}' is not a KiCad footprint or board: expected a (footprint ...), (module ...) or (kicad_pcb ...) form at the root, but its root form is (kicad_symbol_lib ...).",
            error.Message);
    }

    [Fact]
    public void Hierarchy_RefusesARootThatIsNotASchematic()
    {
        var error = Assert.Throws<KiCadDocumentTypeException>(() => SchematicHierarchy.Load(TestData.StackupBoard));

        Assert.Equal("kicad_pcb", error.ActualRootToken);
        Assert.Equal(TestData.StackupBoard, error.FilePath);
    }

    [Fact]
    public void TheException_IsStillAnInvalidOperationException()
    {
        // KiCadBoard and KiCadSchematic threw InvalidOperationException for a wrong root before the
        // specific type existed, and KiCadSchematic.Load documented it. A catch written against that
        // must still catch this.
        InvalidOperationException error = Assert.Throws<KiCadDocumentTypeException>(() => KiCadSchematic.Load(TestData.SymbolLibrary));

        Assert.Contains("kicad_symbol_lib", error.Message, StringComparison.Ordinal);
    }

    // --------------------------------------------------------------- an empty or garbage file throws

    [Theory]
    [MemberData(nameof(NoFormCases))]
    public async Task Loader_RefusesAFileWithNoForm(string loader, string @case, string entry)
    {
        using var scratch = TestData.NewScratchDirectory();
        var path = Path.Combine(scratch, "input.kicad_file");
        File.WriteAllBytes(path, FilesWithNoForm[@case]);

        var error = Assert.IsType<KiCadDocumentTypeException>(await Attempt(Loaders[loader], entry, path));

        Assert.Null(error.ActualRootToken);
        Assert.Equal(Loaders[loader].Roots, error.ExpectedRootTokens);
        Assert.EndsWith("but it holds no s-expression form.", error.Message, StringComparison.Ordinal);
        AssertNamesTheSource(error, entry, path);
    }

    [Theory]
    [MemberData(nameof(MalformedCases))]
    public async Task Loader_LeavesMalformedTextToTheParser(string loader, string @case, string entry)
    {
        // Not s-expressions at all: the parser says so, with a line and column, before there is a
        // root to check. That failure keeps its own type rather than being folded into this one.
        using var scratch = TestData.NewScratchDirectory();
        var path = Path.Combine(scratch, "input.kicad_file");
        File.WriteAllText(path, MalformedText[@case]);

        var error = Assert.IsType<SExpressionFormatException>(await Attempt(Loaders[loader], entry, path));

        Assert.InRange(error.Line, 1, int.MaxValue);
    }

    [Theory]
    [InlineData(nameof(KiCadSymbolLibrary))]
    [InlineData(nameof(KiCadFootprintLibrary))]
    [InlineData(nameof(KiCadBoard))]
    [InlineData(nameof(KiCadSchematic))]
    public void Loader_RefusesAFormWithNoToken(string loader)
    {
        var error = Assert.Throws<KiCadDocumentTypeException>(() => Loaders[loader].Parse("()\n"));

        Assert.Equal(string.Empty, error.ActualRootToken);
        Assert.EndsWith("but its root form has no token.", error.Message, StringComparison.Ordinal);
    }

    // --------------------------------------------------------- constructors over an existing form

    [Fact]
    public void SymbolLibrary_ConstructorRefusesAnotherForm()
    {
        var error = Assert.Throws<ArgumentException>(() => new KiCadSymbolLibrary(SExpression.Parse("(kicad_pcb (version 20241229))")));

        Assert.Equal("expression", error.ParamName);
        Assert.Contains("(kicad_pcb ...)", error.Message, StringComparison.Ordinal);
        Assert.Empty(new KiCadSymbolLibrary(SExpression.Parse("(kicad_symbol_lib (version 20241209))")).Symbols);
    }

    [Fact]
    public void FootprintLibrary_ConstructorAcceptsTheThreeRootsAndRefusesAnother()
    {
        Assert.True(new KiCadFootprintLibrary(SExpression.Parse("(footprint \"X\" (layer \"F.Cu\"))")).IsSingleFootprint);
        Assert.True(new KiCadFootprintLibrary(SExpression.Parse(LegacyModuleText)).IsSingleFootprint);
        Assert.False(new KiCadFootprintLibrary(SExpression.Parse("(kicad_pcb (version 20241229))")).IsSingleFootprint);

        var error = Assert.Throws<ArgumentException>(() => new KiCadFootprintLibrary(SExpression.Parse("(kicad_symbol_lib (version 20241209))")));
        Assert.Equal("expression", error.ParamName);
    }

    [Fact]
    public void Board_ConstructorStillRefusesAnotherForm()
    {
        var error = Assert.Throws<ArgumentException>(() => new KiCadBoard(SExpression.Parse("(kicad_sch (version 20250114))")));

        Assert.Equal("expression", error.ParamName);
        Assert.Equal("Expected a (kicad_pcb ...) form but got (kicad_sch ...). (Parameter 'expression')", error.Message);
    }

    // ------------------------------------------------------------------------------------ helpers

    private static string PathOf(string fixture) => fixture switch
    {
        "symbol library" => TestData.SymbolLibrary,
        "footprint" => TestData.Footprint,
        "legacy module" => LegacyModulePath.Value,
        "board" => TestData.StackupBoard,
        "schematic" => TestData.DuplicateRefsRoot,
        _ => throw new ArgumentOutOfRangeException(nameof(fixture), fixture, null),
    };

    // Read while the theory data is built and then by every case that names it, so no one test owns
    // the directory: it is left undisposed, and deleted when the test process exits.
    private static readonly Lazy<string> LegacyModulePath = new(() =>
    {
        var path = Path.Combine(TestData.NewScratchDirectory(nameof(LegacyModulePath)), "legacy.kicad_mod");
        File.WriteAllText(path, LegacyModuleText);
        return path;
    });

    /// <summary>The root token as the raw tree has it, with no loader in between.</summary>
    private static string RootTokenOf(string fixture) => SDocument.Load(PathOf(fixture)).Root!.Token;

    private static Task<string> Run(Loader loader, string entry, string path) => entry switch
    {
        "Load" => Task.FromResult(loader.Load(path)),
        "LoadAsync" => loader.LoadAsync(path),
        "Parse" => Task.FromResult(loader.Parse(Encoding.UTF8.GetString(File.ReadAllBytes(path)))),
        _ => throw new ArgumentOutOfRangeException(nameof(entry), entry, null),
    };

    private static async Task<Exception?> Attempt(Loader loader, string entry, string path)
    {
        try
        {
            await Run(loader, entry, path);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static void AssertNamesTheSource(KiCadDocumentTypeException error, string entry, string path)
    {
        if (entry == "Parse")
        {
            Assert.Null(error.FilePath);
            Assert.StartsWith("The text is not a KiCad ", error.Message, StringComparison.Ordinal);
            return;
        }

        Assert.Equal(path, error.FilePath);
        Assert.StartsWith($"'{path}' is not a KiCad ", error.Message, StringComparison.Ordinal);
    }
}
