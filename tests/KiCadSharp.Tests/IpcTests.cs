using Google.Protobuf.WellKnownTypes;

using Kiapi.Board.Types;
using Kiapi.Common;
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

    private static KiCad Connect(Action<KiCadClientSettings>? configure = null)
    {
        Environment.SetEnvironmentVariable("KICAD_API_SOCKET", Socket);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddKiCad("kicad-sharp-tests", settings => configure?.Invoke(settings));
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

        // Which directory depends on TMPDIR and the platform, as it does for KiCad (#97);
        // DefaultSocketPathTests pins that. This used to assert /tmp, which a TMPDIR broke.
        Assert.EndsWith(OperatingSystem.IsWindows() ? @"\kicad\api.sock" : "/kicad/api.sock", settings.PipeName);

        // And the name passed to AddKiCad is the client name, which is what goes in the envelope.
        Assert.Equal("my-plugin", settings.ClientName);
    }

    [Fact]
    public async Task AddKiCad_SendsItsClientName_InEveryRequestHeader()
    {
        // Against the in-process peer rather than a KiCad, so it runs everywhere, and reading the
        // header off the wire rather than off the settings: what KiCad sees is what counts.
        var namesSeen = new List<string>();
        using var peer = NngTestPeer.StartRaw(bytes =>
        {
            lock (namesSeen)
            {
                namesSeen.Add(ApiRequest.Parser.ParseFrom(bytes).Header.ClientName);
            }

            return NngTestPeer.Answer(ApiStatusCode.AsOk, payload: new Empty());
        });

        // Two named clients and the default one, all dialling the same socket. The header used to be
        // filled from PipeName, which all three share, so only the names can tell them apart.
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddKiCad("my-plugin", settings => settings.PipeName = peer.Url);
        services.AddKiCad("another-plugin", settings => settings.PipeName = peer.Url);
        services.AddKiCad(settings => settings.PipeName = peer.Url);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IKiCadFactory>();

        await factory.Create("my-plugin").Ping();
        await factory.Create("another-plugin").Ping();
        await factory.Create().Ping();
        await factory.Create("my-plugin").Ping();

        Assert.Equal(
            new[] { "my-plugin", "another-plugin", KiCadClientSettings.DefaultClientName, "my-plugin" },
            namesSeen);
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
    public async Task AShortRequestTimeoutGivesUpAndTheNextRequestStillWorks()
    {
        if (Socket is null)
        {
            return;
        }

        // KiCad answers a Ping in well under a millisecond, so a zero budget is the only way to
        // make a real one miss a deadline. What matters is the other side of it: the abandoned
        // request is still outstanding on the socket, its reply arrives anyway, and the next
        // request has to get its own answer rather than that one.
        var impatient = Connect(settings => settings.RequestTimeout = TimeSpan.Zero);
        await Assert.ThrowsAsync<KiCadConnectionException>(async () => await impatient.Ping());

        var version = await Connect().GetVersion();
        Assert.Equal("10.0.6", $"{version.Major}.{version.Minor}.{version.Patch}");
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
