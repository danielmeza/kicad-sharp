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
        /// This also bounds the send, as nng's <c>send-timeout</c>, and there a token does not reach:
        /// measured, a KiCad that goes away after the dial leaves <c>nng_sendmsg</c> waiting for a
        /// peer on the calling thread until this runs out. Either way the
        /// <see cref="KiCadConnectionException"/> carries a <see cref="TimeoutException"/> as its
        /// inner exception.
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
        private readonly SemaphoreSlim _exchange = new(1, 1);

        private NngRequestSocket? _socket;
        private CancellationTokenSource _connectionCancellationSource;
        private bool _disposed;

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
            _connectionCancellationSource = new CancellationTokenSource();
        }

        public bool IsConnected
        {
            get; private set;

        }
        /// <summary>
        /// Opens the socket and dials KiCad. <see cref="Send{TResult}"/> calls this itself when the
        /// client is not connected, so calling it first is only needed to fail early.
        /// </summary>
        /// <param name="cancellationToken">Observed before the dial, not during it.</param>
        /// <exception cref="KiCadConnectionException">
        /// No socket path is configured, nothing at it completes nng's handshake, or no native nng
        /// library could be loaded.
        /// </exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was already cancelled.</exception>
        /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
        public ValueTask Connect(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (IsConnected)
            {
                Disconnect();
            }

            if (string.IsNullOrWhiteSpace(_settings.PipeName))
            {
                throw new KiCadConnectionException("Pipename not provided");
            }

            cancellationToken.ThrowIfCancellationRequested();

            NngRequestSocket socket;
            try
            {
                // Sending is a blocking nng call, so it is bounded by a socket option; receiving is
                // polled and bounded by the loop in Receive. The dial itself is blocking, as before:
                // nng bounds it at about 10 s against a socket that is bound but not answering, and
                // fails immediately against a path that is not there.
                socket = NngRequestSocket.Dial(_settings.PipeName, _settings.RequestTimeout, _settings.RequestTimeout);
            }
            catch (NngException exception)
            {
                // nng's failures are the ones worth renaming here. A KiCadConnectionException from
                // the loader -- no libnng for this platform -- already says everything it can, and
                // goes past untouched.
                throw new KiCadConnectionException(
                    $"Failed to connect to KiCad at '{_settings.PipeName}': {exception.Message}", exception);
            }

            _socket = socket;
            _connectionCancellationSource = new CancellationTokenSource();
            IsConnected = true;
            _logger.LogDebug("Connected to KiCad at {Socket} using nng {NngVersion}.", _settings.PipeName, Nng.Version());
            return ValueTask.CompletedTask;
        }


        /// <summary>
        /// Sends <paramref name="command"/> to KiCad and returns its reply, connecting first if the
        /// client is not connected.
        /// </summary>
        /// <typeparam name="TResult">The message type the command returns.</typeparam>
        /// <param name="command">The command to send.</param>
        /// <param name="cancellationToken">
        /// Cancels the round trip, including while the reply is awaited. It cannot interrupt the send
        /// itself: with no KiCad to take the request, <c>nng_sendmsg</c> blocks the calling thread
        /// until <see cref="KiCadClientSettings.RequestTimeout"/> runs out, which by default it never
        /// does, or until <see cref="Disconnect"/> closes the socket.
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
        /// <paramref name="cancellationToken"/> was cancelled, or <see cref="Disconnect"/> was called
        /// while the request was outstanding.
        /// </exception>
        /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
        public async ValueTask<TResult> Send<TResult>(IMessage command, CancellationToken cancellationToken = default)
            where TResult : IMessage, new()
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsConnected)
            {
                await Connect(cancellationToken);
            }

            // Linked after connecting, not before. Disconnect() cancels the connection's token and
            // Connect() replaces it, so a token linked before the reconnect was already cancelled: a
            // Send after Disconnect() failed with an OperationCanceledException nobody had asked for,
            // instead of connecting again.
            //
            // The linked token used to be built and then never looked at, so nothing this client did
            // could be cancelled or would even notice a disconnect. It is now honoured for the whole
            // round trip: the reply is polled for rather than blocked on, so cancelling while the
            // request is on the wire returns here instead of waiting for KiCad.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_connectionCancellationSource.Token, cancellationToken);
            linked.Token.ThrowIfCancellationRequested();

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


            var socket = _socket ?? throw new KiCadConnectionException("Not connected to KiCad: the request socket has not been opened.");

            ApiResponse? reply;

            // REQ carries one request at a time. Two callers sharing a client would otherwise
            // interleave their sends and take each other's replies.
            await _exchange.WaitAsync(linked.Token);
            try
            {
                try
                {
                    socket.Send(envelope.ToByteArray());
                }
                catch (Exception exception) when ((exception is NngException or ObjectDisposedException) && linked.Token.IsCancellationRequested)
                {
                    // Disconnect() cancels the connection's token and then closes the socket. A send
                    // blocked in nng when the socket closes fails with NNG_ECLOSED -- measured: with
                    // the default infinite RequestTimeout and KiCad gone, nng_sendmsg waits for a peer
                    // until the socket is closed under it -- and one that starts between the two finds
                    // the socket disposed. Either way the disconnect cut the request off, and it ends
                    // the way the poll in Receive ends one: as a cancellation.
                    throw new OperationCanceledException("The request was abandoned: the client was disconnected or the operation was cancelled.", exception, linked.Token);
                }
                catch (NngException exception) when (exception.Error == Nng.TimedOut)
                {
                    // RequestTimeout is also the socket's send-timeout, and a REQ socket with no peer
                    // to hand the request to -- KiCad went away after the dial -- waits in
                    // nng_sendmsg until it runs out. The same budget as the wait for the reply, so
                    // the same TimeoutException inside.
                    throw new KiCadConnectionException(
                        $"KiCad did not take the request within {_settings.RequestTimeout}: {exception.Message}",
                        new TimeoutException($"KiCad did not take the request within {_settings.RequestTimeout}.", exception));
                }
                catch (NngException exception)
                {
                    throw new KiCadConnectionException($"Failed to send command to KiCad: {exception.Message}", exception);
                }

                try
                {
                    var response = await Receive(socket, linked.Token);
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

            linked.Token.ThrowIfCancellationRequested();
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
        /// Waits for the reply to the request just sent, without blocking on nng.
        /// </summary>
        /// <remarks>
        /// A blocking <c>nng_recvmsg</c> with a timeout cannot be used for this: measured, a receive
        /// that times out destroys the outstanding request, and the reply that arrives afterwards is
        /// discarded rather than returned by a second receive. Polling a non-blocking receive leaves
        /// the request standing between looks, which is what gives the token somewhere to be
        /// observed.
        /// </remarks>
        private async ValueTask<byte[]> Receive(NngRequestSocket socket, CancellationToken cancellationToken)
        {
            var elapsed = Stopwatch.StartNew();
            var timeout = _settings.RequestTimeout;
            var delay = 0;

            while (true)
            {
                // Before the receive, not after: Disconnect() cancels this token and then closes the
                // socket, so a poll that reads the token first reports the disconnect as a
                // cancellation rather than as a use of a closed socket.
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (socket.TryReceive(out var payload))
                    {
                        return payload;
                    }
                }
                catch (Exception exception) when ((exception is NngException or ObjectDisposedException) && cancellationToken.IsCancellationRequested)
                {
                    // The same disconnect, arriving between the look at the token above and the
                    // receive: the socket is disposed, or nng closed it during the call.
                    throw new OperationCanceledException("The request was abandoned: the client was disconnected or the operation was cancelled.", exception, cancellationToken);
                }

                if (timeout != Timeout.InfiniteTimeSpan && elapsed.Elapsed >= timeout)
                {
                    // A TimeoutException inside, as HttpClient does for its own Timeout, so a caller
                    // can tell RequestTimeout running out from the other connection failures without
                    // reading the message.
                    throw new KiCadConnectionException(
                        $"KiCad did not reply within {timeout}. The request is still outstanding and will be replaced by the next one.",
                        new TimeoutException($"No reply from KiCad within {timeout}."));
                }

                if (elapsed.Elapsed < SpinWindow)
                {
                    Thread.SpinWait(64);
                    continue;
                }

                delay = delay == 0 ? 1 : Math.Min(delay * 2, MaximumPollDelayMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

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

        public void Disconnect()
        {
            if (!_disposed)
            {
                _connectionCancellationSource.Cancel();
            }

            IsConnected = false;
            _socket?.Dispose();
            _socket = null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Disconnect();
            _disposed = true;
            _exchange.Dispose();
            _connectionCancellationSource.Dispose();
        }
    }
}
