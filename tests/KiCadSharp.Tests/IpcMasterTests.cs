using System.Text;
using System.Text.RegularExpressions;

using Google.Protobuf.WellKnownTypes;

using Kiapi.Board.Jobs;
using Kiapi.Board.Types;
using Kiapi.Common;
using Kiapi.Common.Commands;
using Kiapi.Common.Project;
using Kiapi.Common.Types;

namespace KiCadSharp.Tests;

/// <summary>
/// The commands KiCad master added over 10.0.6, against a real pcbnew.
/// </summary>
/// <remarks>
/// <para>
/// Run with the harness on the dev image, which carries KiCad master (10.99) beside 10.0.6:
/// </para>
/// <code>
/// export KICADSHARP_KICAD_FLAVOR=nightly
/// eval "$(scripts/kicad-ipc-container.sh start tests/KiCadSharp.Tests/data/kicad10-pcbnew.kicad_pcb)"
/// dotnet test KiCadSharp.slnx -c Release
/// </code>
/// <para>
/// Each test asks the KiCad it reached whether it supports the command, through the
/// <see cref="KiCadVersion"/> flags, and returns early when it does not; against the 10.0.6 image
/// only the ungated ones run. MEASURED 2026-09-21 against nightly <c>10.99.0-unknown-6e93fd642e</c> in the dev image:
/// every test here passed, including the one that pins the library table-editing commands as
/// unhandled.
/// </para>
/// </remarks>
public partial class IpcMasterTests
{
    [Fact]
    public async Task Commit_WithTheDocumentHeader_BeginsAndDrops()
    {
        // Ungated: 10.0.6 ignores the header, 10.0.7 and later read it. Either way it must not
        // break a commit.
        if (await LiveKiCad.Board() is not { } kicad)
        {
            return;
        }

        var board = await kicad.GetBoard();

        var commit = await board.BeginCommit();
        Assert.NotEmpty(commit.Id.Value);
        await board.DropCommit(commit);
    }

    [Fact]
    public async Task EmbeddedFiles_RoundTripThroughKiCad()
    {
        if (await LiveKiCad.Board(version => version.SupportsEmbeddedFiles) is not { } kicad)
        {
            return;
        }

        var board = await kicad.GetBoard();
        var content = Encoding.UTF8.GetBytes("kicad-sharp embedded-file round trip " + Guid.NewGuid());

        await board.AddEmbeddedFile("kicad-sharp-test.txt", content, EmbeddedFileType.EftDatasheet);
        try
        {
            var files = await board.GetEmbeddedFiles();
            var stored = Assert.Single(files, file => file.Name == "kicad-sharp-test.txt");

            // KiCad kept our packing as-is and validated the hash on the way in: the same bytes come
            // back, and they still verify.
            Assert.Equal(EmbeddedFileType.EftDatasheet, stored.Type);
            Assert.Equal(content, EmbeddedFileCodec.Unpack(stored));
        }
        finally
        {
            var others = (await board.GetEmbeddedFiles()).Where(file => file.Name != "kicad-sharp-test.txt").ToArray();
            await board.SetEmbeddedFiles(others);
        }

        Assert.DoesNotContain(await board.GetEmbeddedFiles(), file => file.Name == "kicad-sharp-test.txt");
    }

    [Fact]
    public async Task EmbeddedFiles_KiCadRejectsAWrongHash()
    {
        if (await LiveKiCad.Board(version => version.SupportsEmbeddedFiles) is not { } kicad)
        {
            return;
        }

        var board = await kicad.GetBoard();
        var file = EmbeddedFileCodec.Pack("kicad-sharp-bad-hash.txt", "tampered"u8);
        file.DataHash = EmbeddedFileCodec.ComputeHash("something else"u8);

        // This is why the codec exists: KiCad checks data_hash against the content and refuses
        // the whole request when they disagree.
        var failure = await Assert.ThrowsAsync<ApiException>(async () => await board.AddEmbeddedFiles(file));
        Assert.Contains("validation failed", failure.Message);
    }

    [Fact]
    public async Task Variants_RoundTripThroughKiCad()
    {
        if (await LiveKiCad.Board(version => version.SupportsVariants) is not { } kicad)
        {
            return;
        }

        var board = await kicad.GetBoard();
        var name = "kicad-sharp-" + Guid.NewGuid().ToString("N")[..8];

        await board.AddVariant(name, "added by the tests");
        try
        {
            Assert.Contains(await board.GetVariants(), variant => variant.Name == name && variant.Description == "added by the tests");

            await board.RenameVariant(name, name + "-renamed");
            await board.SetCurrentVariant(name + "-renamed");
            Assert.Equal(name + "-renamed", await board.GetCurrentVariant());

            await board.SetCurrentVariant(null);
            Assert.Null(await board.GetCurrentVariant());
        }
        finally
        {
            foreach (var variant in await board.GetVariants())
            {
                if (variant.Name.StartsWith(name, StringComparison.Ordinal))
                {
                    await board.DeleteVariant(variant.Name);
                }
            }
        }

        Assert.DoesNotContain(await board.GetVariants(), variant => variant.Name.StartsWith(name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ModifiedState_IsKnown()
    {
        if (await LiveKiCad.Board(version => version.SupportsJobs) is not { } kicad)
        {
            return;
        }

        var state = await (await kicad.GetBoard()).GetModifiedState();

        Assert.NotEqual(DocumentModifiedState.DmsUnknown, state);
    }

    [Fact]
    public async Task PageSettings_RoundTripThroughKiCad()
    {
        if (await LiveKiCad.Board(version => version.SupportsJobs) is not { } kicad)
        {
            return;
        }

        var board = await kicad.GetBoard();
        var settings = await board.GetPageSettings();

        Assert.NotEqual(PageSize.PsUnknown, settings.PageSize);
        var applied = await board.SetPageSettings(settings);
        Assert.Equal(settings.PageSize, applied.PageSize);
    }

    [Fact]
    public async Task FocusOnItems_AcceptsTheBoardsFootprint()
    {
        if (await LiveKiCad.Board(version => version.SupportsJobs) is not { } kicad)
        {
            return;
        }

        var board = await kicad.GetBoard();
        var footprint = ((Any)Assert.Single(await board.GetItems(KiCadObjectType.KotPcbFootprint))).Unpack<FootprintInstance>();

        await board.FocusOnItems([footprint.Id], new Distance { ValueNm = 1_000_000 });
    }

    [Fact]
    public async Task DesignRules_ReadAndWriteBack()
    {
        if (await LiveKiCad.Board(version => version.SupportsJobs) is not { } kicad)
        {
            return;
        }

        var board = await kicad.GetBoard();

        var rules = await board.GetDesignRules();
        Assert.NotNull(rules.Rules);
        Assert.NotEqual(Kiapi.Board.Commands.CustomRulesStatus.CrsUnknown, rules.CustomRulesStatus);

        var written = await board.SetDesignRules(rules.Rules);
        Assert.NotNull(written.Rules);

        var custom = await board.GetCustomDesignRules();
        Assert.NotEqual(Kiapi.Board.Commands.CustomRulesStatus.CrsUnknown, custom.Status);
    }

    [Fact]
    public async Task PlotSettings_RoundTripThroughKiCad()
    {
        if (await LiveKiCad.Board(version => version.SupportsJobs) is not { } kicad)
        {
            return;
        }

        var board = await kicad.GetBoard();

        var settings = await board.GetPlotSettings();
        await board.SetPlotSettings(settings);
        Assert.Equal(settings, await board.GetPlotSettings());
    }

    [Fact]
    public async Task RunJob_ExportsAnSvg()
    {
        if (await LiveKiCad.Board(version => version.SupportsJobs) is not { } kicad)
        {
            return;
        }

        var board = await kicad.GetBoard();
        var job = new RunBoardJobExportSvg
        {
            PlotSettings = new BoardPlotSettings { Layers = { BoardLayer.BlFCu, BoardLayer.BlEdgeCuts } },
            PageMode = BoardJobPaginationMode.BjpmAllLayersOnePage,
        };

        // /project is the container's view of the harness's project directory.
        var response = await board.RunJob(job, "/project/kicad-sharp-test.svg");

        Assert.True(response.Status is JobStatus.JsSuccess or JobStatus.JsWarning, response.Message);
        Assert.NotEmpty(response.OutputPath);

        if (LiveKiCad.BoardProject is { } project)
        {
            var written = Directory.GetFiles(project, "*.svg", SearchOption.AllDirectories);
            Assert.NotEmpty(written);
            Assert.StartsWith("<?xml", File.ReadAllText(written[0]).TrimStart());
        }
    }

    [Fact]
    public async Task GetPaths_NamesKiCadsDirectories()
    {
        if (await LiveKiCad.Board(version => version.SupportsJobs) is not { } kicad)
        {
            return;
        }

        var paths = await kicad.GetPaths();

        Assert.NotEmpty(paths);
        Assert.All(paths.Values, path => Assert.True(Path.IsPathRooted(path), path));
    }

    [Fact]
    public async Task Libraries_StatusesAndLoadAll()
    {
        if (await LiveKiCad.Board(version => version.SupportsLibraryCommands) is not { } kicad)
        {
            return;
        }

        var statuses = await kicad.GetLibraryStatuses();
        Assert.All(statuses, status => Assert.NotEqual(LibraryLoadStatus.LlsUnknown, status.Status));

        // The proto says LoadAllLibraries is a no-op in GUI mode; MEASURED at 6e93fd64, the handler
        // answers AS_UNIMPLEMENTED ("LoadAllLibraries is not available in GUI mode") instead. It is
        // for kicad-cli api-server, which the harness does not run.
        var failure = await Assert.ThrowsAsync<ApiException>(async () => await kicad.LoadAllLibraries());
        Assert.Contains("AS_UNIMPLEMENTED", failure.Message);
    }

    [Fact]
    public async Task Libraries_ListAndReadAStockFootprint()
    {
        if (await LiveKiCad.Board(version => version.SupportsLibraryCommands) is not { } kicad)
        {
            return;
        }

        // The harness seeds library tables that point at KiCad's stock ones, so Resistor_SMD is
        // there when the image ships kicad-footprints. If it is not, there is nothing to read.
        // Libraries load in the background after the editor starts, and GetLibraryItems lists an
        // unloaded library as empty, so wait for this one to report loaded first.
        LibraryStatusEntry? resistors = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var statuses = await kicad.GetLibraryStatuses(LibraryTableScope.LtsGlobal, default, LibraryType.LtFootprint);
            resistors = statuses.FirstOrDefault(status => status.Entry.Nickname == "Resistor_SMD");
            if (resistors is null || resistors.Status is LibraryLoadStatus.LlsLoaded or LibraryLoadStatus.LlsError or LibraryLoadStatus.LlsInvalid)
            {
                break;
            }

            await Task.Delay(1000);
        }

        if (resistors is null || resistors.Status != LibraryLoadStatus.LlsLoaded)
        {
            return;
        }

        var items = await kicad.GetLibraryItems(LibraryType.LtFootprint, "Resistor_SMD");
        Assert.NotEmpty(items);
        var target = Assert.Single(items, item => item.EntryName == "R_0402_1005Metric");

        var read = await kicad.GetItemsFromLibrary(LibraryType.LtFootprint, target);
        var footprint = ((Any)Assert.Single(read)).Unpack<Footprint>();
        Assert.NotEmpty(footprint.Items);   // the pads and graphics, packed
    }

    [Fact]
    public async Task LibraryTableEditing_IsUnhandledOnMaster()
    {
        if (await LiveKiCad.Board(version => version.SupportsLibraryCommands) is not { } kicad)
        {
            return;
        }

        // Pinned as measured: master declares GetLibraryTable and the table mutations (Since:
        // 11.0) and registers no handler for them. When this test starts failing, a handler has
        // landed upstream -- turn it into a round trip and drop the note in docs/ipc.md.
        var failure = await Assert.ThrowsAsync<ApiException>(async () => await kicad.GetLibraryTable(LibraryType.LtFootprint));
        Assert.Contains("AS_UNHANDLED", failure.Message);
    }

    [Fact]
    public async Task CrossProbe_HighlightNetsAndSyncSelection()
    {
        if (await LiveKiCad.Board(version => version.SupportsJobs) is not { } kicad)
        {
            return;
        }

        var board = await kicad.GetBoard();

        var highlight = await kicad.HighlightNets("GND");
        Assert.True(highlight.Status is CrossProbeStatus.CpsOk or CrossProbeStatus.CpsNotFound, highlight.Message);
        Assert.Equal(CrossProbeStatus.CpsOk, (await kicad.HighlightNets()).Status);

        // The fixture's one footprint, by its reference, which the board file says.
        var reference = ReferenceDesignator().Match(await board.GetAsString()).Groups[1].Value;
        var spec = new SelectionSpec { Footprint = new FootprintSelectionSpec { Reference = reference } };
        var sync = await kicad.SyncSelection(SyncSelectionContext.SscExplicit, SyncSelectionMode.SsmItemsOnly, spec, spec);
        Assert.Equal(CrossProbeStatus.CpsOk, sync.Status);

        Assert.Single(await board.GetSelection());
        await board.ClearSelection();
    }

    [Fact]
    public async Task Project_NetClassesAndAssignments()
    {
        if (await LiveKiCad.Board() is not { } kicad)
        {
            return;
        }

        var project = (await kicad.GetBoard()).GetProject();

        // Ungated: the project specifier is new in the request, and a 10.0.6 KiCad ignores it.
        var classes = await project.GetNetClasses();
        Assert.Contains(classes, netClass => netClass.Name == "Default");

        var version = await kicad.GetVersion();
        if (version.SupportsJobs)
        {
            var assignments = await project.GetNetClassAssignments();
            Assert.NotNull(assignments);
        }
    }

    [Fact]
    public async Task Project_TextVariables_RoundTripThroughTheBareProjectSpecifier()
    {
        // Ungated on purpose: Project now sends a specifier with only DOCTYPE_PROJECT and the
        // project, and this is the measurement that a 10.0.6 KiCad and master both accept it for
        // the three project-handler commands (set, get, expand).
        if (await LiveKiCad.Board() is not { } kicad)
        {
            return;
        }

        var board = await kicad.GetBoard();
        var project = board.GetProject();
        var original = await project.GetTextVariables();
        var value = "kicad-sharp-" + Guid.NewGuid().ToString("N")[..8];

        await project.SetTextVariables(new TextVariables { Variables = { ["KICADSHARP_TEST"] = value } });
        try
        {
            Assert.Equal(value, (await project.GetTextVariables()).Variables["KICADSHARP_TEST"]);
            Assert.Equal(value, (await kicad.GetTextVariables())["KICADSHARP_TEST"]);

            // Expansion through the document: the board's resolver sees project variables too,
            // and this is the one that answers on 10.0.6 as well as on master.
            Assert.Equal(value, await board.ExpandTextVariables("${KICADSHARP_TEST}"));
            Assert.Equal([value, "x"], await board.ExpandTextVariables(["${KICADSHARP_TEST}", "x"]));

            // Expansion through the project: MEASURED, 10.0.6's pcbnew answers it first and
            // rejects the project document ("the requested document  is not open"); master
            // passes it on to the project handler.
            if ((await kicad.GetVersion()).SupportsJobs)
            {
                Assert.Equal(value, await project.ExpandTextVariables("${KICADSHARP_TEST}"));
            }
        }
        finally
        {
            await project.SetTextVariables(original, MapMergeMode.MmmReplace);
        }

        Assert.False((await project.GetTextVariables()).Variables.ContainsKey("KICADSHARP_TEST"));
    }

    [Fact]
    public async Task Project_ExpandsEnvironmentVariablesOnRequest()
    {
        if (await LiveKiCad.Board(version => version.SupportsEmbeddedFiles) is not { } kicad)
        {
            return;
        }

        var board = await kicad.GetBoard();

        var expanded = await board.ExpandTextVariables("${KIPRJMOD}/x", expandEnvironmentVariables: true);
        Assert.DoesNotContain("${KIPRJMOD}", expanded);
        Assert.EndsWith("/x", expanded);
    }

    [GeneratedRegex("\\(property \"Reference\" \"([^\"]+)\"")]
    private static partial Regex ReferenceDesignator();
}
