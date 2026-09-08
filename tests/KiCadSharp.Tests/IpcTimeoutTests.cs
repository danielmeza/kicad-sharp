using System.Diagnostics;

using Kiapi.Common.Commands;

using Microsoft.Extensions.Logging.Abstractions;

namespace KiCadSharp.Tests;

/// <summary>
/// What the IPC client does while a reply is late, against an nng peer that is late on purpose.
/// </summary>
/// <remarks>
/// A real KiCad answers in under a millisecond, so none of this is observable against one. These
/// are the tests for the half of the transport that only shows up when something goes wrong.
/// </remarks>
public class IpcTimeoutTests
{
    private static KiCadIPCClient Client(string url, TimeSpan? requestTimeout = null) =>
        new(
            new KiCadClientSettings
            {
                PipeName = url,
                ClientName = "kicad-sharp-tests",
                RequestTimeout = requestTimeout ?? Timeout.InfiniteTimeSpan,
            },
            NullLogger<KiCadIPCClient>.Instance);

    [Fact]
    public async Task ARequestCompletesAgainstAPeerThatAnswers()
    {
        using var peer = NngTestPeer.Start(TimeSpan.Zero);
        using var client = Client(peer.Url);

        await client.Send(new Ping());

        Assert.True(client.IsConnected);
        Assert.Equal(1, peer.RequestsReceived);
    }

    [Fact]
    public async Task RequestTimeoutBoundsTheWaitForAReply()
    {
        // The default is Timeout.InfiniteTimeSpan, which is what this client has always done. A
        // caller that wants a bound now has one.
        using var peer = NngTestPeer.Start(TimeSpan.FromSeconds(30));
        using var client = Client(peer.Url, TimeSpan.FromMilliseconds(250));

        var elapsed = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<KiCadConnectionException>(async () => await client.Send(new Ping()));

        Assert.Contains("did not reply", failure.Message);
        Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancellingWhileTheRequestIsOnTheWireReturnsAtOnce()
    {
        // This is the behaviour that changed. The client used to build a linked token and never
        // look at it again, with a comment saying the nng round trip "cannot be interrupted once
        // the request is on the wire". It can now: the reply is polled for, not blocked on.
        using var peer = NngTestPeer.Start(TimeSpan.FromSeconds(30));
        using var client = Client(peer.Url);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var elapsed = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.Send(new Ping(), cancellation.Token));

        Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DisconnectingWhileTheRequestIsOnTheWireReturnsAtOnce()
    {
        using var peer = NngTestPeer.Start(TimeSpan.FromSeconds(30));
        using var client = Client(peer.Url);

        await client.Connect();
        var elapsed = Stopwatch.StartNew();

        var request = Task.Run(async () => await client.Send(new Ping()));
        await Task.Delay(250);
        client.Disconnect();

        // Specifically a cancellation, not "something went wrong": Disconnect cancels the client's
        // own token before it closes the socket, and the poll loop reads that token before each
        // receive, so the in-flight request ends the same way an explicit cancel would.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
        Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AClientRecoversFromARequestItGaveUpOn()
    {
        // Giving up leaves a request outstanding and its reply still arrives. The next request must
        // get its own answer, not the abandoned one's -- otherwise every reply after a single
        // timeout is off by one.
        using var peer = NngTestPeer.Start(TimeSpan.FromMilliseconds(700));
        using var client = Client(peer.Url, TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<KiCadConnectionException>(async () => await client.Send(new Ping()));

        await Task.Delay(TimeSpan.FromSeconds(1.5));   // the abandoned reply lands during this

        using var recovered = Client(peer.Url, TimeSpan.FromSeconds(10));
        await recovered.Send(new Ping());

        Assert.Equal(2, peer.RequestsReceived);
    }

    [Fact]
    public async Task ConnectingToAPathThatIsNotThereThrowsAConnectionException()
    {
        using var client = Client($"ipc://{Path.Combine(Path.GetTempPath(), $"kicadsharp-absent-{Guid.NewGuid():N}.sock")}");

        var failure = await Assert.ThrowsAsync<KiCadConnectionException>(async () => await client.Connect());

        Assert.Contains("Failed to connect to KiCad", failure.Message);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task ConnectingWithNoPipeNameThrowsAConnectionException()
    {
        using var client = Client(url: null!);

        await Assert.ThrowsAsync<KiCadConnectionException>(async () => await client.Connect());
    }
}
