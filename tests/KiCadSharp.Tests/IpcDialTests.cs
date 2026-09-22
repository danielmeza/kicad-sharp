using System.Diagnostics;

using KiCadSharp.Interop;

using Kiapi.Common.Commands;

using Microsoft.Extensions.Logging.Abstractions;

namespace KiCadSharp.Tests;

/// <summary>
/// Connecting, while nng's dial waits (#105): the caller gets its thread back, the caller's token ends
/// the dial, and a dial nng refuses still fails at once.
/// </summary>
/// <remarks>
/// <para>
/// Against <see cref="HeldHandshake"/>, a unix socket that takes nng's connection and never answers
/// its handshake: a KiCad that has opened its API socket and is not serving yet. nng's dial waits on
/// it for 10 s. Until #105 so did the thread that called <c>Connect</c> or the first <c>Send</c>,
/// whatever its token said: measured on master <c>9d46852</c> with a token cancelled after 200 ms,
/// <c>Send</c> returned after 10,006 ms with a <c>KiCadConnectionException</c> and its task already
/// complete, and <c>Connect</c> threw at the call site after 10,008 ms.
/// </para>
/// <para>
/// What ends a dial is closing the socket under it. Measured on nng 1.3.2 and 1.4.0, the two this
/// package ships: <c>nng_close</c> from another thread returned in under 0.1 ms, and the
/// <c>nng_dial</c> blocked on the other thread returned <c>NNG_ECLOSED</c> at the same moment, against
/// a listener that never accepts and against one that accepted and holds the handshake alike. A dial
/// against a path with nothing at it went on failing in 0.3 ms with <c>NNG_ECONNREFUSED</c>.
/// </para>
/// </remarks>
public class IpcDialTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    // "At once" and "promptly", asserted an order of magnitude above what they take.
    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(2);

    private static KiCadIPCClient Client(string url) =>
        new(
            new KiCadClientSettings
            {
                PipeName = url,
                ClientName = "kicad-sharp-tests",
                RequestTimeout = Timeout.InfiniteTimeSpan,
            },
            NullLogger<KiCadIPCClient>.Instance);

    [UnixSocketFact]
    public async Task ConnectReturnsWhileTheDialWaits()
    {
        // Before #105, Connect blocked here for nng's 10 s and then threw, and AcceptDial below was
        // never reached in time.
        using var kicad = HeldHandshake.Start();
        using var client = Client(kicad.Url);

        var connect = client.Connect().AsTask();

        using var connection = await kicad.AcceptDial();
        Assert.False(connect.IsCompleted, "Connect was expected to be waiting for the handshake");
        Assert.False(client.IsConnected);

        await HeldHandshake.CompleteHandshake(connection);
        await connect.WaitAsync(Bound);
        Assert.True(client.IsConnected);
    }

    [UnixSocketFact]
    public async Task AFirstSendHoldsNoThreadWhileTheDialWaits()
    {
        // Observed the way CancellingASendThatHasNoKiCadToTakeItEndsItPromptlyAndHoldsNoThread
        // observes the send: on a thread of its own, which ends when Send hands it back. A UI thread
        // that awaits kicad.Ping() while KiCad starts is this thread.
        using var kicad = HeldHandshake.Start();
        using var client = Client(kicad.Url);
        using var cancellation = new CancellationTokenSource();

        Task? call = null;
        var caller = new Thread(() => call = client.Send(new Ping(), cancellation.Token).AsTask()) { IsBackground = true };
        caller.Start();

        using var connection = await kicad.AcceptDial();
        Assert.True(caller.Join(Bound), "the first Send kept the thread that called it while the dial waited");
        Assert.False(call!.IsCompleted, "the call was expected to be waiting for the handshake");

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(Bound));
    }

    [UnixSocketFact]
    public async Task CancellingWhileTheDialWaitsEndsConnectWithTheCallersToken()
    {
        using var kicad = HeldHandshake.Start();
        using var client = Client(kicad.Url);
        using var cancellation = new CancellationTokenSource();

        var connect = client.Connect(cancellation.Token).AsTask();
        using var connection = await kicad.AcceptDial();

        var elapsed = Stopwatch.StartNew();
        await cancellation.CancelAsync();
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect.WaitAsync(Bound));

        Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, Promptly);
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Assert.False(client.IsConnected);

        // The socket the dial opened is closed, and nothing was sent on it.
        Assert.Equal(0, await HeldHandshake.ReadUntilClosed(connection));
    }

    [UnixSocketFact]
    public async Task CancellingAFirstSendWhileItDialsEndsItWithTheCallersToken()
    {
        // The same through Send, which is how every KiCad method connects: the first call of any of
        // them dials, and a deadline on its token now bounds that dial too.
        using var kicad = HeldHandshake.Start();
        using var client = Client(kicad.Url);
        using var cancellation = new CancellationTokenSource();

        var call = client.Send(new Ping(), cancellation.Token).AsTask();
        using var connection = await kicad.AcceptDial();

        var elapsed = Stopwatch.StartNew();
        await cancellation.CancelAsync();
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(Bound));

        Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, Promptly);
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Assert.False(client.IsConnected);
        Assert.Equal(0, await HeldHandshake.ReadUntilClosed(connection));
    }

    [UnixSocketFact]
    public async Task ADialThePeerRefusesIsAConnectionFailureNotACancellation()
    {
        // The dial runs on a thread of its own now, and what nng says there still reaches the caller
        // as the connection failure it always was: here NNG_EPROTO, for a header that is not nng's.
        using var kicad = HeldHandshake.Start();
        using var client = Client(kicad.Url);

        var connect = client.Connect().AsTask();
        using var connection = await kicad.AcceptDial();
        await HeldHandshake.RefuseHandshake(connection);

        var failure = await Assert.ThrowsAsync<KiCadConnectionException>(() => connect.WaitAsync(Bound));

        var nng = Assert.IsType<NngException>(failure.InnerException);
        Assert.Equal(nameof(Nng.nng_dial), nng.Operation);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task ConnectingToAPathWithNothingAtItStillFailsAtOnce()
    {
        // The row of docs/api.md's failure table that a non-blocking nng dial would have changed: nng
        // refuses a path with nothing at it at once, and the caller still hears so at once, with no
        // retry behind it.
        using var client = Client(SocketPaths.NewUrl("absent"));

        var elapsed = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<KiCadConnectionException>(async () => await client.Connect());

        Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, Promptly);
        Assert.Contains("Connection refused", failure.Message);
        Assert.False(client.IsConnected);
    }
}
