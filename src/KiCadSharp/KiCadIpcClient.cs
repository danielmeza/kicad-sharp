using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Kiapi.Common;

using Microsoft.Extensions.Logging;

using nng;
namespace KiCadSharp
{

    public class KiCadClientSettings
    {
        public string? PipeName { get; set; }

        public string? Token { get; set; }

        public string? ClientName { get; internal set; }

        public const string DefaultClientName = "kicad.client";

    }
    public class KiCadIPCClient : IDisposable
    {
        private readonly ILogger<KiCadIPCClient> _logger;
        private readonly IAPIFactory<INngMsg> _messageFactory;
        private KiCadClientSettings _settings;
        private EventWaitHandle sync = new EventWaitHandle(false, EventResetMode.ManualReset);

        private IReqSocket? _socket;
        private CancellationTokenSource _connectionCancellationSource;
        public KiCadIPCClient(IAPIFactory<INngMsg> messageFactory, KiCadClientSettings settings, ILogger<KiCadIPCClient> logger)
        {
            _messageFactory = messageFactory;
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

            _socket = _messageFactory.RequesterOpen()
                .ThenDial(_settings.PipeName, nng.Native.Defines.NngFlag.NNG_FLAG_ALLOC)
                .Unwrap();

            _connectionCancellationSource = new CancellationTokenSource();
            IsConnected = true;
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
            // could be cancelled or would even notice a disconnect. The nng round trip below is
            // synchronous and cannot be interrupted once the request is on the wire, so what is
            // honest here is to refuse to start and to notice on the way out -- not to pretend the
            // send itself is cancellable.
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

            try
            {
                var request = _messageFactory.CreateMessage();
                request.Append(envelope.ToByteArray());
                socket.SendMsg(request).Unwrap();
            }
            catch (Exception ex)
            {
                throw new KiCadConnectionException($"Failed to send command to KiCad: {ex.Message}", ex);
            }

            ApiResponse? reply;
            try
            {
                var response = socket.RecvMsg().Unwrap();
                reply = ApiResponse.Parser.ParseFrom(response.AsSpan());
            }
            catch (Exception ex)
            {
                throw new KiCadConnectionException($"Error receiving reply from KiCad: {ex.Message}", ex);
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

        public async ValueTask Send(IMessage command, CancellationToken cancellationToken = default)
        {
            await Send<Empty>(command, cancellationToken);
        }

        public void Disconnect()
        {
            _connectionCancellationSource.Cancel();
            IsConnected = false;
            _socket?.Dispose();
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
