using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Kiapi.Board;
using Kiapi.Board.Commands;
using Kiapi.Board.Jobs;
using Kiapi.Board.Types;
using Kiapi.Common;
using Kiapi.Common.Commands;
using Kiapi.Common.Project;
using Kiapi.Common.Types;
using Kiapi.Schematic.Commands;
using Kiapi.Schematic.Jobs;
using Kiapi.Schematic.Types;

using Microsoft.Extensions.Logging.Abstractions;

namespace KiCadSharp.Tests;

/// <summary>
/// Every command KiCad master added since 10.0.6, sent through the real client and transport to an
/// in-process peer that records what arrived and answers with a scripted reply.
/// </summary>
/// <remarks>
/// These pin the wire shape: which message goes out, which document or header it carries, how the
/// reply is unpacked. What KiCad does with the command is only measurable against a KiCad built
/// from master, which is the live half in <see cref="IpcTests"/>.
/// </remarks>
public class IpcCommandTests
{
    private static readonly DocumentSpecifier BoardDocument = new()
    {
        Type = DocumentType.DoctypePcb,
        BoardFilename = "probe.kicad_pcb",
        Project = new ProjectSpecifier { Name = "probe", Path = "/work/probe" },
    };

    /// <summary>What <see cref="Project"/> sends for <see cref="BoardDocument"/>'s project: the path with the separator KiCad compares against.</summary>
    private static readonly ProjectSpecifier BoardProject = new() { Name = "probe", Path = "/work/probe/" };

    private static readonly DocumentSpecifier SchematicDocument = new()
    {
        Type = DocumentType.DoctypeSchematic,
        SheetPath = new SheetPath { PathHumanReadable = "/" },
        Project = new ProjectSpecifier { Name = "probe", Path = "/work/probe" },
    };

    private static KiCadIPCClient Client(NngTestPeer peer) =>
        new(new KiCadClientSettings { PipeName = peer.Url, ClientName = "kicad-sharp-tests" }, NullLogger<KiCadIPCClient>.Instance);

    // ------------------------------------------------------------------------------- commits

    [Fact]
    public async Task BeginCommit_NamesTheDocument()
    {
        using var peer = NngTestPeer.Start(new BeginCommitResponse { Id = new KIID { Value = "c1" } });
        using var client = Client(peer);

        var commit = await new Board(client, BoardDocument).BeginCommit();

        Assert.Equal("c1", commit.Id.Value);
        Assert.Equal(BoardDocument, peer.Single<BeginCommit>().Header.Document);
    }

    [Fact]
    public async Task PushCommit_AndDropCommit_NameTheDocument()
    {
        using var peer = NngTestPeer.Start(new EndCommitResponse());
        using var client = Client(peer);
        var board = new Board(client, BoardDocument);

        await board.PushCommit(new Commit(new KIID { Value = "c1" }), "why");
        await board.DropCommit(new Commit(new KIID { Value = "c2" }));

        var requests = peer.Requests.Select(request => request.Message.Unpack<EndCommit>()).ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Equal(CommitAction.CmaCommit, requests[0].Action);
        Assert.Equal("why", requests[0].Message);
        Assert.Equal(CommitAction.CmaDrop, requests[1].Action);
        Assert.All(requests, request => Assert.Equal(BoardDocument, request.Header.Document));
    }

    // ------------------------------------------------------------------------ editor commands

    [Fact]
    public async Task GetModifiedState_UnpacksTheState()
    {
        using var peer = NngTestPeer.Start(new GetDocumentModifiedStateResponse { State = DocumentModifiedState.DmsModified });
        using var client = Client(peer);

        var state = await new Board(client, BoardDocument).GetModifiedState();

        Assert.Equal(DocumentModifiedState.DmsModified, state);
        Assert.Equal(BoardDocument, peer.Single<GetDocumentModifiedState>().Document);
    }

    [Fact]
    public async Task FocusOnItems_SendsTheIdsAndTheMargin()
    {
        using var peer = NngTestPeer.Start(new Empty());
        using var client = Client(peer);

        await new Board(client, BoardDocument).FocusOnItems([new KIID { Value = "a" }, new KIID { Value = "b" }], new Distance { ValueNm = 500_000 });

        var request = peer.Single<FocusOnItems>();
        Assert.Equal(BoardDocument, request.Document);
        Assert.Equal(["a", "b"], request.Items.Select(id => id.Value));
        Assert.Equal(500_000, request.Margin.ValueNm);
    }

    [Fact]
    public async Task PageSettings_RoundTrip()
    {
        var settings = new PageSettings { PageSize = PageSize.PsA3, Orientation = PageOrientation.PoLandscape };
        using var peer = NngTestPeer.Start(settings);
        using var client = Client(peer);
        var schematic = new Schematic(client, SchematicDocument);

        Assert.Equal(settings, await schematic.GetPageSettings());
        Assert.Equal(settings, await schematic.SetPageSettings(settings));

        var requests = peer.Requests;
        Assert.Equal(SchematicDocument, requests[0].Message.Unpack<GetPageSettings>().Document);
        var set = requests[1].Message.Unpack<SetPageSettings>();
        Assert.Equal(SchematicDocument, set.Document);
        Assert.Equal(settings, set.PageSettings);
    }

    // ------------------------------------------------------------------------------ variants

    [Fact]
    public async Task Variants_EveryCommandCarriesTheDocument()
    {
        using var peer = NngTestPeer.Start(request =>
            request.Message.Is(GetVariants.Descriptor)
                ? NngTestPeer.Ok(new VariantsResponse { Variants = { new DesignVariant { Name = "A" }, new DesignVariant { Name = "B" } } })
                : request.Message.Is(GetCurrentVariant.Descriptor)
                    ? NngTestPeer.Ok(new CurrentVariantResponse { Name = "A" })
                    : NngTestPeer.Ok(new Empty()));
        using var client = Client(peer);
        var board = new Board(client, BoardDocument);

        Assert.Equal(["A", "B"], (await board.GetVariants()).Select(variant => variant.Name));
        await board.AddVariant("A", "first");
        await board.AddVariant("B");
        await board.RenameVariant("B", "C");
        await board.SetVariantDescription("C", "third");
        await board.CopyVariant("A", "D", "copy");
        await board.SetCurrentVariant("A");
        Assert.Equal("A", await board.GetCurrentVariant());
        await board.SetCurrentVariant(null);
        await board.DeleteVariant("D");

        var requests = peer.Requests;
        Assert.Equal(10, requests.Length);

        var add = requests[1].Message.Unpack<AddVariant>();
        Assert.Equal("A", add.Name);
        Assert.Equal("first", add.Description);
        Assert.False(requests[2].Message.Unpack<AddVariant>().HasDescription);

        var rename = requests[3].Message.Unpack<RenameVariant>();
        Assert.Equal(("B", "C"), (rename.OldName, rename.NewName));
        Assert.Equal("third", requests[4].Message.Unpack<SetVariantDescription>().Description);

        var copy = requests[5].Message.Unpack<CopyVariant>();
        Assert.Equal(("A", "D", "copy"), (copy.OldName, copy.NewName, copy.NewDescription));

        Assert.Equal("A", requests[6].Message.Unpack<SetCurrentVariant>().Name);
        Assert.False(requests[8].Message.Unpack<SetCurrentVariant>().HasName);   // null selects the default variant
        Assert.Equal("D", requests[9].Message.Unpack<DeleteVariant>().Name);

        // Every one of them is scoped to the document, which is what makes board and schematic
        // variants distinct in KiCad.
        foreach (var request in requests)
        {
            // Field 1 is the document in all ten messages, so any of them parses as GetVariants.
            // Any.Unpack would refuse the type URL; the parser does not care.
            var document = GetVariants.Parser.ParseFrom(request.Message.Value).Document;
            Assert.Equal(BoardDocument, document);
        }
    }

    [Fact]
    public async Task GetCurrentVariant_IsNullForTheDefaultVariant()
    {
        using var peer = NngTestPeer.Start(new CurrentVariantResponse());
        using var client = Client(peer);

        Assert.Null(await new Board(client, BoardDocument).GetCurrentVariant());
    }

    // ---------------------------------------------------------------------------------- jobs

    [Fact]
    public async Task RunJob_FillsInTheJobSettings_AndReturnsTheResponse()
    {
        var expected = new RunJobResponse { Status = JobStatus.JsSuccess, OutputPath = { "/out/probe-F_Cu.gbr" } };
        using var peer = NngTestPeer.Start(expected);
        using var client = Client(peer);

        var response = await new Board(client, BoardDocument).RunJob(new RunBoardJobExportGerbers { UseX2Format = true }, "/out");

        Assert.Equal(expected, response);
        var job = peer.Single<RunBoardJobExportGerbers>();
        Assert.True(job.UseX2Format);
        Assert.Equal(BoardDocument, job.JobSettings.Document);
        Assert.Equal("/out", job.JobSettings.OutputPath);
    }

    [Fact]
    public async Task RunJob_WorksForSchematicJobsToo()
    {
        using var peer = NngTestPeer.Start(new RunJobResponse { Status = JobStatus.JsWarning, Message = "no title block" });
        using var client = Client(peer);

        var response = await new Schematic(client, SchematicDocument).RunJob(new RunSchematicJobExportPdf { HierarchicalLinks = true }, "/out/probe.pdf");

        Assert.Equal(JobStatus.JsWarning, response.Status);
        Assert.Equal(SchematicDocument, peer.Single<RunSchematicJobExportPdf>().JobSettings.Document);
    }

    [Fact]
    public async Task RunJob_RefusesAMessageThatIsNotAJob()
    {
        using var peer = NngTestPeer.Start(new Empty());
        using var client = Client(peer);

        var failure = await Assert.ThrowsAsync<ArgumentException>(async () => await new Board(client, BoardDocument).RunJob(new Ping(), "/out"));

        Assert.Contains("job_settings", failure.Message);
        Assert.Empty(peer.Requests);
    }

    // --------------------------------------------------------------------------------- board

    [Fact]
    public async Task EmbeddedFiles_GoOutPacked_AndComeBackDecodable()
    {
        var datasheet = "%PDF-1.4 not really"u8.ToArray();
        var stored = EmbeddedFileCodec.Pack("ds.pdf", datasheet, EmbeddedFileType.EftDatasheet);
        using var peer = NngTestPeer.Start(request =>
            request.Message.Is(GetEmbeddedFiles.Descriptor)
                ? NngTestPeer.Ok(new EmbeddedFiles { Files = { stored } })
                : NngTestPeer.Ok(new Empty()));
        using var client = Client(peer);
        var board = new Board(client, BoardDocument);

        await board.AddEmbeddedFile("ds.pdf", datasheet, EmbeddedFileType.EftDatasheet);
        await board.SetEmbeddedFiles(stored, stored);
        var files = await board.GetEmbeddedFiles();

        var added = Assert.Single(peer.Requests[0].Message.Unpack<AddEmbeddedFiles>().Files.Files);
        Assert.Equal(stored, added);
        Assert.Equal(BoardDocument, peer.Requests[0].Message.Unpack<AddEmbeddedFiles>().Board);

        var set = peer.Requests[1].Message.Unpack<SetEmbeddedFiles>();
        Assert.Equal(2, set.Files.Files.Count);
        Assert.Equal(BoardDocument, set.Board);

        Assert.Equal(BoardDocument, peer.Requests[2].Message.Unpack<GetEmbeddedFiles>().Board);
        var file = Assert.Single(files);
        Assert.Equal(datasheet, EmbeddedFileCodec.Unpack(file));
    }

    [Fact]
    public async Task DesignRules_RoundTrip()
    {
        var reply = new BoardDesignRulesResponse { Rules = new BoardDesignRules(), CustomRulesStatus = CustomRulesStatus.CrsValid };
        using var peer = NngTestPeer.Start(reply);
        using var client = Client(peer);
        var board = new Board(client, BoardDocument);

        Assert.Equal(reply, await board.GetDesignRules());
        Assert.Equal(reply, await board.SetDesignRules(reply.Rules));

        Assert.Equal(BoardDocument, peer.Requests[0].Message.Unpack<GetBoardDesignRules>().Board);
        var set = peer.Requests[1].Message.Unpack<SetBoardDesignRules>();
        Assert.Equal(BoardDocument, set.Board);
        Assert.Equal(reply.Rules, set.Rules);
    }

    [Fact]
    public async Task CustomDesignRules_RoundTrip()
    {
        var reply = new CustomRulesResponse { Status = CustomRulesStatus.CrsInvalid, ErrorText = "line 3" };
        using var peer = NngTestPeer.Start(reply);
        using var client = Client(peer);
        var board = new Board(client, BoardDocument);

        Assert.Equal(reply, await board.GetCustomDesignRules());
        Assert.Equal(reply, await board.SetCustomDesignRules(new CustomRule(), new CustomRule()));

        Assert.Equal(BoardDocument, peer.Requests[0].Message.Unpack<GetCustomDesignRules>().Board);
        var set = peer.Requests[1].Message.Unpack<SetCustomDesignRules>();
        Assert.Equal(BoardDocument, set.Board);
        Assert.Equal(2, set.Rules.Count);
    }

    [Fact]
    public async Task ImportNetlist_SendsEveryOption()
    {
        using var peer = NngTestPeer.Start(new ImportNetlistResponse { WarningCount = 2, NewFootprintCount = 5, Report = "ok" });
        using var client = Client(peer);

        var response = await new Board(client, BoardDocument).ImportNetlist(
            "/work/probe.net", dryRun: true, NetlistMatchMode.NmmReference,
            deleteExtraFootprints: true, updateFootprints: true, transferGroups: true, overrideLocks: true);

        Assert.Equal(5u, response.NewFootprintCount);
        var request = peer.Single<ImportNetlist>();
        Assert.Equal(BoardDocument, request.Board);
        Assert.Equal("/work/probe.net", request.NetlistPath);
        Assert.True(request.DryRun);
        Assert.Equal(NetlistMatchMode.NmmReference, request.MatchMode);
        Assert.True(request.DeleteExtraFootprints && request.UpdateFootprints && request.TransferGroups && request.OverrideLocks);
    }

    [Fact]
    public async Task PlotSettings_RoundTrip()
    {
        var settings = new BoardPlotSettings { Mirror = true, DrillMarks = PlotDrillMarks.PdmSmall };
        using var peer = NngTestPeer.Start(request =>
            request.Message.Is(GetBoardPlotSettings.Descriptor)
                ? NngTestPeer.Ok(new BoardPlotSettingsResponse { PlotSettings = settings })
                : NngTestPeer.Ok(new Empty()));
        using var client = Client(peer);
        var board = new Board(client, BoardDocument);

        Assert.Equal(settings, await board.GetPlotSettings());
        await board.SetPlotSettings(settings);

        Assert.Equal(BoardDocument, peer.Requests[0].Message.Unpack<GetBoardPlotSettings>().Board);
        var set = peer.Requests[1].Message.Unpack<SetBoardPlotSettings>();
        Assert.Equal(BoardDocument, set.Board);
        Assert.Equal(settings, set.PlotSettings);
    }

    [Fact]
    public async Task PlaceFootprintFromLibrary_UnpacksTheFootprint()
    {
        var placed = new FootprintInstance { Id = new KIID { Value = "fp1" } };
        using var peer = NngTestPeer.Start(new PlaceFromLibraryResponse { Item = Any.Pack(placed) });
        using var client = Client(peer);
        var libraryId = new LibraryIdentifier { LibraryNickname = "Resistor_SMD", EntryName = "R_0402_1005Metric" };

        var footprint = await new Board(client, BoardDocument).PlaceFootprintFromLibrary(
            libraryId, new Vector2 { XNm = 1, YNm = 2 }, new Angle { ValueDegrees = 90 }, BoardLayer.BlBCu);

        Assert.Equal(placed, footprint);
        var request = peer.Single<PlaceFootprintFromLibrary>();
        Assert.Equal(BoardDocument, request.Header.Document);
        Assert.Equal(libraryId, request.LibId);
        Assert.Equal(90, request.Orientation.ValueDegrees);
        Assert.Equal(BoardLayer.BlBCu, request.Layer);
    }

    [Fact]
    public async Task PlaceFootprintFromLibrary_LeavesOrientationAndLayerUnsetByDefault()
    {
        using var peer = NngTestPeer.Start(new PlaceFromLibraryResponse { Item = Any.Pack(new FootprintInstance()) });
        using var client = Client(peer);

        await new Board(client, BoardDocument).PlaceFootprintFromLibrary(new LibraryIdentifier(), new Vector2());

        var request = peer.Single<PlaceFootprintFromLibrary>();
        Assert.Null(request.Orientation);
        Assert.False(request.HasLayer);
    }

    [Fact]
    public async Task PlaceFootprintFromLibrary_ThrowsWhenKiCadReturnsSomethingElse()
    {
        using var peer = NngTestPeer.Start(new PlaceFromLibraryResponse { Item = Any.Pack(new Empty()) });
        using var client = Client(peer);

        var failure = await Assert.ThrowsAsync<ApiException>(async () =>
            await new Board(client, BoardDocument).PlaceFootprintFromLibrary(new LibraryIdentifier(), new Vector2()));

        Assert.Contains("not a FootprintInstance", failure.Message);
    }

    // ----------------------------------------------------------------------------- schematic

    [Fact]
    public async Task GetSchematic_BindsTheFirstOpenSchematic()
    {
        using var peer = NngTestPeer.Start(new GetOpenDocumentsResponse { Documents = { SchematicDocument } });
        using var client = Client(peer);

        var schematic = await new KiCad(client).GetSchematic();

        Assert.Equal(SchematicDocument, schematic.Document);
        Assert.Equal("/", schematic.Name);
        Assert.Equal(DocumentType.DoctypeSchematic, peer.Single<GetOpenDocuments>().Type);
    }

    [Fact]
    public async Task GetSchematic_ThrowsWhenNoneIsOpen()
    {
        using var peer = NngTestPeer.Start(new GetOpenDocumentsResponse());
        using var client = Client(peer);

        await Assert.ThrowsAsync<ApiException>(async () => await new KiCad(client).GetSchematic());
    }

    [Fact]
    public async Task GetHierarchy_AndGetNetlist_NameTheSchematic()
    {
        using var peer = NngTestPeer.Start(request =>
            request.Message.Is(GetSchematicHierarchy.Descriptor)
                ? NngTestPeer.Ok(new SchematicHierarchyResponse { TopLevelSheets = { new SheetInstance() } })
                : NngTestPeer.Ok(new SchematicNetlistResponse { Nets = { new SchematicNet(), new SchematicNet() } }));
        using var client = Client(peer);
        var schematic = new Schematic(client, SchematicDocument);

        Assert.Single(await schematic.GetHierarchy());
        Assert.Equal(2, (await schematic.GetNetlist(KiCadObjectType.KotSchPin)).Length);

        Assert.Equal(SchematicDocument, peer.Requests[0].Message.Unpack<GetSchematicHierarchy>().Document);
        var netlist = peer.Requests[1].Message.Unpack<GetSchematicNetlist>();
        Assert.Equal(SchematicDocument, netlist.Document);
        Assert.Equal([KiCadObjectType.KotSchPin], netlist.Types_);
    }

    [Fact]
    public async Task PlaceSymbolFromLibrary_UnpacksTheSymbol()
    {
        var placed = new SchematicSymbolInstance { Id = new KIID { Value = "sym1" } };
        using var peer = NngTestPeer.Start(new PlaceFromLibraryResponse { Item = Any.Pack(placed) });
        using var client = Client(peer);
        var libraryId = new LibraryIdentifier { LibraryNickname = "Device", EntryName = "R" };

        var symbol = await new Schematic(client, SchematicDocument).PlaceSymbolFromLibrary(libraryId, new Vector2 { XNm = 3 }, reference: "R7");

        Assert.Equal(placed, symbol);
        var request = peer.Single<PlaceSymbolFromLibrary>();
        Assert.Equal(SchematicDocument, request.Header.Document);
        Assert.Equal(libraryId, request.LibId);
        Assert.Equal("R7", request.Reference);
        Assert.False(request.HasOrientation);
        Assert.Null(request.Unit);
    }

    // ---------------------------------------------------------------------- kicad: documents

    [Fact]
    public async Task GetPaths_ReturnsThemByKind()
    {
        using var peer = NngTestPeer.Start(new GetPathsResponse
        {
            Paths = { new PathEntry { Type = PathType.PathUserPlugins, Path = "/home/me/.local/share/kicad/11.0/plugins" } },
        });
        using var client = Client(peer);

        var paths = await new KiCad(client).GetPaths();

        Assert.Equal("/home/me/.local/share/kicad/11.0/plugins", paths[PathType.PathUserPlugins]);
        peer.Single<GetPaths>();
    }

    [Fact]
    public async Task OpenCreateAndCloseDocuments()
    {
        using var peer = NngTestPeer.Start(request =>
            request.Message.Is(OpenDocument.Descriptor) || request.Message.Is(CreateDocument.Descriptor)
                ? NngTestPeer.Ok(new OpenDocumentResponse { Document = BoardDocument })
                : NngTestPeer.Ok(new Empty()));
        using var client = Client(peer);
        var kicad = new KiCad(client);

        Assert.Equal(BoardDocument, await kicad.OpenDocument(DocumentType.DoctypePcb, "/work/probe.kicad_pcb"));
        Assert.Equal(BoardDocument, await kicad.CreateDocument(DocumentType.DoctypePcb, "/work/new.kicad_pcb"));
        await kicad.CloseDocument(BoardDocument);
        await kicad.CloseAllDocuments(force: true);
        await kicad.OpenLibraryItem(DocumentType.DoctypeFootprint, new LibraryIdentifier { LibraryNickname = "L", EntryName = "E" });

        var requests = peer.Requests;
        var open = requests[0].Message.Unpack<OpenDocument>();
        Assert.Equal((DocumentType.DoctypePcb, "/work/probe.kicad_pcb"), (open.Type, open.Path));
        Assert.Equal("/work/new.kicad_pcb", requests[1].Message.Unpack<CreateDocument>().Path);
        Assert.Equal(BoardDocument, requests[2].Message.Unpack<CloseDocument>().Document);
        Assert.True(requests[3].Message.Unpack<CloseAllDocuments>().Force);
        var item = requests[4].Message.Unpack<OpenLibraryItem>();
        Assert.Equal(DocumentType.DoctypeFootprint, item.Type);
        Assert.Equal("E", item.Identifier.EntryName);
    }

    // ---------------------------------------------------------------------- kicad: libraries

    [Fact]
    public async Task LibraryTable_Commands()
    {
        var ok = new LibraryCommandStatus { Code = LibraryCommandStatus.Types.Code.LcsOk };
        using var peer = NngTestPeer.Start(request =>
            request.Message.Is(GetLibraryTable.Descriptor)
                ? NngTestPeer.Ok(new LibraryTableResponse { Table = new LibraryTable { Path = "/home/me/.config/kicad/11.0/fp-lib-table" } })
                : NngTestPeer.Ok(ok));
        using var client = Client(peer);
        var kicad = new KiCad(client);
        var entry = new LibraryTableEntry { Nickname = "MyLib", Uri = "${KIPRJMOD}/my.pretty", Type = LibraryType.LtFootprint };

        var table = await kicad.GetLibraryTable(LibraryType.LtFootprint, LibraryTableScope.LtsGlobal, substituted: true);
        Assert.Equal(ok, await kicad.AddLibraryTableEntry(entry, LibraryTableScope.LtsProject));
        Assert.Equal(ok, await kicad.UpdateLibraryTableEntry(entry, LibraryTableScope.LtsProject));
        Assert.Equal(ok, await kicad.DeleteLibraryTableEntry(LibraryType.LtFootprint, "MyLib", LibraryTableScope.LtsProject));
        Assert.Equal(ok, await kicad.ImportLibrary("/libs/x.pretty", "X", LibraryType.LtFootprint, description: "mine"));

        Assert.EndsWith("fp-lib-table", table.Table.Path);
        var requests = peer.Requests;
        var get = requests[0].Message.Unpack<GetLibraryTable>();
        Assert.Equal((LibraryType.LtFootprint, LibraryTableScope.LtsGlobal, true), (get.Type, get.Scope, get.Substituted));
        Assert.Equal(entry, requests[1].Message.Unpack<AddLibraryTableEntry>().Entry);
        Assert.Equal(LibraryTableScope.LtsProject, requests[2].Message.Unpack<UpdateLibraryTableEntry>().Scope);
        var delete = requests[3].Message.Unpack<DeleteLibraryTableEntry>();
        Assert.Equal(("MyLib", LibraryType.LtFootprint), (delete.Nickname, delete.Type));

        // ImportLibrary is AddLibraryTableEntry with the row built from its arguments.
        var import = requests[4].Message.Unpack<AddLibraryTableEntry>();
        Assert.Equal(LibraryTableScope.LtsGlobal, import.Scope);
        Assert.Equal("X", import.Entry.Nickname);
        Assert.Equal("/libs/x.pretty", import.Entry.Uri);
        Assert.Equal(LibraryType.LtFootprint, import.Entry.Type);
        Assert.Equal("mine", import.Entry.Description);
    }

    [Fact]
    public async Task LibraryManagement_Commands()
    {
        var ok = new LibraryCommandStatus { Code = LibraryCommandStatus.Types.Code.LcsOk };
        var loaded = new LibraryStatusEntry { Entry = new LibraryTableEntry { Nickname = "Device" }, Status = LibraryLoadStatus.LlsLoaded };
        using var peer = NngTestPeer.Start(request =>
            request.Message.Is(GetLibraryStatuses.Descriptor)
                ? NngTestPeer.Ok(new LibraryStatusResponse { Libraries = { loaded } })
                : NngTestPeer.Ok(ok));
        using var client = Client(peer);
        var kicad = new KiCad(client);

        Assert.Equal([loaded], await kicad.GetLibraryStatuses(LibraryTableScope.LtsProject, default, LibraryType.LtSymbol, LibraryType.LtFootprint));
        Assert.Equal(ok, await kicad.ReloadLibrary(LibraryType.LtSymbol, LibraryTableScope.LtsGlobal, default, "Device", "Power"));
        Assert.Equal(ok, await kicad.LoadAllLibraries(LibraryType.LtFootprint));
        Assert.Equal(ok, await kicad.LoadAllLibraries());

        var requests = peer.Requests;
        var statuses = requests[0].Message.Unpack<GetLibraryStatuses>();
        Assert.Equal(LibraryTableScope.LtsProject, statuses.Scope);
        Assert.Equal([LibraryType.LtSymbol, LibraryType.LtFootprint], statuses.Types_);
        var reload = requests[1].Message.Unpack<ReloadLibrary>();
        Assert.Equal((LibraryType.LtSymbol, LibraryTableScope.LtsGlobal), (reload.Type, reload.Scope));
        Assert.Equal(["Device", "Power"], reload.Nickname);
        Assert.Equal([LibraryType.LtFootprint], requests[2].Message.Unpack<LoadAllLibraries>().Type);
        Assert.Empty(requests[3].Message.Unpack<LoadAllLibraries>().Type);
    }

    [Fact]
    public async Task LibraryQueries_Commands()
    {
        var r = new LibraryIdentifier { LibraryNickname = "Device", EntryName = "R" };
        var c = new LibraryIdentifier { LibraryNickname = "Device", EntryName = "C" };
        using var peer = NngTestPeer.Start(request =>
            request.Message.Is(GetLibraryItems.Descriptor) || request.Message.Is(SearchLibraries.Descriptor)
                ? NngTestPeer.Ok(request.Message.Is(GetLibraryItems.Descriptor)
                    ? new LibraryItemsResponse { Items = { r, c } }
                    : new SearchLibrariesResponse { Items = { r } })
                : NngTestPeer.Ok(new GetItemsResponse { Items = { Any.Pack(new SchematicSymbol()) } }));
        using var client = Client(peer);
        var kicad = new KiCad(client);

        Assert.Equal([r, c], await kicad.GetLibraryItems(LibraryType.LtSymbol, "Device"));
        var items = await kicad.GetItemsFromLibrary(LibraryType.LtSymbol, r);
        var scoped = await kicad.GetItemsFromLibrary(LibraryType.LtSymbol, SchematicDocument, default, r, c);
        Assert.Equal([r], await kicad.SearchLibraries(LibraryType.LtSymbol, "R*"));

        Assert.True(((Any)Assert.Single(items)).Is(SchematicSymbol.Descriptor));
        Assert.Single(scoped);

        var requests = peer.Requests;
        var list = requests[0].Message.Unpack<GetLibraryItems>();
        Assert.Equal(LibraryType.LtSymbol, list.Type);
        Assert.Equal(["Device"], list.Nickname);

        var unscoped = requests[1].Message.Unpack<GetItemsFromLibrary>();
        Assert.Null(unscoped.Document);
        Assert.Equal([r], unscoped.ItemIds);

        var withDocument = requests[2].Message.Unpack<GetItemsFromLibrary>();
        Assert.Equal(SchematicDocument, withDocument.Document);
        Assert.Equal([r, c], withDocument.ItemIds);

        var search = requests[3].Message.Unpack<SearchLibraries>();
        Assert.Equal((LibraryType.LtSymbol, "R*"), (search.Type, search.Query));
    }

    // -------------------------------------------------------------------- kicad: cross-probe

    [Fact]
    public async Task CrossProbe_Commands()
    {
        using var peer = NngTestPeer.Start(request =>
            request.Message.Is(SyncSelection.Descriptor) ? NngTestPeer.Ok(new SyncSelectionResponse { Status = CrossProbeStatus.CpsOk })
            : request.Message.Is(HighlightNets.Descriptor) ? NngTestPeer.Ok(new HighlightNetsResponse { Status = CrossProbeStatus.CpsNotFound, Message = "no such net" })
            : request.Message.Is(FocusOnItem.Descriptor) ? NngTestPeer.Ok(new FocusOnItemResponse { Status = CrossProbeStatus.CpsOk })
            : NngTestPeer.Ok(new CrossProbeAnnounceResponse { Status = CrossProbeStatus.CpsDisabled }));
        using var client = Client(peer);
        var kicad = new KiCad(client);
        var r1 = new SelectionSpec { Footprint = new FootprintSelectionSpec { Reference = "R1" } };
        var pad = new SelectionSpec { Pad = new PadSelectionSpec { Reference = "R1", Number = "2" } };

        var sync = await kicad.SyncSelection(SyncSelectionContext.SscExplicit, SyncSelectionMode.SsmItemsAndNets, r1, r1, pad);
        var highlight = await kicad.HighlightNets("GND", "VCC");
        var focus = await kicad.FocusOnItem(pad);
        var announce = await kicad.CrossProbeAnnounce(FrameType.FtPcbEditor, "/tmp/kicad/api.sock", "token");

        Assert.Equal(CrossProbeStatus.CpsOk, sync.Status);
        Assert.Equal("no such net", highlight.Message);
        Assert.Equal(CrossProbeStatus.CpsOk, focus.Status);
        Assert.Equal(CrossProbeStatus.CpsDisabled, announce.Status);

        var requests = peer.Requests;
        var syncRequest = requests[0].Message.Unpack<SyncSelection>();
        Assert.Equal((SyncSelectionContext.SscExplicit, SyncSelectionMode.SsmItemsAndNets), (syncRequest.Context, syncRequest.Mode));
        Assert.Equal(r1, syncRequest.FocusItem);
        Assert.Equal([r1, pad], syncRequest.Items);
        Assert.Equal(["GND", "VCC"], requests[1].Message.Unpack<HighlightNets>().NetName);
        Assert.Equal(pad, requests[2].Message.Unpack<FocusOnItem>().FocusItem);
        var announceRequest = requests[3].Message.Unpack<CrossProbeAnnounce>();
        Assert.Equal((FrameType.FtPcbEditor, "/tmp/kicad/api.sock", "token"), (announceRequest.FrameType, announceRequest.SocketPath, announceRequest.ApiToken));
    }

    // -------------------------------------------------------------------------------- project

    [Fact]
    public async Task NetClasses_NameTheProject()
    {
        using var peer = NngTestPeer.Start(request =>
            request.Message.Is(GetNetClasses.Descriptor)
                ? NngTestPeer.Ok(new NetClassesResponse { NetClasses = { new NetClass { Name = "Default" } } })
                : NngTestPeer.Ok(new Empty()));
        using var client = Client(peer);
        var project = new Project(client, BoardDocument.Clone());

        var classes = await project.GetNetClasses();
        await project.SetNetClasses(classes, MapMergeMode.MmmReplace);

        Assert.Equal("Default", Assert.Single(classes).Name);
        Assert.Equal(BoardProject, peer.Requests[0].Message.Unpack<GetNetClasses>().Project);
        var set = peer.Requests[1].Message.Unpack<SetNetClasses>();
        Assert.Equal(BoardProject, set.Project);
        Assert.Equal(MapMergeMode.MmmReplace, set.MergeMode);
    }

    [Fact]
    public async Task NetClassAssignments_RoundTrip()
    {
        var reply = new NetClassAssignmentsResponse
        {
            Assignments = { new NetClassAssignment { Net = "/GND", Netclasses = { "Power" } } },
            PatternAssignments = { new NetClassPatternAssignment { Pattern = "/power/*", Netclass = "Power" } },
        };
        using var peer = NngTestPeer.Start(request =>
            request.Message.Is(GetNetClassAssignments.Descriptor) ? NngTestPeer.Ok(reply) : NngTestPeer.Ok(new Empty()));
        using var client = Client(peer);
        var project = new Project(client, BoardDocument.Clone());

        Assert.Equal(reply, await project.GetNetClassAssignments());
        await project.SetNetClassAssignments(reply.Assignments, reply.PatternAssignments, MapMergeMode.MmmReplace);
        await project.SetNetClassAssignments([new NetClassAssignment { Net = "/X" }]);

        Assert.Equal(BoardProject, peer.Requests[0].Message.Unpack<GetNetClassAssignments>().Project);
        var set = peer.Requests[1].Message.Unpack<SetNetClassAssignments>();
        Assert.Equal(BoardProject, set.Project);
        Assert.Equal(MapMergeMode.MmmReplace, set.MergeMode);
        Assert.Equal(reply.Assignments, set.Assignments);
        Assert.Equal(reply.PatternAssignments, set.PatternAssignments);
        var merge = peer.Requests[2].Message.Unpack<SetNetClassAssignments>();
        Assert.Equal(MapMergeMode.MmmMerge, merge.MergeMode);
        Assert.Empty(merge.PatternAssignments);
    }

    [Fact]
    public async Task ExpandTextVariables_CanAlsoExpandEnvironmentVariables()
    {
        using var peer = NngTestPeer.Start(new ExpandTextVariablesResponse { Text = { "/work/probe/x" } });
        using var client = Client(peer);
        var project = new Project(client, BoardDocument.Clone());

        Assert.Equal("/work/probe/x", await project.ExpandTextVariables("${KIPRJMOD}/x", expandEnvironmentVariables: true));
        await project.ExpandTextVariables(["a", "b"]);

        var withEnv = peer.Requests[0].Message.Unpack<ExpandTextVariables>();
        Assert.True(withEnv.ExpandEnvVars);
        var plain = peer.Requests[1].Message.Unpack<ExpandTextVariables>();
        Assert.False(plain.ExpandEnvVars);
        Assert.Equal(["a", "b"], plain.Text);

        // The project proxy sends a bare project document: type PROJECT and the project, no board
        // file name. KiCad's project handler wants exactly that, and pcbnew's handler answers
        // "not open" to a project-typed specifier that still names a board.
        Assert.Equal(DocumentType.DoctypeProject, withEnv.Document.Type);
        Assert.Equal(DocumentSpecifier.IdentifierOneofCase.None, withEnv.Document.IdentifierCase);
        Assert.Equal(BoardProject, withEnv.Document.Project);
    }

    [Fact]
    public void Project_DoesNotChangeTheBoardsOwnSpecifier()
    {
        using var peer = NngTestPeer.Start(new Empty());
        using var client = Client(peer);
        var board = new Board(client, BoardDocument.Clone());

        var project = board.GetProject();

        // It used to flip the shared specifier's type to PROJECT, so every board command after
        // GetProject() went out as a project.
        Assert.Equal(DocumentType.DoctypePcb, board.Document.Type);
        Assert.Equal("probe.kicad_pcb", board.Document.BoardFilename);
        Assert.Equal(DocumentType.DoctypeProject, project.Document.Type);
    }

    // -------------------------------------------------------------------------------- status

    [Fact]
    public async Task AKiCadThatDoesNotKnowACommand_AnswersUnhandled_AndThatIsAnApiException()
    {
        // What a 10.0.x KiCad says to every command on this page. #46 is about giving the
        // exception a status; until then the status is in the message.
        using var peer = NngTestPeer.Start(_ => NngTestPeer.Fail(ApiStatusCode.AsUnhandled));
        using var client = Client(peer);

        var failure = await Assert.ThrowsAsync<ApiException>(async () => await new KiCad(client).GetPaths());

        Assert.Contains("AS_UNHANDLED", failure.Message);
    }

    // ------------------------------------------------------------------------------- version

    [Theory]
    [InlineData(10u, 0u, 6u, false, false, false)]
    [InlineData(10u, 0u, 7u, true, false, false)]
    [InlineData(10u, 99u, 0u, true, true, true)]
    [InlineData(11u, 0u, 0u, true, true, false)]
    public void KiCadVersion_CapabilityFlags(uint major, uint minor, uint patch, bool embedded, bool libraries, bool development)
    {
        var version = new KiCadVersion(major, minor, patch);

        Assert.Equal(embedded, version.SupportsEmbeddedFiles);
        Assert.Equal(embedded, version.SupportsVariants);
        Assert.Equal(libraries, version.SupportsLibraryCommands);
        Assert.Equal(libraries, version.SupportsSchematic);
        Assert.Equal(libraries, version.SupportsJobs);
        Assert.Equal(development, version.IsDevelopmentBuild);
    }

    [Fact]
    public void KiCadVersion_IsAtLeast_ComparesAllThreeNumbers()
    {
        var version = new KiCadVersion(10, 99, 0);

        Assert.True(version.IsAtLeast(10, 99));
        Assert.True(version.IsAtLeast(10, 0, 7));
        Assert.True(version.IsAtLeast(9, 100, 100));
        Assert.False(version.IsAtLeast(10, 99, 1));
        Assert.False(version.IsAtLeast(11, 0));
    }
}
