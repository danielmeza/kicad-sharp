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

    static NngTestPeer() =>
        // This assembly's own P/Invokes need the same libnng the library found, and by the same
        // rules -- runtimes/<rid>/native next to the test binaries.
        NativeLibrary.SetDllImportResolver(typeof(NngTestPeer).Assembly, NngLibraryResolver.Resolve);

    private NngTestPeer(Nng.NngSocket socket, string url, TimeSpan delay, bool answer)
    {
        _socket = socket;
        Url = url;
        _serving = Task.Factory.StartNew(
            () => Serve(delay, answer, _stopping.Token),
            TaskCreationOptions.LongRunning);
    }

    /// <summary>The <c>ipc://</c> URL a client should dial.</summary>
    internal string Url { get; }

    /// <summary>How many requests this peer has taken off the wire.</summary>
    internal int RequestsReceived;

    /// <summary>Starts a peer that answers each request after <paramref name="delay"/>.</summary>
    internal static NngTestPeer Start(TimeSpan delay, bool answer = true)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kicadsharp-test-{Guid.NewGuid():N}.sock");
        Check("nng_rep0_open", nng_rep0_open(out var socket));
        Check("nng_listen", nng_listen(socket, $"ipc://{path}", out _, 0));
        return new NngTestPeer(socket, $"ipc://{path}", delay, answer);
    }

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

            // The received message is reused rather than replaced: REP routes a reply by the
            // backtrace header the request arrived with.
            var payload = Reply().ToByteArray();
            nng_msg_clear(message);
            Nng.nng_msg_append(message, payload, (nuint)payload.Length);

            if (Nng.nng_sendmsg(_socket, message, 0) != 0)
            {
                Nng.nng_msg_free(message);
            }
        }
    }

    private static ApiResponse Reply() => new()
    {
        Header = new ApiResponseHeader { KicadToken = "test-peer-token" },
        Status = new ApiResponseStatus { Status = ApiStatusCode.AsOk },
        Message = Any.Pack(new Empty()),
    };

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
