using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace KiCadSharp.Tests;

/// <summary>
/// A peer that takes nng's <c>ipc://</c> connection and holds its handshake: until the test answers
/// it, so that a dial can be caught while it waits, or for ever, so that the dial runs into nng's
/// own timeout. A KiCad that has created its API endpoint and is not serving yet looks like this.
/// </summary>
/// <remarks>
/// <para>
/// nng's <c>ipc://</c> transport opens a connection with an 8-byte header each way:
/// <c>00 'S' 'P' 00</c>, the protocol number, and two zero bytes. REQ's arrives as
/// <c>00 53 50 00 00 30 00 00</c>. nng's dial returns only once the other side's header has
/// arrived, or after nng's own 10 s (<c>ipc.c</c>, <c>nni_aio_set_timeout(…, 10000)</c> in
/// <c>ipc_pipe_start</c>, the same line in 1.3.2 and 1.4.0), which
/// <c>IpcFailureTests.ASocketThatNeverCompletesTheHandshakeIsAConnectionFailure</c> measures.
/// Measured against the nng 1.3.2 this repository ships for linux-x64: the dial was still waiting
/// 500 ms after the connection was accepted, returned as soon as REP's header (protocol <c>0x31</c>)
/// was written, and failed at once with <c>NNG_EPROTO</c> when the header was not <c>'S' 'P'</c>.
/// </para>
/// <para>
/// What nng connects to differs by platform, and so does this peer:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Linux and macOS:</b> a Unix domain socket at a <see cref="SocketPaths"/> path, which is what
/// nng's <c>ipc://</c> dials there.
/// </description></item>
/// <item><description>
/// <b>Windows:</b> a named pipe. nng 1.4.0 opens <c>\\.\pipe\</c> followed by the path
/// (<c>win_ipcdial.c</c>, line 240; the prefix in <c>win_ipc.h</c>, line 20). A Unix socket bound
/// at the path is never looked at: the dial fails at once with "Connection refused", which is what
/// <c>ERROR_FILE_NOT_FOUND</c> becomes at <c>win_ipcdial.c</c>, lines 88-89 (#106, #122). So here
/// it is a <see cref="NamedPipeServerStream"/> on a bare name, <c>kicadsharp-held-</c> and eight
/// random characters. .NET opens that under the same prefix, unchanged
/// (<c>PipeStream.GetPipePath</c>), and nothing appears on disk, so the name needs no directory.
/// </description></item>
/// </list>
/// <para>
/// The accept starts with the peer, before any dial, so that a test can dial on its own thread and
/// block there; on Windows that is also what puts the pipe instance into its listening state.
/// </para>
/// </remarks>
internal sealed class HeldHandshake : IDisposable
{
    private const byte Rep0 = 0x31;
    private const byte Req0 = 0x30;

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private readonly IDisposable _listener;
    private readonly Task<Stream> _accepted;
    private readonly string? _path;

    private HeldHandshake(string url, IDisposable listener, Task<Stream> accepted, string? path)
    {
        Url = url;
        _listener = listener;
        _accepted = accepted;
        _path = path;
    }

    /// <summary>The <c>ipc://</c> URL a client should dial.</summary>
    internal string Url { get; }

    internal static HeldHandshake Start()
    {
        if (OperatingSystem.IsWindows())
        {
            var name = "kicadsharp-held-" + RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz0123456789", 8);
            var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            return new HeldHandshake("ipc://" + name, pipe, Accept(pipe), path: null);
        }

        var path = SocketPaths.New("held");
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(8);
        return new HeldHandshake("ipc://" + path, listener, Accept(listener), path);
    }

    private static async Task<Stream> Accept(NamedPipeServerStream pipe)
    {
        await pipe.WaitForConnectionAsync();
        return pipe;
    }

    private static async Task<Stream> Accept(Socket listener) =>
        new NetworkStream(await listener.AcceptAsync(), ownsSocket: true);

    /// <summary>Takes the dial's connection, and reads REQ's header off it.</summary>
    internal async Task<Stream> AcceptDial()
    {
        var connection = await _accepted.WaitAsync(Bound);
        var header = new byte[8];
        await connection.ReadExactlyAsync(header).AsTask().WaitAsync(Bound);

        Assert.Equal(Header(Req0), header);
        return connection;
    }

    internal static async Task CompleteHandshake(Stream connection)
    {
        await connection.WriteAsync(Header(Rep0));
        await connection.FlushAsync();
    }

    internal static async Task RefuseHandshake(Stream connection)
    {
        await connection.WriteAsync(new byte[] { 0x00, (byte)'X', (byte)'X', 0x00, 0x00, Rep0, 0x00, 0x00 });
        await connection.FlushAsync();
    }

    /// <summary>How many bytes arrive before the client closes the connection.</summary>
    internal static async Task<int> ReadUntilClosed(Stream connection)
    {
        var total = 0;
        var buffer = new byte[256];
        while (true)
        {
            var got = await connection.ReadAsync(buffer).AsTask().WaitAsync(Bound);
            if (got == 0)
            {
                return total;
            }

            total += got;
        }
    }

    private static byte[] Header(byte protocol) => [0x00, (byte)'S', (byte)'P', 0x00, 0x00, protocol, 0x00, 0x00];

    public void Dispose()
    {
        if (_accepted.IsCompletedSuccessfully)
        {
            _accepted.Result.Dispose();
        }

        _listener.Dispose();

        // An accept that was still waiting fails now that its listener is gone. That is nobody's
        // failure, so it is observed here rather than left for the finalizer thread to report.
        _accepted.ContinueWith(
            static accepted => _ = accepted.Exception,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        if (_path is not null)
        {
            File.Delete(_path);
        }
    }
}
