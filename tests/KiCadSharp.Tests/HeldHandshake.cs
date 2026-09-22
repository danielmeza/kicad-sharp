using System.Net.Sockets;

namespace KiCadSharp.Tests;

/// <summary>
/// A unix socket that takes nng's connection and holds its handshake until the test answers it, so
/// that a dial can be caught while it waits.
/// </summary>
/// <remarks>
/// nng's <c>ipc://</c> transport opens a connection with an 8-byte header each way:
/// <c>00 'S' 'P' 00</c>, the protocol number, and two zero bytes. REQ's arrives as
/// <c>00 53 50 00 00 30 00 00</c>. nng's dial returns only once the other side's header has
/// arrived, or after nng's own 10 s, which <c>ASocketThatNeverCompletesTheHandshakeIsAConnectionFailure</c>
/// measures. Measured against the nng 1.3.2 this repository ships for linux-x64: the dial was
/// still waiting 500 ms after the connection was accepted, returned as soon as REP's header
/// (protocol <c>0x31</c>) was written, and failed at once with <c>NNG_EPROTO</c> when the header was
/// not <c>'S' 'P'</c>. Closing the REQ socket under the dial ends it too: measured on nng 1.3.2 and
/// 1.4.0, <c>nng_dial</c> then returned <c>NNG_ECLOSED</c> within 0.1 ms of <c>nng_close</c>, and this
/// side read end of stream 0.4 ms later (#105).
/// </remarks>
internal sealed class HeldHandshake : IDisposable
{
    private const byte Rep0 = 0x31;
    private const byte Req0 = 0x30;

    // How long a test waits for nng's connection, its header, or its close. Generous: an order of
    // magnitude above the sub-millisecond each takes.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private readonly string _path;
    private readonly Socket _listener;

    private HeldHandshake(string path, Socket listener)
    {
        _path = path;
        _listener = listener;
    }

    internal string Url => $"ipc://{_path}";

    internal static HeldHandshake Start()
    {
        var path = SocketPaths.New("held");
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(1);
        return new HeldHandshake(path, listener);
    }

    /// <summary>Takes the dial's connection, and reads REQ's header off it.</summary>
    internal async Task<Socket> AcceptDial()
    {
        var connection = await _listener.AcceptAsync().WaitAsync(Bound);
        var header = new byte[8];
        for (var read = 0; read < header.Length;)
        {
            var got = await connection.ReceiveAsync(header.AsMemory(read)).AsTask().WaitAsync(Bound);
            Assert.NotEqual(0, got);
            read += got;
        }

        Assert.Equal(Header(Req0), header);
        return connection;
    }

    internal static async Task CompleteHandshake(Socket connection) =>
        await connection.SendAsync(Header(Rep0));

    internal static async Task RefuseHandshake(Socket connection) =>
        await connection.SendAsync(new byte[] { 0x00, (byte)'X', (byte)'X', 0x00, 0x00, Rep0, 0x00, 0x00 });

    /// <summary>How many bytes arrive before the client closes the connection.</summary>
    internal static async Task<int> ReadUntilClosed(Socket connection)
    {
        var total = 0;
        var buffer = new byte[256];
        while (true)
        {
            var got = await connection.ReceiveAsync(buffer.AsMemory()).AsTask().WaitAsync(Bound);
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
        _listener.Dispose();
        File.Delete(_path);
    }
}
