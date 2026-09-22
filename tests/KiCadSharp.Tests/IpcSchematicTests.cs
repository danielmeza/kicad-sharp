using Google.Protobuf.WellKnownTypes;

using Kiapi.Common.Commands;
using Kiapi.Common.Types;
using Kiapi.Schematic.Jobs;
using Kiapi.Schematic.Types;

namespace KiCadSharp.Tests;

/// <summary>
/// The schematic API, against a real eeschema. KiCad master only: on 10.0.x eeschema answers
/// nothing (docs/ipc.md).
/// </summary>
/// <remarks>
/// <code>
/// export KICADSHARP_KICAD_FLAVOR=nightly
/// eval "$(scripts/kicad-ipc-container.sh start tests/KiCadSharp.Tests/data/duplicate-refs/duplicate-refs.kicad_sch)"
/// dotnet test KiCadSharp.slnx -c Release
/// </code>
/// The fixture is a two-sheet hierarchy (a root and <c>power-input.kicad_sch</c>), so the
/// hierarchy and the netlist both have something to return.
/// </remarks>
public class IpcSchematicTests
{
    private static Task<KiCad?> Schematic() => LiveKiCad.Schematic(version => version.SupportsSchematic);

    [Fact]
    public async Task Eeschema_AnswersPingAndVersion()
    {
        if (await Schematic() is not { } kicad)
        {
            return;
        }

        await kicad.Ping();
        Assert.True((await kicad.GetVersion()).SupportsSchematic);
    }

    [Fact]
    public async Task GetSchematic_BindsTheOpenSheet()
    {
        if (await Schematic() is not { } kicad)
        {
            return;
        }

        var schematic = await kicad.GetSchematic();

        Assert.Equal(DocumentType.DoctypeSchematic, schematic.Document.Type);
        Assert.NotEmpty(schematic.Document.Project.Name);
    }

    [Fact]
    public async Task GetHierarchy_ListsTheSheets()
    {
        if (await Schematic() is not { } kicad)
        {
            return;
        }

        var sheets = await (await kicad.GetSchematic()).GetHierarchy();

        Assert.NotEmpty(sheets);
    }

    [Fact]
    public async Task GetNetlist_ListsTheNets()
    {
        if (await Schematic() is not { } kicad)
        {
            return;
        }

        var nets = await (await kicad.GetSchematic()).GetNetlist();

        Assert.NotEmpty(nets);
    }

    [Fact]
    public async Task GetItems_ReadsTheSymbols()
    {
        if (await Schematic() is not { } kicad)
        {
            return;
        }

        // The fixture's root sheet holds only a sheet symbol; the parts are on the sub-sheet. A
        // schematic document is addressed by sheet path, so walk the hierarchy and read each sheet
        // through its own specifier.
        var root = await kicad.GetSchematic();
        var symbols = new List<Any>();
        foreach (var sheet in Flatten(await root.GetHierarchy()))
        {
            var document = new DocumentSpecifier { Type = DocumentType.DoctypeSchematic, SheetPath = sheet.Path, Project = root.Document.Project };
            symbols.AddRange((await kicad.GetSchematic(document).GetItems(KiCadObjectType.KotSchSymbol)).Cast<Any>());
        }

        // A symbol on a sheet is a SchematicSymbolInstance; SchematicSymbol is the library definition.
        Assert.NotEmpty(symbols);
        Assert.All(symbols, item => Assert.True(item.Is(SchematicSymbolInstance.Descriptor), item.TypeUrl));
    }

    private static IEnumerable<SheetInstance> Flatten(IEnumerable<SheetInstance> sheets)
    {
        foreach (var sheet in sheets)
        {
            yield return sheet;
            foreach (var child in Flatten(sheet.Children))
            {
                yield return child;
            }
        }
    }

    [Fact]
    public async Task GetAsString_ReturnsTheSheetAsAnSExpression()
    {
        if (await Schematic() is not { } kicad)
        {
            return;
        }

        var text = await (await kicad.GetSchematic()).GetAsString();

        Assert.StartsWith("(kicad_sch", text);
    }

    [Fact]
    public async Task Commit_BeginsAndDrops()
    {
        if (await Schematic() is not { } kicad)
        {
            return;
        }

        var schematic = await kicad.GetSchematic();

        var commit = await schematic.BeginCommit();
        Assert.NotEmpty(commit.Id.Value);
        await schematic.DropCommit(commit);
    }

    [Fact]
    public async Task ModifiedState_AndPageSettings()
    {
        if (await Schematic() is not { } kicad)
        {
            return;
        }

        var schematic = await kicad.GetSchematic();

        Assert.NotEqual(DocumentModifiedState.DmsUnknown, await schematic.GetModifiedState());
        Assert.NotEqual(PageSize.PsUnknown, (await schematic.GetPageSettings()).PageSize);
    }

    [Fact]
    public async Task Variants_RoundTripThroughEeschema()
    {
        if (await Schematic() is not { } kicad)
        {
            return;
        }

        var schematic = await kicad.GetSchematic();
        var name = "kicad-sharp-" + Guid.NewGuid().ToString("N")[..8];

        await schematic.AddVariant(name);
        try
        {
            Assert.Contains(await schematic.GetVariants(), variant => variant.Name == name);
            await schematic.SetCurrentVariant(name);
            Assert.Equal(name, await schematic.GetCurrentVariant());
            await schematic.SetCurrentVariant(null);
        }
        finally
        {
            await schematic.DeleteVariant(name);
        }

        Assert.DoesNotContain(await schematic.GetVariants(), variant => variant.Name == name);
    }

    [Fact]
    public async Task RunJob_ExportsAnSvg()
    {
        if (await Schematic() is not { } kicad)
        {
            return;
        }

        var schematic = await kicad.GetSchematic();

        var response = await schematic.RunJob(new RunSchematicJobExportSvg { PlotSettings = new SchematicPlotSettings { PlotAll = true } }, "/project/svg");

        Assert.True(response.Status is JobStatus.JsSuccess or JobStatus.JsWarning, response.Message);

        if (LiveKiCad.SchematicProject is { } project)
        {
            Assert.NotEmpty(Directory.GetFiles(project, "*.svg", SearchOption.AllDirectories));
        }
    }
}
