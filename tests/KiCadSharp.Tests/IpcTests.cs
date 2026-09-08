using Kiapi.Board.Types;
using Kiapi.Common.Types;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KiCadSharp.Tests;

/// <summary>
/// The IPC client, against a real KiCad.
/// </summary>
/// <remarks>
/// <para>
/// KiCad is a GUI application and there is no way to test this without one running, so
/// <c>scripts/kicad-ipc-container.sh</c> starts one headless in a container and bind-mounts the API
/// socket back out. The live tests return early unless <c>KICADSHARP_IPC_SOCKET</c> names it:
/// </para>
/// <code>
/// eval "$(scripts/kicad-ipc-container.sh start some.kicad_pcb)"
/// dotnet test KiCadSharp.slnx -c Release
/// scripts/kicad-ipc-container.sh stop
/// </code>
/// <para>
/// The wiring tests above them need no KiCad at all, and they are the ones that pin the three bugs
/// that stopped this client from ever completing a single request.
/// </para>
/// </remarks>
public class IpcTests
{
    private static string? Socket
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("KICADSHARP_IPC_SOCKET");
            return string.IsNullOrEmpty(configured) ? null : configured;
        }
    }

    private static KiCad Connect()
    {
        Environment.SetEnvironmentVariable("KICAD_API_SOCKET", Socket);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddKiCad("kicad-sharp-tests");
        return services.BuildServiceProvider().GetRequiredService<IKiCadFactory>().Create("kicad-sharp-tests");
    }

    // ----------------------------------------------------------------- wiring; no KiCad required

    [Fact]
    public void AddKiCad_FallsBackToTheDefaultSocketPath_WhenTheEnvironmentDoesNotNameOne()
    {
        Environment.SetEnvironmentVariable("KICAD_API_SOCKET", null);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddKiCad("my-plugin");

        var settings = services.BuildServiceProvider()
            .GetRequiredService<IOptionsFactory<KiCadClientSettings>>()
            .Create("my-plugin");

        // It used to read KICAD_API_SOCKET straight through, so with the variable unset -- which is
        // every case except a plugin KiCad launched itself -- PipeName was null and Connect() threw
        // "Pipename not provided". KiCadEnvironment.GetDefaultSocketPath() existed for this and was
        // never called.
        Assert.Equal(KiCadEnvironment.GetDefaultSocketPath(), settings.PipeName);
        Assert.Equal("ipc:///tmp/kicad/api.sock", OperatingSystem.IsWindows() ? "ipc:///tmp/kicad/api.sock" : settings.PipeName);

        // And the name passed to AddKiCad is the client name, which is what goes in the envelope.
        Assert.Equal("my-plugin", settings.ClientName);
    }

    [Fact]
    public void ApiException_CanBeCaughtByName()
    {
        // It was `internal`, so a consumer in another assembly could not name it. This test only
        // has to compile to prove otherwise.
        var caught = Assert.Throws<ApiException>(void () => throw new ApiException("boom"));
        Assert.Equal("boom", caught.Message);
    }

    // ------------------------------------------------------------------------------ live traffic

    [Fact]
    public async Task Ping_CompletesARoundTrip()
    {
        if (Socket is null)
        {
            return;
        }

        await Connect().Ping();
    }

    [Fact]
    public async Task GetVersion_ReportsTheRunningKiCad()
    {
        if (Socket is null)
        {
            return;
        }

        var version = await Connect().GetVersion();

        Assert.Equal(10u, version.Major);
        Assert.Equal("10.0.6", $"{version.Major}.{version.Minor}.{version.Patch}");
    }

    [Fact]
    public async Task GetOpenDocuments_ListsTheOpenBoard()
    {
        if (Socket is null)
        {
            return;
        }

        var documents = await Connect().GetOpenDocuments(DocumentType.DoctypePcb);

        Assert.Single(documents);
        Assert.EndsWith(".kicad_pcb", documents[0].BoardFilename);
    }

    [Fact]
    public async Task GetBoard_NamesTheOpenBoard()
    {
        if (Socket is null)
        {
            return;
        }

        var board = await Connect().GetBoard();

        Assert.EndsWith(".kicad_pcb", board.Name);
    }

    [Fact]
    public async Task ActiveLayer_RoundTripsThroughKiCad()
    {
        if (Socket is null)
        {
            return;
        }

        var board = await Connect().GetBoard();
        var original = await board.GetActiveLayer();

        try
        {
            await board.SetActiveLayer(BoardLayer.BlBCu);
            Assert.Equal(BoardLayer.BlBCu, await board.GetActiveLayer());
        }
        finally
        {
            await board.SetActiveLayer(original);
        }
    }

    [Fact]
    public async Task GetItems_ReadsTheBoardsFootprints()
    {
        if (Socket is null)
        {
            return;
        }

        var board = await Connect().GetBoard();

        var footprints = await board.GetItems(KiCadObjectType.KotPcbFootprint);
        Assert.NotEmpty(footprints);
    }

    [Fact]
    public async Task GetAsString_ReturnsTheBoardAsAnSExpression()
    {
        if (Socket is null)
        {
            return;
        }

        var board = await Connect().GetBoard();

        var text = await board.GetAsString();
        Assert.StartsWith("(kicad_pcb", text);
    }

    [Fact]
    public async Task ACancelledTokenStopsTheRequest()
    {
        if (Socket is null)
        {
            return;
        }

        var kicad = Connect();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // The object model used to drop the token on the floor, so this was unobservable.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await kicad.Ping(cancellation.Token));
    }
}
