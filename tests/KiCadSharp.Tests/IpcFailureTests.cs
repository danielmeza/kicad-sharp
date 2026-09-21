using System.Diagnostics;
using System.Net.Sockets;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using KiCadSharp.Interop;

using Kiapi.Common;
using Kiapi.Common.Commands;

using Microsoft.Extensions.Logging.Abstractions;

namespace KiCadSharp.Tests;

/// <summary>
/// Every way an IPC call can fail, and what each one throws: a <see cref="KiCadIpcException"/> for
/// all of them, and a cancellation as itself.
/// </summary>
/// <remarks>
/// <para>
/// Against <see cref="NngTestPeer"/>, an in-process nng REP socket, answering the way KiCad 10.0.6's
/// <c>KICAD_API_SERVER</c> does -- the status codes, the error texts and which replies carry a header
/// are read from <c>common/api/api_server.cpp</c> at that tag. No KiCad is needed, and none of these
/// can be produced against a real one on demand: a KiCad does not send a malformed reply or answer
/// <c>AS_BUSY</c> because a test asks it to.
/// </para>
/// <para>
/// Before these, four of the failures below escaped as something no caller could catch by type
/// without catching <see cref="Exception"/>: three <see cref="NullReferenceException"/>s and a raw
/// <see cref="InvalidProtocolBufferException"/>. And <see cref="ApiException"/> carried no status.
/// </para>
/// </remarks>
public class IpcFailureTests
{
    // The text KiCad 10.0.6 sends with AS_UNHANDLED and AS_TOKEN_MISMATCH, from api_server.cpp.
    private const string NoHandlerForPing = "no handler available for request of type kiapi.common.commands.Ping";
    private const string TokenMismatch = "the provided kicad_token did not match this KiCad instance's token";

    private static KiCadIPCClient Client(string? url, TimeSpan? requestTimeout = null, string? token = null) =>
        new(
            new KiCadClientSettings
            {
                PipeName = url,
                Token = token,
                ClientName = "kicad-sharp-tests",
                RequestTimeout = requestTimeout ?? Timeout.InfiniteTimeSpan,
            },
            NullLogger<KiCadIPCClient>.Instance);

    private static string AbsentSocket() =>
        $"ipc://{Path.Combine(Path.GetTempPath(), $"kicadsharp-absent-{Guid.NewGuid():N}.sock")}";

    /// <summary>
    /// A connected client whose KiCad went away after the dial: the peer it dialled has closed.
    /// </summary>
    /// <remarks>
    /// The REQ socket learns that its connection closed on nng's own thread, a moment after the peer's
    /// socket closes. A request sent inside that moment still finds the connection ready. It goes out
    /// on the dying connection, nng queues it to be sent again, and the call then waits for a reply
    /// rather than for somebody to take the request, which is not the case these tests are about.
    /// Measured, sending straight after the peer closed hit that window in 21, then 19, of 300 runs,
    /// and in 12 of 300 on the blocking send before #58. Waiting 20 ms first, it hit it in none of
    /// 300, nor did waiting 100 ms, which is the wait here.
    /// </remarks>
    private static async Task<KiCadIPCClient> AClientWhoseKiCadWentAway(TimeSpan? requestTimeout = null)
    {
        var peer = NngTestPeer.Start(TimeSpan.Zero);
        var client = Client(peer.Url, requestTimeout);
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
    /// Asserts <paramref name="call"/> throws exactly <typeparamref name="T"/>, and that a caller
    /// catching only <see cref="KiCadIpcException"/> would have caught it.
    /// </summary>
    private static async Task<T> FailsWith<T>(Func<Task> call)
        where T : KiCadIpcException
    {
        var failure = await Assert.ThrowsAsync<T>(call);
        Assert.IsAssignableFrom<KiCadIpcException>(failure);
        return failure;
    }

    // ------------------------------------------------------------ connection refused, or no socket

    [Fact]
    public async Task APathWithNothingListeningIsAConnectionFailure()
    {
        using var client = Client(AbsentSocket());

        var failure = await FailsWith<KiCadConnectionException>(async () => await client.Send(new Ping()));

        var nng = Assert.IsType<NngException>(failure.InnerException);
        Assert.Equal(nameof(Nng.nng_dial), nng.Operation);
        Assert.Contains("Connection refused", failure.Message);
    }

    [Fact]
    public async Task NoSocketPathIsAConnectionFailure()
    {
        using var client = Client(url: null);

        await FailsWith<KiCadConnectionException>(async () => await client.Send(new Ping()));
    }

    [Fact]
    public async Task ASocketThatNeverCompletesTheHandshakeIsAConnectionFailure()
    {
        // A KiCad that has bound its socket and is not serving yet. nng gives up on the dial after
        // its own 10 s, which is the cost of this test.
        var path = Path.Combine(Path.GetTempPath(), $"kicadsharp-silent-{Guid.NewGuid():N}.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(8);

        try
        {
            using var client = Client($"ipc://{path}");

            var failure = await FailsWith<KiCadConnectionException>(async () => await client.Send(new Ping()));

            Assert.Contains("Timed out", Assert.IsType<NngException>(failure.InnerException).Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ----------------------------------------------------------------------- a missing or wrong token

    /// <summary>A peer that checks the token the way <c>KICAD_API_SERVER::handleApiEvent</c> does.</summary>
    private static NngTestPeer.Raw PeerWithToken(string token, List<string>? tokensSeen = null) =>
        NngTestPeer.StartRaw(bytes =>
        {
            var sent = ApiRequest.Parser.ParseFrom(bytes).Header.KicadToken;
            if (tokensSeen is not null)
            {
                lock (tokensSeen)
                {
                    tokensSeen.Add(sent);
                }
            }

            return sent.Length != 0 && sent != token
                ? NngTestPeer.Answer(ApiStatusCode.AsTokenMismatch, TokenMismatch, token: token)
                : NngTestPeer.Answer(ApiStatusCode.AsOk, payload: new Empty(), token: token);
        });

    [Fact]
    public async Task AWrongTokenIsRefusedWithTokenMismatch()
    {
        using var peer = PeerWithToken("this-kicad");
        using var client = Client(peer.Url, token: "another-kicad");

        var failure = await FailsWith<ApiException>(async () => await client.Send(new Ping()));

        Assert.Equal(ApiStatusCode.AsTokenMismatch, failure.StatusCode);
        Assert.Equal(TokenMismatch, failure.ErrorMessage);
    }

    [Fact]
    public async Task AMissingTokenIsNotAFailureAndIsAdoptedFromTheFirstReply()
    {
        var tokensSeen = new List<string>();
        using var peer = PeerWithToken("this-kicad", tokensSeen);
        using var client = Client(peer.Url, token: null);

        await client.Send(new Ping());
        await client.Send(new Ping());

        Assert.Equal(new[] { "", "this-kicad" }, tokensSeen);
    }

    // -------------------------------------------------------------------------------------- timeouts

    [Fact]
    public async Task AReplyThatDoesNotComeInTimeIsAConnectionFailureWithATimeoutInside()
    {
        using var peer = NngTestPeer.Start(TimeSpan.FromSeconds(30));
        using var client = Client(peer.Url, TimeSpan.FromMilliseconds(250));

        var failure = await FailsWith<KiCadConnectionException>(async () => await client.Send(new Ping()));

        Assert.IsType<TimeoutException>(failure.InnerException);
    }

    [Fact]
    public async Task ASendWithNoKiCadToTakeItIsAConnectionFailureWithATimeoutInside()
    {
        // With the peer gone after the dial, a REQ socket has nobody to hand the request to, and the
        // send waits for somebody until RequestTimeout runs out. It used to wait in nng_sendmsg, on
        // nng's send-timeout, and nng's NNG_ETIMEDOUT was inside. Since #58 the send is polled, and
        // what is inside is what nng answered the last attempt: NNG_EAGAIN, from nng_sendmsg.
        using var client = await AClientWhoseKiCadWentAway(TimeSpan.FromMilliseconds(500));

        var failure = await FailsWith<KiCadConnectionException>(async () => await client.Send(new Ping()));

        var timeout = Assert.IsType<TimeoutException>(failure.InnerException);
        var nng = Assert.IsType<NngException>(timeout.InnerException);
        Assert.Equal(nameof(Nng.nng_sendmsg), nng.Operation);
        Assert.Equal(Nng.Again, nng.Error);
    }

    // ----------------------------------------------------------------------- the transport mid-call

    [Fact]
    public async Task AKiCadThatGoesAwayMidCallIsNotReportedByNngAndIsBoundedByRequestTimeout()
    {
        // The peer takes the request and closes without answering. Measured, nng does not turn that
        // into an error on the REQ side: the non-blocking receive goes on answering "not yet". What
        // ends the call is the timeout, or the caller's token; with neither, it waits.
        var peer = NngTestPeer.Start(TimeSpan.Zero, answer: false);
        using var client = Client(peer.Url, TimeSpan.FromSeconds(1));

        var call = client.Send(new Ping()).AsTask();
        await WaitUntil(() => peer.RequestsReceived == 1);
        peer.Dispose();

        var failure = await FailsWith<KiCadConnectionException>(() => call);
        Assert.IsType<TimeoutException>(failure.InnerException);
    }

    [Fact]
    public async Task DisconnectingWhileTheSendWaitsForAKiCadIsACancellationNotAFailure()
    {
        // With the default infinite RequestTimeout and no peer, the send waits for one. It used to wait
        // blocked in nng_sendmsg, and closing the socket under it failed the send with NNG_ECLOSED,
        // which used to surface as a KiCadConnectionException. It is the disconnect that ended the
        // request, and a request a disconnect ends is reported as a cancellation wherever it is -- the
        // same as DisconnectingWhileTheRequestIsOnTheWire. Since #58 the send is polled, so the
        // disconnect is usually seen on the token rather than as NNG_ECLOSED, and no inner exception is
        // asserted: either way it is the same cancellation.
        using var client = await AClientWhoseKiCadWentAway();

        var call = Task.Run(async () => await client.Send(new Ping()));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(call.IsCompleted, "the send was expected to be waiting for a peer");

        var elapsed = Stopwatch.StartNew();
        client.Disconnect();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CancellingASendThatHasNoKiCadToTakeItEndsItPromptlyAndHoldsNoThread()
    {
        // #58. With the default infinite RequestTimeout and the peer gone after the dial, a REQ socket
        // has no one to hand the request to. The send used to be one blocking nng_sendmsg that the
        // token did not reach: measured when #58 was filed, it was still blocked 5 s after the cancel,
        // on the thread that called it. It is now attempted without blocking and retried on a timer, the way the reply is
        // polled for, so the caller gets its thread back and the token ends the wait.
        using var client = await AClientWhoseKiCadWentAway();

        // A thread of its own, so that "held" can be observed: it ends when Send hands it back. A send
        // parked in nng would keep it until the socket closes, which is when the client is disposed.
        using var cancellation = new CancellationTokenSource();
        Task? call = null;
        var caller = new Thread(() => call = client.Send(new Ping(), cancellation.Token).AsTask()) { IsBackground = true };
        caller.Start();

        Assert.True(caller.Join(TimeSpan.FromSeconds(5)), "the send kept the thread that called it");
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(call!.IsCompleted, "the send was expected to be waiting for a peer");

        var elapsed = Stopwatch.StartNew();
        await cancellation.CancelAsync();
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);

        Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
    }

    [Fact]
    public async Task ASendToAKiCadThatIsThereIsUnaffected()
    {
        // The other side of #58. Each request goes out exactly once and gets its own reply: the peer
        // numbers its answers, so a request sent twice, or one lost, would put every reply after it
        // out by one.
        var answered = 0;
        using var peer = NngTestPeer.StartRaw(_ => NngTestPeer.Answer(
            ApiStatusCode.AsOk,
            payload: new GetVersionResponse { Version = new() { Major = (uint)Interlocked.Increment(ref answered) } }));
        using var client = Client(peer.Url);
        using var cancellation = new CancellationTokenSource();

        for (var i = 1; i <= 20; i++)
        {
            var reply = await client.Send<GetVersionResponse>(new GetVersion(), cancellation.Token);
            Assert.Equal((uint)i, reply.Version.Major);
        }

        Assert.Equal(20, peer.RequestsReceived);
    }

    // ------------------------------------------------------------------- KiCad answers with an error

    [Theory]
    [InlineData(ApiStatusCode.AsTimeout, "AS_TIMEOUT")]
    [InlineData(ApiStatusCode.AsBadRequest, "AS_BAD_REQUEST")]
    [InlineData(ApiStatusCode.AsNotReady, "AS_NOT_READY")]
    [InlineData(ApiStatusCode.AsUnhandled, "AS_UNHANDLED")]
    [InlineData(ApiStatusCode.AsTokenMismatch, "AS_TOKEN_MISMATCH")]
    [InlineData(ApiStatusCode.AsBusy, "AS_BUSY")]
    [InlineData(ApiStatusCode.AsUnimplemented, "AS_UNIMPLEMENTED")]
    [InlineData(ApiStatusCode.AsUnknown, "AS_UNKNOWN")]
    public async Task EveryStatusButOkIsAnApiFailureThatCarriesIt(ApiStatusCode status, string spelled)
    {
        // AS_NOT_READY is the one reply KiCad sends without a header: KICAD_API_SERVER::onApiRequest
        // answers it before the request is even parsed.
        using var peer = NngTestPeer.StartRaw(_ => NngTestPeer.Answer(
            status, "what KiCad said", token: status == ApiStatusCode.AsNotReady ? null : "this-kicad"));
        using var client = Client(peer.Url);

        var failure = await FailsWith<ApiException>(async () => await client.Send(new Ping()));

        Assert.Equal(status, failure.StatusCode);
        Assert.Equal("what KiCad said", failure.ErrorMessage);
        Assert.Equal($"KiCad answered kiapi.common.commands.Ping with {spelled}: what KiCad said", failure.Message);
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public async Task AnUnsupportedCommandCanBeToldApartByItsStatus()
    {
        // What eeschema on KiCad 10.0.6 answers to Ping (docs/status.md), and the reason StatusCode is
        // public: it is how a caller finds out a command is not there.
        using var peer = NngTestPeer.StartRaw(_ => NngTestPeer.Answer(ApiStatusCode.AsUnhandled, NoHandlerForPing));
        using var client = Client(peer.Url);

        var supported = true;
        try
        {
            await client.Send(new Ping());
        }
        catch (ApiException e) when (e.StatusCode == ApiStatusCode.AsUnhandled)
        {
            supported = false;
        }

        Assert.False(supported);
    }

    [Fact]
    public async Task AStatusThisBuildDoesNotKnowIsStillCarried()
    {
        // A newer KiCad may add a code. It is passed through as the number it is.
        using var peer = NngTestPeer.StartRaw(_ => NngTestPeer.Answer((ApiStatusCode)42));
        using var client = Client(peer.Url);

        var failure = await FailsWith<ApiException>(async () => await client.Send(new Ping()));

        Assert.Equal((ApiStatusCode)42, failure.StatusCode);
        Assert.Equal("KiCad answered kiapi.common.commands.Ping with status 42", failure.Message);
    }

    [Fact]
    public async Task NoBoardOpenIsAnApiFailureWithNoStatus()
    {
        // KiCad answers GetOpenDocuments successfully, with nothing in it. The failure is this
        // library's, not a status KiCad sent, so there is no StatusCode to report.
        using var peer = NngTestPeer.StartRaw(_ => NngTestPeer.Answer(ApiStatusCode.AsOk, payload: new GetOpenDocumentsResponse()));
        using var client = Client(peer.Url);

        var failure = await FailsWith<ApiException>(async () => await new KiCad(client).GetBoard());

        Assert.Null(failure.StatusCode);
    }

    // ------------------------------------------------------------------------------ a malformed reply

    [Fact]
    public async Task BytesThatAreNotAnApiResponseAreAConnectionFailure()
    {
        // A length-delimited field 1 that promises five bytes and has none.
        using var peer = NngTestPeer.StartRaw(_ => [0x0A, 0x05]);
        using var client = Client(peer.Url);

        var failure = await FailsWith<KiCadConnectionException>(async () => await client.Send(new Ping()));

        Assert.IsType<InvalidProtocolBufferException>(failure.InnerException);
    }

    [Fact]
    public async Task AReplyWithNoStatusIsAnApiFailureReadAsUnknown()
    {
        // Zero bytes parse as an ApiResponse with every field absent. This used to be a
        // NullReferenceException.
        using var peer = NngTestPeer.StartRaw(_ => []);
        using var client = Client(peer.Url);

        var failure = await FailsWith<ApiException>(async () => await client.Send(new Ping()));

        Assert.Equal(ApiStatusCode.AsUnknown, failure.StatusCode);
        Assert.Contains("carried no status", failure.Message);
    }

    [Fact]
    public async Task AnOkWithNoPayloadIsAnApiFailure()
    {
        // This used to be a NullReferenceException.
        using var peer = NngTestPeer.StartRaw(_ => NngTestPeer.Answer(ApiStatusCode.AsOk));
        using var client = Client(peer.Url);

        var failure = await FailsWith<ApiException>(async () => await client.Send(new Ping()));

        Assert.Equal(ApiStatusCode.AsOk, failure.StatusCode);
        Assert.Contains("no payload", failure.Message);
    }

    [Fact]
    public async Task AnOkCarryingTheWrongTypeIsAnApiFailure()
    {
        using var peer = NngTestPeer.StartRaw(_ => NngTestPeer.Answer(ApiStatusCode.AsOk, payload: new StringValue { Value = "not a version" }));
        using var client = Client(peer.Url);

        var failure = await FailsWith<ApiException>(async () => await client.Send<GetVersionResponse>(new GetVersion()));

        Assert.Equal(ApiStatusCode.AsOk, failure.StatusCode);
        Assert.Contains("type.googleapis.com/google.protobuf.StringValue", failure.Message);
    }

    [Fact]
    public async Task AnOkWhosePayloadDoesNotParseIsAnApiFailure()
    {
        // The right type URL over bytes that are not that type. This used to escape as protobuf's
        // own InvalidProtocolBufferException.
        using var peer = NngTestPeer.StartRaw(_ => new ApiResponse
        {
            Header = new ApiResponseHeader { KicadToken = "this-kicad" },
            Status = new ApiResponseStatus { Status = ApiStatusCode.AsOk },
            Message = new Any
            {
                TypeUrl = Any.Pack(new GetVersionResponse()).TypeUrl,
                Value = ByteString.CopyFrom(0x0A, 0x05),
            },
        }.ToByteArray());
        using var client = Client(peer.Url);

        var failure = await FailsWith<ApiException>(async () => await client.Send<GetVersionResponse>(new GetVersion()));

        Assert.Equal(ApiStatusCode.AsOk, failure.StatusCode);
        Assert.IsType<InvalidProtocolBufferException>(failure.InnerException);
    }

    [Fact]
    public async Task AnOkWithNoHeaderStillSucceedsAndAdoptsNoToken()
    {
        // This used to be a NullReferenceException, thrown after the request had succeeded, while
        // adopting the token from a header that was not there.
        var tokensSeen = new List<string>();
        using var peer = NngTestPeer.StartRaw(bytes =>
        {
            lock (tokensSeen)
            {
                tokensSeen.Add(ApiRequest.Parser.ParseFrom(bytes).Header.KicadToken);
            }

            return NngTestPeer.Answer(ApiStatusCode.AsOk, payload: new Empty(), token: null);
        });
        using var client = Client(peer.Url, token: null);

        await client.Send(new Ping());
        await client.Send(new Ping());

        Assert.Equal(new[] { "", "" }, tokensSeen);
    }

    // ---------------------------------------------------------------------- cancellation stays itself

    [Fact]
    public async Task AnAlreadyCancelledTokenIsACancellationCarryingThatToken()
    {
        using var peer = NngTestPeer.Start(TimeSpan.Zero);
        using var client = Client(peer.Url);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.Send(new Ping(), cancellation.Token));

        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Assert.Equal(0, peer.RequestsReceived);
    }

    [Fact]
    public async Task CancellingWhileTheReplyIsAwaitedIsACancellationNotAFailure()
    {
        using var peer = NngTestPeer.Start(TimeSpan.FromSeconds(30));
        using var client = Client(peer.Url);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.Send(new Ping(), cancellation.Token));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
    }

    [Fact]
    public async Task ACallQueuedBehindAnotherIsCancelledWithItsOwnToken()
    {
        // REQ carries one request at a time, so a second call on the same client waits for the first
        // to finish. Cancelling it while it waits there is the same cancellation as anywhere else in
        // the round trip, and leaves the first call alone.
        using var peer = NngTestPeer.Start(TimeSpan.FromSeconds(30));
        using var client = Client(peer.Url);
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();

        var firstCall = client.Send(new Ping(), first.Token).AsTask();
        await WaitUntil(() => peer.RequestsReceived == 1);
        var secondCall = client.Send(new Ping(), second.Token).AsTask();

        await second.CancelAsync();
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondCall);
        Assert.Equal(second.Token, cancelled.CancellationToken);
        Assert.False(firstCall.IsCompleted, "cancelling the second call ended the first");

        await first.CancelAsync();
        cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstCall);
        Assert.Equal(first.Token, cancelled.CancellationToken);
        Assert.Equal(1, peer.RequestsReceived);
    }

    // ------------------------------------------------------------------------------- the client itself

    [Fact]
    public async Task ASendAfterDisconnectConnectsAgain()
    {
        // Disconnect() cancels the connection's token and the next Send used to link to it before
        // reconnecting, so it failed with an OperationCanceledException nobody had asked for.
        using var peer = NngTestPeer.Start(TimeSpan.Zero);
        using var client = Client(peer.Url);

        await client.Send(new Ping());
        client.Disconnect();
        await client.Send(new Ping());

        Assert.True(client.IsConnected);
        Assert.Equal(2, peer.RequestsReceived);
    }

    [Fact]
    public async Task ASendAfterDisposeIsAUsageErrorNotAnIpcFailure()
    {
        using var peer = NngTestPeer.Start(TimeSpan.Zero);
        var client = Client(peer.Url);
        await client.Send(new Ping());
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.Send(new Ping()));
        Assert.Equal(1, peer.RequestsReceived);
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
