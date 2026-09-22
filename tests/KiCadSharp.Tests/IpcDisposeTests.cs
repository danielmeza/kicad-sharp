using System.Diagnostics;

using KiCadSharp.Interop;

using Kiapi.Common.Commands;

using Microsoft.Extensions.Logging.Abstractions;

namespace KiCadSharp.Tests;

/// <summary>
/// Disposing the client while calls are under way (#67). Every such call ends with an
/// <see cref="OperationCanceledException"/>, wherever it is in the round trip, and a call made
/// afterwards throws <see cref="ObjectDisposedException"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Dispose()</c> used to dispose the <see cref="SemaphoreSlim"/> that keeps calls in turn while
/// calls were still using it. A call it cut off then threw <see cref="ObjectDisposedException"/> from
/// <c>SemaphoreSlim.Release</c> whenever its continuation ran after that, and a call queued behind
/// another could be left waiting for ever, because disposing a <see cref="SemaphoreSlim"/> drops its
/// waiters without completing them.
/// </para>
/// <para>
/// A test cannot choose when a continuation runs. These call <c>Send</c> from the test itself, so their
/// continuations go through xUnit's synchronization context and in practice run after
/// <c>Dispose()</c> has returned. Run 11 times against the client before #67, every test here except
/// the last failed every time, most of them with that <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
public class IpcDisposeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private static KiCadIPCClient Client(string url) =>
        new(
            new KiCadClientSettings
            {
                PipeName = url,
                ClientName = "kicad-sharp-tests",
                RequestTimeout = Timeout.InfiniteTimeSpan,
            },
            NullLogger<KiCadIPCClient>.Instance);

    /// <summary>
    /// A connected client whose KiCad went away after the dial, built as
    /// <c>IpcFailureTests.AClientWhoseKiCadWentAway</c> builds it. That helper's remarks explain the
    /// 100 ms wait.
    /// </summary>
    private static async Task<KiCadIPCClient> AClientWhoseKiCadWentAway()
    {
        var peer = NngTestPeer.Start(TimeSpan.Zero);
        var client = Client(peer.Url);
        try
        {
            await client.Connect();
        }
        finally
        {
            peer.Dispose();
        }

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        return client;
    }

    /// <summary>
    /// Asserts that <paramref name="call"/> ends with exactly an <see cref="OperationCanceledException"/>
    /// that <c>Dispose()</c> caused, within <see cref="Bound"/>.
    /// </summary>
    private static async Task<OperationCanceledException> EndedByDispose(Task call)
    {
        // WaitAsync bounds it. A call that is still running fails the assertion with a
        // TimeoutException, and that failure names the type.
        var cancelled = await Assert.ThrowsAsync<OperationCanceledException>(() => call.WaitAsync(Bound));

        Assert.Contains("disposed", cancelled.Message);
        Assert.True(cancelled.CancellationToken.IsCancellationRequested);
        return cancelled;
    }

    /// <summary>
    /// Disposes <paramref name="client"/> and asserts that <c>Dispose()</c> itself returned promptly.
    /// </summary>
    private static void DisposePromptly(KiCadIPCClient client)
    {
        var elapsed = Stopwatch.StartNew();
        client.Dispose();
        Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task DisposingWhileTheSendWaitsForAKiCadCancelsTheCall()
    {
        using var client = await AClientWhoseKiCadWentAway();

        // Called here rather than through Task.Run: Send returns at its first await, and with nobody
        // to take the request that await is in the send's poll. So by the time it returns, the call is
        // waiting to send.
        using var callerToken = new CancellationTokenSource();
        var call = client.Send(new Ping(), callerToken.Token).AsTask();
        Assert.False(call.IsCompleted, "the send was expected to be waiting for a peer");

        DisposePromptly(client);

        var cancelled = await EndedByDispose(call);
        Assert.NotEqual(callerToken.Token, cancelled.CancellationToken);
    }

    [Fact]
    public async Task DisposingWhileTheReplyIsAwaitedCancelsTheCall()
    {
        using var peer = NngTestPeer.Start(TimeSpan.FromSeconds(30));
        using var client = Client(peer.Url);

        var call = client.Send(new Ping()).AsTask();
        await WaitUntil(() => peer.RequestsReceived == 1);
        Assert.False(call.IsCompleted, "the call was expected to be waiting for its reply");

        DisposePromptly(client);

        await EndedByDispose(call);
    }

    [Fact]
    public async Task DisposingCancelsACallQueuedBehindAnotherAndTheOneAhead()
    {
        using var peer = NngTestPeer.Start(TimeSpan.FromSeconds(30));
        using var client = Client(peer.Url);

        var ahead = client.Send(new Ping()).AsTask();
        await WaitUntil(() => peer.RequestsReceived == 1);

        // Send returns at its first await, which for this call is the wait behind the first.
        var queued = client.Send(new Ping()).AsTask();
        Assert.False(queued.IsCompleted, "the second call was expected to wait behind the first");

        DisposePromptly(client);

        await EndedByDispose(ahead);
        await EndedByDispose(queued);
        Assert.Equal(1, peer.RequestsReceived);
    }

    [Fact]
    public async Task DisposingWhileTheCallConnectsCancelsItAndClosesTheSocketTheDialOpens()
    {
        // The dial cannot be interrupted, so Dispose cannot end this call at once. It must end when
        // the dial returns, without the call going on as if the client were still there, and without
        // leaving the socket the dial opened behind. Before #67 the call did go on: it made that socket
        // the disposed client's connection, so IsConnected read true again, and then failed with
        // ObjectDisposedException from the disposed semaphore. The socket was still open 5 s later,
        // with nothing left that would close it.
        using var kicad = HeldHandshake.Start();
        using var client = Client(kicad.Url);

        Task? call = null;
        var caller = new Thread(() => call = client.Send(new Ping()).AsTask()) { IsBackground = true };
        caller.Start();

        using var connection = await kicad.AcceptDial();
        Assert.True(caller.IsAlive, "the dial was expected to be waiting for the handshake");

        DisposePromptly(client);
        Assert.True(caller.IsAlive, "Dispose was not expected to end a dial that nng is still running");

        await HeldHandshake.CompleteHandshake(connection);
        Assert.True(caller.Join(Bound), "the dial did not return once the handshake completed");

        var cancelled = await EndedByDispose(call!);
        Assert.Null(cancelled.InnerException);

        // The client closed the socket, and sent nothing on it.
        Assert.Equal(0, await HeldHandshake.ReadUntilClosed(connection));
    }

    [Fact]
    public async Task ADialThatFailsAfterDisposeIsStillACancellation()
    {
        // The same call, but the dial fails once the client is disposed. The call was under way when
        // Dispose ran, so it ends the way every such call does, and not with a connection failure. The
        // failure itself is kept inside.
        using var kicad = HeldHandshake.Start();
        using var client = Client(kicad.Url);

        Task? call = null;
        var caller = new Thread(() => call = client.Send(new Ping()).AsTask()) { IsBackground = true };
        caller.Start();

        using var connection = await kicad.AcceptDial();
        DisposePromptly(client);

        await HeldHandshake.RefuseHandshake(connection);
        Assert.True(caller.Join(Bound), "the dial did not return once the handshake was refused");

        var cancelled = await EndedByDispose(call!);
        var nng = Assert.IsType<NngException>(cancelled.InnerException);
        Assert.Equal(nameof(Nng.nng_dial), nng.Operation);
    }

    [Fact]
    public async Task DisposeIsSafeTwiceAndAlongsideDisconnectAndItself()
    {
        using var peer = NngTestPeer.Start(TimeSpan.FromSeconds(30));
        var client = Client(peer.Url);

        var calls = new List<Task> { client.Send(new Ping()).AsTask() };
        await WaitUntil(() => peer.RequestsReceived == 1);
        for (var i = 0; i < 3; i++)
        {
            calls.Add(client.Send(new Ping()).AsTask());
        }

        Assert.DoesNotContain(calls, call => call.IsCompleted);

        // Three disposes and a disconnect, released at the same moment.
        Action[] closers = [client.Dispose, client.Dispose, client.Disconnect, client.Dispose];
        using var start = new Barrier(closers.Length);
        var failures = new List<Exception>();
        var threads = closers
            .Select(close => new Thread(() =>
            {
                try
                {
                    start.SignalAndWait();
                    close();
                }
                catch (Exception exception)
                {
                    lock (failures)
                    {
                        failures.Add(exception);
                    }
                }
            }) { IsBackground = true })
            .ToList();

        threads.ForEach(thread => thread.Start());
        Assert.All(threads, thread => Assert.True(thread.Join(Bound), "a Dispose or Disconnect did not return"));
        Assert.Empty(failures);

        // Each call ends with exactly the one exception. Whether its message says disposed or
        // disconnected depends on which of the four took the connection, so it is not asserted.
        foreach (var call in calls)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => call.WaitAsync(Bound));
        }

        client.Dispose();
        client.Disconnect();
        Assert.False(client.IsConnected);
        Assert.Equal(1, peer.RequestsReceived);
    }

    [Fact]
    public async Task ACallMadeAfterDisposeIsAUsageError()
    {
        using var peer = NngTestPeer.Start(TimeSpan.Zero);
        var client = Client(peer.Url);
        await client.Connect();
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.Send(new Ping()));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.Connect());
        Assert.Equal(0, peer.RequestsReceived);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), "the peer never received the request");
            await Task.Delay(10);
        }
    }
}
