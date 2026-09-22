using System.Diagnostics;
using System.Reflection;

using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;

using KiCadSharp.Interop;

using Kiapi.Common;

using Microsoft.Extensions.Logging;

namespace KiCadSharp
{

    public class KiCadClientSettings
    {
        public string? PipeName { get; set; }

        public string? Token { get; set; }

        public string? ClientName { get; internal set; }

        public const string DefaultClientName = "kicad.client";

        /// <summary>
        /// How long one request may wait for its reply before
        /// <see cref="KiCadConnectionException"/> is thrown.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Defaults to <see cref="Timeout.InfiniteTimeSpan"/>, which is what this client has always
        /// done and what nng itself defaults to. It is a setting rather than a constant because
        /// there is no one right number: <c>Ping</c> comes back in under a millisecond, while
        /// <c>RefillZones</c> on a large board is a long-running operation, and a default short
        /// enough to protect the first would abort the second.
        /// </para>
        /// <para>
        /// Waiting is no longer the same as hanging, though: the wait is cancellable, so a caller
        /// that wants a bound can pass a <see cref="CancellationTokenSource"/> with a deadline
        /// instead of setting this globally.
        /// </para>
        /// <para>
        /// It bounds the send the same way, separately. A KiCad that goes away after the dial leaves
        /// the request with nobody to take it, and the send waits for somebody until this runs out,
        /// or until the token is cancelled. Either way the <see cref="KiCadConnectionException"/>
        /// carries a <see cref="TimeoutException"/> as its inner exception.
        /// </para>
        /// </remarks>
        public TimeSpan RequestTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    }
    public class KiCadIPCClient : IDisposable
    {
        // How long to look for the reply on the calling thread before yielding it. KiCad answers a
        // Ping in well under a millisecond over a unix socket, so the overwhelming majority of
        // requests finish inside this window and never see a timer.
        private static readonly TimeSpan SpinWindow = TimeSpan.FromMilliseconds(2);

        // After that, poll on a timer with a doubling delay. Long enough to cost nothing over a
        // multi-second command, short enough that a cancellation is noticed promptly.
        private const int MaximumPollDelayMilliseconds = 25;

        private readonly ILogger<KiCadIPCClient> _logger;
        private readonly KiCadClientSettings _settings;

        // REQ carries one request at a time; see Send. Never disposed, and that is deliberate: see
        // Dispose.
        private readonly SemaphoreSlim _exchange = new(1, 1);

        // Guards _connection and _disposed, together. A call reads both under it, and so does Dispose,
        // so a call either finds the client disposed or gets a connection that Dispose will close.
        private readonly object _gate = new();

        private Connection? _connection;
        private volatile bool _disposed;

        // The sockets whose dials are under way, so that Dispose() can end them. nng's dial blocks
        // until the handshake completes, nng gives up, or the socket is closed under it, and the close
        // is what ends it here. Whoever removes a socket from this set closes it: Dispose(), the
        // caller's token, or the Open that ran the dial. Guarded by _gate.
        private readonly HashSet<NngRequestSocket> _dialing = [];

        /// <param name="settings">Socket path, token and client name.</param>
        /// <param name="logger">Where dial and error detail goes.</param>
        /// <remarks>
        /// This used to take an <c>IAPIFactory&lt;INngMsg&gt;</c> as its first argument -- the
        /// factory type of the <c>nng.NET</c> managed binding. nng is now reached by P/Invoke
        /// (<see cref="Interop.Nng"/>) and there is no factory to hand in.
        /// </remarks>
        public KiCadIPCClient(KiCadClientSettings settings, ILogger<KiCadIPCClient> logger)
        {
            _settings = settings;
            _logger = logger;
        }

        /// <summary>Whether the client has a connection to KiCad open.</summary>
        public bool IsConnected => Volatile.Read(ref _connection) is not null;

        /// <summary>
        /// Opens the socket and dials KiCad, closing the connection first if there is one.
        /// <see cref="Send{TResult}"/> connects by itself when the client is not connected, so calling
        /// this first is only needed to fail early.
        /// </summary>
        /// <param name="cancellationToken">
        /// Ends the dial, at any point in it. The task then fails with an
        /// <see cref="OperationCanceledException"/> that carries this token, and the socket the dial
        /// opened is closed.
        /// </param>
        /// <remarks>
        /// The dial does not hold the calling thread. nng's dial is blocking, and against a socket that
        /// is bound but never completes the handshake -- a KiCad that has opened its API socket and is
        /// not serving yet -- it takes nng's own 10 s to give up, so it runs on a thread of its own and
        /// the task returned here completes when it returns (#105). Against a path with nothing at it
        /// the dial still fails at once, with connection refused, and nothing retries it.
        /// </remarks>
        /// <exception cref="KiCadConnectionException">
        /// No socket path is configured, it is longer than a socket path can be on this platform,
        /// nothing at it completes nng's handshake, or no native nng library could be loaded.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// <paramref name="cancellationToken"/> was cancelled, before or during the dial; or
        /// <see cref="Dispose"/> was called while the dial was under way, which ended it.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The client had been disposed before the call.</exception>
        public async ValueTask Connect(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (IsConnected)
            {
                Disconnect();
            }

            await Open(cancellationToken);
        }

        /// <summary>
        /// The client's connection, or <see langword="null"/> when it has none.
        /// </summary>
        /// <remarks>
        /// Read under the same lock <see cref="Dispose"/> takes. A call that gets a connection here is
        /// one that <see cref="Dispose"/> will cut off, with a cancellation. A call that comes too late
        /// for that gets the <see cref="ObjectDisposedException"/> instead.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
        private Connection? Current()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _connection;
            }
        }

        /// <summary>
        /// Dials KiCad, and makes the result the client's connection unless another call got there first,
        /// in which case that one is returned.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The dial runs on a thread of its own, because nng's <c>nng_dial</c> blocks until the
        /// handshake completes, nng gives up, or the socket is closed under it. Closing it is how a dial
        /// is ended here, by <paramref name="cancellationToken"/> and by <see cref="Dispose"/>.
        /// Measured on nng 1.3.2 and 1.4.0, the two this package ships, against a listener that never
        /// accepts and against one that holds the handshake: <c>nng_close</c> from another thread
        /// returned in under 0.1 ms, and the blocked <c>nng_dial</c> returned <c>NNG_ECLOSED</c> at the
        /// same moment. In nng's core (<c>src/core/dialer.c</c>, both versions) the socket's shutdown
        /// aborts the dialer's connect aio with <c>NNG_ECLOSED</c>, and <c>dialer_connect_cb</c> finishes
        /// the aio the synchronous dial waits on with that result, whatever the transport.
        /// </para>
        /// <para>
        /// Whoever removes the socket from <see cref="_dialing"/> closes it, so the close runs once, on
        /// one thread. A dial whose socket somebody else removed was ended on purpose, and the call
        /// reports the cancellation, with the caller's token when it was the caller's. A dial that fails
        /// on its own closes its socket here, since nobody else has seen it. A socket that dialed after
        /// <see cref="Dispose"/> ran, or that lost the race to another call's dial, is closed here too.
        /// </para>
        /// </remarks>
        private async ValueTask<Connection> Open(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_settings.PipeName))
            {
                throw new KiCadConnectionException("Pipename not provided");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Before nng sees it: nng refuses a path too long for the platform as "Address invalid",
            // which does not say what is wrong (#108). This does. The limit is IpcPathLimit's.
            if (IpcPathLimit.TooLong(_settings.PipeName) is { } tooLong)
            {
                throw new KiCadConnectionException($"Failed to connect to KiCad at '{_settings.PipeName}': {tooLong}");
            }

            NngRequestSocket socket;
            try
            {
                // Sending and receiving are both polled, and bounded by the loop in Poll; the socket
                // options are set too, but no call this client makes waits on them.
                socket = NngRequestSocket.Open(_settings.RequestTimeout, _settings.RequestTimeout);
            }
            catch (NngException exception)
            {
                // nng's failures are the ones worth renaming here. A KiCadConnectionException from
                // the loader -- no libnng for this platform -- already says everything it can, and
                // goes past untouched.
                throw ConnectionFailed(exception);
            }

            var disposed = false;
            lock (_gate)
            {
                if (_disposed)
                {
                    disposed = true;
                }
                else
                {
                    _dialing.Add(socket);
                }
            }

            if (disposed)
            {
                socket.Dispose();
                throw DisposedWhileConnecting();
            }

            Exception? failure;
            using (cancellationToken.UnsafeRegister(_ => AbandonDial(socket), null))
            {
                // On a thread of its own: it blocks in nng for up to nng's 10 s, and holding a thread
                // pool thread for that starves the pool on a small machine (#68).
                failure = await Task.Factory.StartNew(
                    () => Dial(socket, _settings.PipeName),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
            }

            bool mine;
            lock (_gate)
            {
                mine = _dialing.Remove(socket);
            }

            if (!mine)
            {
                // The token or Dispose() closed the socket under the dial, and the dial reported the
                // close. Whichever it was, the call was ended on purpose.
                throw cancellationToken.IsCancellationRequested
                    ? new OperationCanceledException("Connecting to KiCad was cancelled.", failure, cancellationToken)
                    : DisposedWhileConnecting(failure);
            }

            if (failure is not null)
            {
                socket.Dispose();
                throw ConnectionFailed(failure);
            }

            var opened = new Connection(socket);
            Connection? current;
            lock (_gate)
            {
                if (_disposed)
                {
                    current = null;
                }
                else
                {
                    _connection ??= opened;
                    current = _connection;
                }
            }

            if (current != opened)
            {
                opened.Close();
            }

            if (current is null)
            {
                throw DisposedWhileConnecting();
            }

            if (current == opened)
            {
                _logger.LogDebug("Connected to KiCad at {Socket} using nng {NngVersion}.", _settings.PipeName, Nng.Version());
            }

            return current;
        }

        /// <summary>
        /// The dial itself, on the thread that runs it: <see langword="null"/> when it connected, or
        /// what nng said when it did not.
        /// </summary>
        /// <remarks>
        /// An <see cref="ObjectDisposedException"/> is the socket's own, for a close that landed before
        /// the dial reached nng; a close that lands during it comes back as nng's <c>NNG_ECLOSED</c>.
        /// Either way the socket is not in <see cref="_dialing"/> any more, and <see cref="Open"/>
        /// reports the cancellation rather than this.
        /// </remarks>
        private static Exception? Dial(NngRequestSocket socket, string url)
        {
            try
            {
                socket.Dial(url);
                return null;
            }
            catch (Exception exception) when (exception is NngException or ObjectDisposedException)
            {
                return exception;
            }
        }

        /// <summary>
        /// Ends the dial on <paramref name="socket"/>, if it is still under way, by closing the socket
        /// under it. The caller's token runs this when it is cancelled.
        /// </summary>
        private void AbandonDial(NngRequestSocket socket)
        {
            bool mine;
            lock (_gate)
            {
                mine = _dialing.Remove(socket);
            }

            if (mine)
            {
                socket.Dispose();
            }
        }

        private KiCadConnectionException ConnectionFailed(Exception exception) =>
            new($"Failed to connect to KiCad at '{_settings.PipeName}': {exception.Message}", exception);

        private static OperationCanceledException DisposedWhileConnecting(Exception? innerException = null) =>
            new("The client was disposed while it was connecting to KiCad.", innerException, new CancellationToken(canceled: true));

        /// <summary>
        /// Sends <paramref name="command"/> to KiCad and returns its reply, connecting first if the
        /// client is not connected.
        /// </summary>
        /// <typeparam name="TResult">The message type the command returns.</typeparam>
        /// <param name="command">The command to send.</param>
        /// <param name="cancellationToken">
        /// Cancels the round trip at any point in it: while the client dials KiCad, which it does
        /// without holding a thread (see <see cref="Connect"/>); while the send waits for KiCad to take
        /// the request, which it does without holding a thread when KiCad has gone away; and while the
        /// reply is awaited.
        /// </param>
        /// <returns>KiCad's reply.</returns>
        /// <remarks>
        /// Every failure to get a result is a <see cref="KiCadIpcException"/>, and cancellation is not
        /// one: catch <see cref="KiCadIpcException"/> for "KiCad could not be asked, or said no", and
        /// let <see cref="OperationCanceledException"/> through.
        /// </remarks>
        /// <exception cref="KiCadConnectionException">
        /// KiCad could not be reached, nng refused the send or the receive, the reply is not an
        /// <c>ApiResponse</c>, or <see cref="KiCadClientSettings.RequestTimeout"/> ran out (the inner
        /// exception is then a <see cref="TimeoutException"/>).
        /// </exception>
        /// <exception cref="ApiException">
        /// KiCad answered with a status other than <c>AS_OK</c>, which
        /// <see cref="ApiException.StatusCode"/> carries, or answered <c>AS_OK</c> without a
        /// <typeparamref name="TResult"/> in the reply.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// <paramref name="cancellationToken"/> was cancelled, and the exception carries it as its
        /// <see cref="OperationCanceledException.CancellationToken"/>; or <see cref="Disconnect"/> or
        /// <see cref="Dispose"/> was called while the call was under way, at any point in it: while it
        /// connected, waited behind another call on this client, waited to send, or waited for the
        /// reply.
        /// </exception>
        /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">
        /// The client had been disposed before the call. A call already under way when the client is
        /// disposed ends with <see cref="OperationCanceledException"/> instead.
        /// </exception>
        public async ValueTask<TResult> Send<TResult>(IMessage command, CancellationToken cancellationToken = default)
            where TResult : IMessage, new()
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            // The one connection this call runs on, start to finish: its socket, and the token that
            // Disconnect() and Dispose() cancel before they close that socket. Reading the socket and
            // the token separately let a call send on one connection while watching another's token,
            // which nothing would ever cancel.
            var connection = Current() ?? await Open(cancellationToken);

            // Linked after connecting, not before. A token linked before a reconnect belonged to the
            // connection Disconnect() had cancelled: a Send after Disconnect() failed with an
            // OperationCanceledException nobody had asked for, instead of connecting again.
            //
            // The linked token used to be built and then never looked at, so nothing this client did
            // could be cancelled or would even notice a disconnect. It is now honoured for the whole
            // round trip: the send and the reply are both polled for rather than blocked on, so
            // cancelling while either waits returns here instead of waiting for KiCad.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(connection.Closing, cancellationToken);
            ThrowIfAbandoned(linked.Token, cancellationToken);

            var envelope = new ApiRequest();
            envelope.Message = Any.Pack(command);
            envelope.Header = new ApiRequestHeader()
            {
                // Never null. Protobuf's generated string setters throw ArgumentNullException, so a
                // client that KiCad did not launch itself -- one with no KICAD_API_TOKEN in its
                // environment -- used to die here, on its first request, before a single byte
                // reached KiCad. An empty token is exactly what a new client is supposed to send:
                // KiCad answers with one, and the reply handler below adopts it.
                KicadToken = _settings.Token ?? string.Empty,

                // The client's own name, not the socket path. KiCad shows this in its API log, so
                // sending the pipe name made every client look the same and made the name passed to
                // AddKiCad("my-plugin") do nothing.
                ClientName = _settings.ClientName ?? KiCadClientSettings.DefaultClientName,
            };

            var socket = connection.Socket;
            ApiResponse? reply;

            // REQ carries one request at a time. Two callers sharing a client would otherwise
            // interleave their sends and take each other's replies.
            try
            {
                await _exchange.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (linked.Token.IsCancellationRequested)
            {
                // Queued behind another caller's request. The same cancellation as anywhere else in
                // the round trip, carrying the caller's own token rather than the linked one.
                throw Abandoned(linked.Token, cancellationToken);
            }

            try
            {
                try
                {
                    await SendRequest(socket, envelope.ToByteArray(), linked.Token, cancellationToken);
                }
                catch (NngException exception)
                {
                    // A cancellation, a disconnect and RequestTimeout are already the exceptions they
                    // should be; anything else nng refused is a connection failure.
                    throw new KiCadConnectionException($"Failed to send command to KiCad: {exception.Message}", exception);
                }

                try
                {
                    var response = await Receive(socket, linked.Token, cancellationToken);
                    reply = ApiResponse.Parser.ParseFrom(response);
                }
                catch (Exception exception) when (exception is NngException or InvalidProtocolBufferException)
                {
                    // The two things this block can produce that a caller should see as a connection
                    // failure: nng refused, or KiCad sent something that is not an ApiResponse. A
                    // cancellation and a RequestTimeout are already the exceptions they should be,
                    // and re-wrapping them would only bury them.
                    throw new KiCadConnectionException($"Error receiving reply from KiCad: {exception.Message}", exception);
                }
            }
            finally
            {
                // Never disposed, so this cannot throw ObjectDisposedException over the exception
                // that is already on its way out. See Dispose.
                _exchange.Release();
            }

            var result = Unpack<TResult>(command, reply);

            // A reply with no header is not KiCad's -- it sets one on every reply except AS_NOT_READY,
            // which never gets here -- but it is no reason to fail a request that did succeed. There
            // is simply no token to adopt from it.
            if (string.IsNullOrWhiteSpace(_settings.Token) && reply.Header is { } header)
            {
                _settings.Token = header.KicadToken;
            }

            ThrowIfAbandoned(linked.Token, cancellationToken);
            return result;
        }

        /// <summary>
        /// The <typeparamref name="TResult"/> in KiCad's reply, or the <see cref="ApiException"/> that
        /// says why there is none.
        /// </summary>
        /// <remarks>
        /// Every field of an <see cref="ApiResponse"/> is optional on the wire, and three of the ways
        /// one can be missing used to escape as something other than an IPC failure: a reply with no
        /// status (an empty message parses as one) and an <c>AS_OK</c> with no payload each threw a
        /// <see cref="NullReferenceException"/>, and a payload that names the right type but does not
        /// parse threw protobuf's <see cref="InvalidProtocolBufferException"/>.
        /// </remarks>
        private static TResult Unpack<TResult>(IMessage command, ApiResponse reply)
            where TResult : IMessage, new()
        {
            var commandName = command.Descriptor.FullName;
            var status = reply.Status;

            if (status is null)
            {
                // C# protobuf leaves an absent message field null; C++ and Python -- KiCad and
                // kicad-python -- read it as the default instance, whose status is AS_UNKNOWN.
                // kicad-python raises its ApiError with that code for such a reply, and so does this.
                throw new ApiException(
                    $"KiCad's reply to {commandName} carried no status.",
                    ApiStatusCode.AsUnknown);
            }

            if (status.Status != ApiStatusCode.AsOk)
            {
                var described = string.IsNullOrEmpty(status.ErrorMessage) ? string.Empty : $": {status.ErrorMessage}";
                throw new ApiException(
                    $"KiCad answered {commandName} with {StatusName(status.Status)}{described}",
                    status.Status,
                    status.ErrorMessage);
            }

            if (reply.Message is null)
            {
                throw new ApiException(
                    $"KiCad answered {commandName} with AS_OK and no payload; expected {typeof(TResult).FullName}.",
                    ApiStatusCode.AsOk);
            }

            bool unpacked;
            TResult result;
            try
            {
                unpacked = reply.Message.TryUnpack(out result);
            }
            catch (InvalidProtocolBufferException exception)
            {
                throw new ApiException(
                    $"Failed to unpack {typeof(TResult).FullName} from the response to {commandName}: {exception.Message}",
                    ApiStatusCode.AsOk,
                    innerException: exception);
            }

            if (!unpacked)
            {
                throw new ApiException(
                    $"Failed to unpack {typeof(TResult).FullName} from the response to {commandName}: the reply carries {reply.Message.TypeUrl}.",
                    ApiStatusCode.AsOk);
            }

            return result;
        }

        /// <summary>
        /// The status as KiCad's <c>.proto</c> spells it, <c>AS_UNHANDLED</c> rather than
        /// <c>AsUnhandled</c>, which is also how KiCad's own API log prints it. A code this build does
        /// not know -- a newer KiCad's -- is printed as its number.
        /// </summary>
        private static string StatusName(ApiStatusCode code) =>
            typeof(ApiStatusCode).GetField(code.ToString())?.GetCustomAttribute<OriginalNameAttribute>()?.Name
            ?? $"status {(int)code}";

        /// <summary>
        /// Hands the request to nng once it has a connection to send it on, without blocking on nng.
        /// </summary>
        /// <remarks>
        /// Against a KiCad that is there, the first attempt sends it. The wait is for a KiCad that went
        /// away after the dial: the REQ socket then has nobody to hand the request to, and this polls
        /// until somebody takes it, the token is cancelled, or
        /// <see cref="KiCadClientSettings.RequestTimeout"/> runs out.
        /// </remarks>
        private ValueTask SendRequest(NngRequestSocket socket, byte[] request, CancellationToken linked, CancellationToken caller) =>
            Poll(
                () => socket.TrySend(request),
                timeout => new KiCadConnectionException(
                    $"KiCad did not take the request within {timeout}: nng had no connection ready to send it on.",
                    // The same TimeoutException inside as for the reply, around what nng answered the
                    // last attempt: NNG_EAGAIN, "not now".
                    new TimeoutException(
                        $"KiCad did not take the request within {timeout}.",
                        new NngException(nameof(Nng.nng_sendmsg), Nng.Again))),
                linked,
                caller);

        /// <summary>
        /// Waits for the reply to the request just sent, without blocking on nng.
        /// </summary>
        private async ValueTask<byte[]> Receive(NngRequestSocket socket, CancellationToken linked, CancellationToken caller)
        {
            byte[]? payload = null;

            await Poll(
                () => socket.TryReceive(out payload),
                // A TimeoutException inside, as HttpClient does for its own Timeout, so a caller can
                // tell RequestTimeout running out from the other connection failures without reading
                // the message.
                timeout => new KiCadConnectionException(
                    $"KiCad did not reply within {timeout}. The request is still outstanding and will be replaced by the next one.",
                    new TimeoutException($"No reply from KiCad within {timeout}.")),
                linked,
                caller);

            // Poll returns only once TryReceive has returned true, and then the payload is set.
            return payload!;
        }

        /// <summary>
        /// Calls <paramref name="attempt"/> until it returns <see langword="true"/>, without ever
        /// blocking in nng: on the calling thread for the first <see cref="SpinWindow"/>, then on a
        /// timer with a doubling delay.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Both halves of the round trip go through here, because neither can be a blocking nng call.
        /// A blocking <c>nng_recvmsg</c> that times out destroys the outstanding request: measured,
        /// the reply that arrives afterwards is discarded rather than returned by a second receive. A
        /// blocking <c>nng_sendmsg</c> with nobody to take the request waits in nng for as long as
        /// <c>send-timeout</c> allows, which by default is forever, on the calling thread, and no
        /// token reaches it (#58). Asking nng for "now or not at all", and asking again, leaves the
        /// request standing between looks, and gives the token somewhere to be observed.
        /// </para>
        /// <para>
        /// <see cref="KiCadClientSettings.RequestTimeout"/> bounds each call separately, as the
        /// socket's <c>send-timeout</c> and the receive loop used to. When it runs out, the exception
        /// <paramref name="timedOut"/> builds for it is thrown.
        /// </para>
        /// </remarks>
        private async ValueTask Poll(Func<bool> attempt, Func<TimeSpan, KiCadConnectionException> timedOut, CancellationToken linked, CancellationToken caller)
        {
            var elapsed = Stopwatch.StartNew();
            var timeout = _settings.RequestTimeout;
            var delay = 0;

            while (true)
            {
                // Before the attempt, not after: Disconnect() cancels this token and then closes the
                // socket, so a poll that reads the token first reports the disconnect as a
                // cancellation rather than as a use of a closed socket.
                ThrowIfAbandoned(linked, caller);

                try
                {
                    if (attempt())
                    {
                        return;
                    }
                }
                catch (Exception exception) when ((exception is NngException or ObjectDisposedException) && linked.IsCancellationRequested)
                {
                    // The same disconnect, arriving between the look at the token above and the nng
                    // call: the socket is disposed, or nng closed it during the call.
                    throw Abandoned(linked, caller, exception);
                }

                if (timeout != Timeout.InfiniteTimeSpan && elapsed.Elapsed >= timeout)
                {
                    throw timedOut(timeout);
                }

                if (elapsed.Elapsed < SpinWindow)
                {
                    Thread.SpinWait(64);
                    continue;
                }

                delay = delay == 0 ? 1 : Math.Min(delay * 2, MaximumPollDelayMilliseconds);

                // A cancelled delay does not throw here. The top of the loop reports it, with the
                // caller's own token when it was the caller who cancelled.
                await Task.Delay(delay, linked).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
            }
        }

        /// <summary>Throws <see cref="Abandoned"/> once <paramref name="linked"/> is cancelled.</summary>
        private void ThrowIfAbandoned(CancellationToken linked, CancellationToken caller)
        {
            if (linked.IsCancellationRequested)
            {
                throw Abandoned(linked, caller);
            }
        }

        /// <summary>
        /// The cancellation that ends a request once <paramref name="linked"/>, the connection's token
        /// linked with the caller's, is cancelled.
        /// </summary>
        /// <remarks>
        /// When the caller cancelled, it carries the caller's own token, so that the caller can tell
        /// its cancellation from any other by <see cref="OperationCanceledException.CancellationToken"/>.
        /// Otherwise <see cref="Disconnect"/> or <see cref="Dispose"/> cut the request off, and it
        /// carries the linked token.
        /// </remarks>
        private OperationCanceledException Abandoned(CancellationToken linked, CancellationToken caller, Exception? innerException = null) =>
            caller.IsCancellationRequested
                ? new OperationCanceledException("The request was cancelled.", innerException, caller)
                : new OperationCanceledException(
                    _disposed
                        ? "The request was abandoned: the client was disposed."
                        : "The request was abandoned: the client was disconnected.",
                    innerException,
                    linked);

        /// <summary>
        /// Sends a command whose reply carries no result. Fails exactly as
        /// <see cref="Send{TResult}"/> does.
        /// </summary>
        /// <param name="command">The command to send.</param>
        /// <param name="cancellationToken">Cancels the round trip; see <see cref="Send{TResult}"/>.</param>
        public async ValueTask Send(IMessage command, CancellationToken cancellationToken = default)
        {
            await Send<Empty>(command, cancellationToken);
        }

        /// <summary>
        /// Closes the connection, if there is one. Every call under way on it ends with an
        /// <see cref="OperationCanceledException"/>, and the next <see cref="Send{TResult}"/> connects
        /// again.
        /// </summary>
        /// <remarks>
        /// Safe from any thread at any time: concurrently with calls, with <see cref="Dispose"/> and
        /// with itself. After <see cref="Dispose"/> it does nothing. A call that is still dialing is not
        /// on the connection, and this does not end it: its dial goes on, and what it connects becomes
        /// the client's connection, as a <see cref="Send{TResult}"/> made after this would connect.
        /// </remarks>
        public void Disconnect()
        {
            Connection? connection;
            lock (_gate)
            {
                connection = _connection;
                _connection = null;
            }

            connection?.Close();
        }

        /// <summary>
        /// Closes the connection and ends every call under way on this client with an
        /// <see cref="OperationCanceledException"/>. A call made afterwards throws
        /// <see cref="ObjectDisposedException"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A call is under way from the moment it finds the client not disposed, and it ends with the
        /// same exception at every point in the round trip: while it connects, while it waits behind
        /// another call on this client, while it waits to send, and while it waits for the reply. The
        /// exception is the one <see cref="Disconnect"/> produces, as <see cref="HttpClient"/> cancels
        /// its pending requests when it is disposed: the call did not fail, it was stopped. Its
        /// <see cref="OperationCanceledException.CancellationToken"/> is not the caller's, unless the
        /// caller had cancelled too.
        /// </para>
        /// <para>
        /// Safe to call more than once, and from any thread concurrently with calls,
        /// <see cref="Disconnect"/> and itself. It does not wait for the calls it ends, and it never
        /// waits on nng or on KiCad. A call that is dialing is ended at once too, by closing the socket
        /// under its dial; see <see cref="Open"/> for what nng does then, and how quickly.
        /// </para>
        /// <para>
        /// The <see cref="SemaphoreSlim"/> that keeps calls in turn is not disposed, and neither is the
        /// connection's <see cref="CancellationTokenSource"/>. The calls this ends still use both while
        /// they unwind, and neither holds anything but memory: neither has a timer, and nothing reads
        /// their wait handles. Disposing the semaphore was #67. A call ended here threw
        /// <see cref="ObjectDisposedException"/> from <see cref="SemaphoreSlim.Release()"/> in most
        /// runs, and a call waiting behind another sometimes never ended, because disposing a
        /// <see cref="SemaphoreSlim"/> drops its waiters without completing them. A disposed
        /// <see cref="CancellationTokenSource"/> would throw from <see cref="CancellationTokenSource.Token"/>
        /// the same way, for a call still starting.
        /// </para>
        /// </remarks>
        public void Dispose()
        {
            Connection? connection;
            NngRequestSocket[] dialing;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                connection = _connection;
                _connection = null;
                dialing = [.. _dialing];
                _dialing.Clear();
            }

            connection?.Close();

            // Each dial ends as soon as its socket closes; the call that ran it reports the disposal.
            foreach (var socket in dialing)
            {
                socket.Dispose();
            }
        }

        /// <summary>
        /// One dialled socket, and the token that ends every call made on it.
        /// </summary>
        /// <remarks>
        /// Whoever takes a connection out of <see cref="_connection"/> closes it: <see cref="Disconnect"/>,
        /// <see cref="Dispose"/>, or the <see cref="Open"/> that never put its own there. Nobody else can,
        /// so <see cref="Close"/> runs once for each connection, on one thread. Its cancellation has
        /// therefore reached every call's linked token before the socket closes under those calls, and
        /// a call that then finds the socket closed always finds its token cancelled too. It reports a
        /// cancellation rather than a transport failure.
        /// </remarks>
        private sealed class Connection(NngRequestSocket socket)
        {
            // Never disposed: see KiCadIPCClient.Dispose. A disposed one would also throw from Token,
            // which a call still starting on this connection may read after Close.
            private readonly CancellationTokenSource _closing = new();

            internal NngRequestSocket Socket { get; } = socket;

            internal CancellationToken Closing => _closing.Token;

            internal void Close()
            {
                // The token first. Cancel() runs every linked token's cancellation before it returns,
                // and Poll reads the token before each nng call, so a call sees a cancellation and not
                // a closed socket. The socket is closed even if a callback throws, since nothing else
                // will close it.
                try
                {
                    _closing.Cancel();
                }
                finally
                {
                    Socket.Dispose();
                }
            }
        }
    }
}
