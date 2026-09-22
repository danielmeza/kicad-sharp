using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using KiCadSharp.Interop;

using Kiapi.Common;

// See the comment on AllowUnsafeBlocks in the project file.
[assembly: DisableRuntimeMarshalling]

namespace KiCadSharp.Tests;

/// <summary>
/// An nng REP socket that answers like KiCad does, after a delay of the test's choosing.
/// </summary>
/// <remarks>
/// <para>
/// The IPC client's timeout and cancellation behaviour is about what happens when a reply is
/// <i>late</i>, and a real KiCad answers a <c>Ping</c> in under a millisecond -- there is no way to
/// observe a late reply against one. This is the other end of the socket, in process, with a
/// controllable delay.
/// </para>
/// <para>
/// The two entry points it needs beyond <see cref="Nng"/> -- <c>nng_rep0_open</c> and
/// <c>nng_listen</c> -- are declared here rather than in the library. The library dials, it never
/// listens, and a production interop surface should carry what production calls.
/// </para>
/// </remarks>
internal sealed partial class NngTestPeer : IDisposable
{
    private readonly Nng.NngSocket _socket;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _serving;
    private readonly Func<ApiRequest, ApiResponse> _respond;
    private readonly List<ApiRequest> _requests = [];

    static NngTestPeer() =>
        // This assembly's own P/Invokes need the same libnng the library found, and by the same
        // rules -- runtimes/<rid>/native next to the test binaries.
        NativeLibrary.SetDllImportResolver(typeof(NngTestPeer).Assembly, NngLibraryResolver.Resolve);

    private NngTestPeer(Nng.NngSocket socket, string url, TimeSpan delay, bool answer, Func<ApiRequest, ApiResponse> respond)
    {
        _socket = socket;
        _respond = respond;
        Url = url;
        _serving = Task.Factory.StartNew(
            () => Serve(delay, answer, _stopping.Token),
            TaskCreationOptions.LongRunning);
    }

    /// <summary>The <c>ipc://</c> URL a client should dial.</summary>
    internal string Url { get; }

    /// <summary>How many requests this peer has taken off the wire.</summary>
    internal int RequestsReceived;

    /// <summary>Every request this peer has parsed, in order.</summary>
    internal ApiRequest[] Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>The one request a single-command test expects, unpacked to the command it sent.</summary>
    internal T Single<T>() where T : IMessage, new()
    {
        var request = Assert.Single(Requests);
        Assert.True(request.Message.Is(new T().Descriptor), $"expected {typeof(T).Name}, got {request.Message.TypeUrl}");
        return request.Message.Unpack<T>();
    }

    /// <summary>Starts a peer that answers each request after <paramref name="delay"/>.</summary>
    internal static NngTestPeer Start(TimeSpan delay, bool answer = true) =>
        Start(delay, answer, _ => Ok(new Empty()));

    /// <summary>
    /// Starts a peer that answers at once, with whatever <paramref name="respond"/> returns for
    /// the request it parsed. This is what the command tests use: they send one command through
    /// the real client and transport, then look at what arrived.
    /// </summary>
    internal static NngTestPeer Start(Func<ApiRequest, ApiResponse> respond) =>
        Start(TimeSpan.Zero, answer: true, respond);

    /// <summary>Starts a peer that answers every request with <paramref name="reply"/>.</summary>
    internal static NngTestPeer Start(IMessage reply) => Start(_ => Ok(reply));

    private static NngTestPeer Start(TimeSpan delay, bool answer, Func<ApiRequest, ApiResponse> respond)
    {
        var url = SocketPaths.NewUrl("peer");
        Check("nng_rep0_open", nng_rep0_open(out var socket));
        Check("nng_listen", nng_listen(socket, url, out _, 0));
        return new NngTestPeer(socket, url, delay, answer, respond);
    }

    /// <summary>An OK reply carrying <paramref name="message"/>.</summary>
    internal static ApiResponse Ok(IMessage message) => new()
    {
        Header = new ApiResponseHeader { KicadToken = "test-peer-token" },
        Status = new ApiResponseStatus { Status = ApiStatusCode.AsOk },
        Message = Any.Pack(message),
    };

    /// <summary>A reply with a non-OK status, as a KiCad that does not know a command answers.</summary>
    internal static ApiResponse Fail(ApiStatusCode status, string errorMessage = "") => new()
    {
        Header = new ApiResponseHeader { KicadToken = "test-peer-token" },
        Status = new ApiResponseStatus { Status = status, ErrorMessage = errorMessage },
        Message = Any.Pack(new Empty()),
    };

    private void Serve(TimeSpan delay, bool answer, CancellationToken stopping)
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

            if (!answer)
            {
                Nng.nng_msg_free(message);
                continue;
            }

            if (delay > TimeSpan.Zero)
            {
                stopping.WaitHandle.WaitOne(delay);
            }

            ApiResponse reply;
            try
            {
                var body = Nng.nng_msg_body(message);
                var length = (int)Nng.nng_msg_len(message);
                var bytes = new byte[length];
                Marshal.Copy(body, bytes, 0, length);
                var request = ApiRequest.Parser.ParseFrom(bytes);
                lock (_requests)
                {
                    _requests.Add(request);
                }

                reply = _respond(request);
            }
            catch (Exception exception)
            {
                // A responder that throws must not take the serving thread down with it; the client
                // would then wait forever. Answer with the failure instead, so the test sees it.
                reply = Fail(ApiStatusCode.AsBadRequest, exception.ToString());
            }

            // The received message is reused rather than replaced: REP routes a reply by the
            // backtrace header the request arrived with.
            var payload = reply.ToByteArray();
            nng_msg_clear(message);
            Nng.nng_msg_append(message, payload, (nuint)payload.Length);

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

    private static void Check(string operation, int result)
    {
        if (result != 0)
        {
            throw new InvalidOperationException($"{operation} failed: {Nng.Describe(result)}");
        }
    }

    [LibraryImport(Nng.Library)]
    private static partial int nng_rep0_open(out Nng.NngSocket socket);

    [LibraryImport(Nng.Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int nng_listen(Nng.NngSocket socket, string url, out Nng.NngDialer listener, int flags);

    [LibraryImport(Nng.Library)]
    private static partial void nng_msg_clear(IntPtr message);
}
