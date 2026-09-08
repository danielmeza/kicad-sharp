using System.Diagnostics;

using Google.Protobuf;
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
        public ValueTask Connect(CancellationToken cancellationToken = default)
        {

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


        public async ValueTask<TResult> Send<TResult>(IMessage command, CancellationToken cancellationToken = default)
            where TResult : IMessage, new()
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_connectionCancellationSource.Token, cancellationToken);

            // The linked token used to be built and then never looked at, so nothing this client did
            // could be cancelled or would even notice a disconnect. It is now honoured for the whole
            // round trip: the reply is polled for rather than blocked on, so cancelling while the
            // request is on the wire returns here instead of waiting for KiCad.
            linked.Token.ThrowIfCancellationRequested();

            if (!IsConnected)
            {
                await Connect(cancellationToken);
            }

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

            if (reply.Status.Status != ApiStatusCode.AsOk)
            {
                throw new ApiException($"KiCad returned error: {reply.Status}");
            }

            if (!reply.Message.TryUnpack<TResult>(out var result))
            {
                throw new ApiException($"Failed to unpack {typeof(TResult).FullName} from the response to {command.GetType().FullName}");
            }

            if (string.IsNullOrWhiteSpace(_settings.Token))
            {
                _settings.Token = reply.Header.KicadToken;
            }

            linked.Token.ThrowIfCancellationRequested();
            return result;
        }

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

                if (socket.TryReceive(out var payload))
                {
                    return payload;
                }

                if (timeout != Timeout.InfiniteTimeSpan && elapsed.Elapsed >= timeout)
                {
                    throw new KiCadConnectionException(
                        $"KiCad did not reply within {timeout}. The request is still outstanding and will be replaced by the next one.");
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
