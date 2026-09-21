using System.Runtime.InteropServices;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using KiCadSharp.Interop;

using Kiapi.Common;

namespace KiCadSharp.Tests;

internal sealed partial class NngTestPeer
{
    /// <summary>
    /// Starts a peer that answers each request with the bytes <paramref name="respond"/> returns for
    /// the request's bytes, or does not answer it when that is <see langword="null"/>.
    /// </summary>
    internal static Raw StartRaw(Func<byte[], byte[]?> respond) => new(respond);

    /// <summary>
    /// A reply as <c>KICAD_API_SERVER::handleApiEvent</c> in KiCad 10.0.6 builds one: the status, the
    /// header with KiCad's token unless <paramref name="token"/> is <see langword="null"/> -- the one
    /// reply KiCad sends without it is <c>AS_NOT_READY</c> -- and the payload only when there is one.
    /// </summary>
    internal static byte[] Answer(ApiStatusCode status, string errorMessage = "", IMessage? payload = null, string? token = "test-peer-token")
    {
        var response = new ApiResponse
        {
            Status = new ApiResponseStatus { Status = status, ErrorMessage = errorMessage },
        };

        if (token is not null)
        {
            response.Header = new ApiResponseHeader { KicadToken = token };
        }

        if (payload is not null)
        {
            response.Message = Any.Pack(payload);
        }

        return response.ToByteArray();
    }

    /// <summary>
    /// The same REP peer, answering with whatever bytes a test decides on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IpcFailureTests"/> needs replies no KiCad sends on purpose: an error status, an
    /// <c>ApiResponse</c> with a field missing, and bytes that are not an <c>ApiResponse</c> at all.
    /// The last of those cannot be written as a message, so the responder here returns the bytes that
    /// go on the wire, and <see cref="Answer"/> builds the well-formed ones.
    /// </para>
    /// <para>
    /// Its own type with its own serving loop, rather than a change to the one in
    /// <c>NngTestPeer.cs</c>: that loop is what every other transport test runs against, and it stays
    /// as it was. Nested, so it shares the outer class's <c>nng_rep0_open</c> and <c>nng_listen</c>,
    /// and so the first call into them runs the outer class's static constructor, which is what
    /// points this assembly's P/Invokes at libnng.
    /// </para>
    /// </remarks>
    internal sealed class Raw : IDisposable
    {
        private readonly Nng.NngSocket _socket;
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _serving;

        internal Raw(Func<byte[], byte[]?> respond)
        {
            var path = Path.Combine(Path.GetTempPath(), $"kicadsharp-test-{Guid.NewGuid():N}.sock");
            Check("nng_rep0_open", nng_rep0_open(out _socket));
            Check("nng_listen", nng_listen(_socket, $"ipc://{path}", out _, 0));
            Url = $"ipc://{path}";
            _serving = Task.Factory.StartNew(
                () => Serve(respond, _stopping.Token),
                TaskCreationOptions.LongRunning);
        }

        /// <summary>The <c>ipc://</c> URL a client should dial.</summary>
        internal string Url { get; }

        /// <summary>How many requests this peer has taken off the wire.</summary>
        internal int RequestsReceived;

        private void Serve(Func<byte[], byte[]?> respond, CancellationToken stopping)
        {
            // Bounded so the loop can notice it has been told to stop.
            Nng.nng_socket_set_ms(_socket, Nng.OptionReceiveTimeout, 100);

            while (!stopping.IsCancellationRequested)
            {
                if (Nng.nng_recvmsg(_socket, out var message, 0) != 0)
                {
                    continue;
                }

                Interlocked.Increment(ref RequestsReceived);

                var request = new byte[(int)Nng.nng_msg_len(message)];
                if (request.Length > 0)
                {
                    Marshal.Copy(Nng.nng_msg_body(message), request, 0, request.Length);
                }

                var reply = respond(request);
                if (reply is null)
                {
                    Nng.nng_msg_free(message);
                    continue;
                }

                // Reused rather than replaced: REP routes a reply by the backtrace header the request
                // arrived with.
                nng_msg_clear(message);
                Nng.nng_msg_append(message, reply, (nuint)reply.Length);

                if (Nng.nng_sendmsg(_socket, message, 0) != 0)
                {
                    Nng.nng_msg_free(message);
                }
            }
        }

        public void Dispose()
        {
            _stopping.Cancel();
            _serving.Wait(TimeSpan.FromSeconds(5));
            Nng.nng_close(_socket);
            _stopping.Dispose();
        }
    }
}
