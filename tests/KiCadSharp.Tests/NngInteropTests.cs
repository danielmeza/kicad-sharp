using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;

using KiCadSharp.Interop;

namespace KiCadSharp.Tests;

/// <summary>
/// The nng P/Invoke wrapper, without a KiCad.
/// </summary>
/// <remarks>
/// These are the tests that run everywhere. The IPC suite needs a live KiCad and returns early
/// without one, which would leave the transport with no gate at all on a machine that has none --
/// including CI.
/// </remarks>
public class NngInteropTests
{
    // Generous: these assert an order of magnitude, not a stopwatch reading.
    private static readonly TimeSpan Immediately = TimeSpan.FromSeconds(2);

    [Fact]
    public void TheNativeLibraryThisPackageShipsLoadsAndAnswers()
    {
        // Through Open(), not straight at Nng.Version(), so that a machine which cannot load libnng
        // fails with the message that names the missing dependency rather than with the loader's
        // bare "cannot open shared object file". This is the first test that touches nng, so it is
        // the one whose failure a reader sees first.
        using (NngRequestSocket.Open())
        {
        }

        // If this returns nonsense the marshalling is wrong. nng_strerror is the cheapest call that
        // proves the string really comes back out of the native library.
        Assert.False(string.IsNullOrWhiteSpace(Nng.Version()));
        Assert.Equal("Timed out", Nng.Describe(5));
        Assert.Equal("Object closed", Nng.Describe(Nng.Closed));
        Assert.Equal("Incorrect state", Nng.Describe(Nng.State));
    }

    [Fact]
    public void TheAdviceForAFailedLoadMatchesTheRunningPlatform()
    {
        // It used to name Linux's libatomic whatever platform it was running on, which is worse than
        // saying nothing at all on the two thirds of platforms where it is not true.
        var advice = NngLibraryResolver.NativeDependencyAdvice();

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("VCRUNTIME140.dll", advice);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Contains("libSystem.B.dylib", advice);
        }
        else
        {
            Assert.Contains("libatomic.so.1", advice);
        }

        Assert.Contains(NngLibraryResolver.NativeDependencyAdvice(), NngLibraryResolver.DescribeFailure());
    }

    [Fact]
    public void TheNativeLibraryIsShippedForThisPlatform()
    {
        // The package carries libnng under runtimes/<rid>/native, which is a NuGet convention and
        // not a loader one -- see NngLibraryResolver. This asserts the file is actually there for
        // the platform running the tests, which is what a project reference relies on.
        var identifier = NngLibraryResolver.PortableRuntimeIdentifier();
        Assert.Contains(identifier, NngLibraryResolver.ShippedRuntimeIdentifiers);

        var expected = Path.Combine(AppContext.BaseDirectory, "runtimes", identifier, "native", NngLibraryResolver.FileName);
        Assert.True(File.Exists(expected), $"{expected} is missing; nng would only load if the platform happened to have one.");
        Assert.Contains(expected, NngLibraryResolver.ProbePaths(typeof(Nng).Assembly));
    }

    [Fact]
    public void EveryShippedRuntimeIdentifierHasItsLibrary()
    {
        // Eight runtime identifiers: the six nng.NET publishes, and osx-arm64 and win-arm64, built
        // from nng's source under native/. A platform silently losing its binary is one failure; the
        // other is a binary that ships while the resolver still tells that platform there is none,
        // which is what issue #81 found in the docs. So the list and the build's runtimes/ folders
        // must be the same set, and each folder must hold the file nng has on that platform.
        var runtimes = Path.Combine(AppContext.BaseDirectory, "runtimes");
        var built = Directory.GetDirectories(runtimes)
            .Where(directory => Directory.Exists(Path.Combine(directory, "native")))
            .Select(directory => Path.GetFileName(directory))
            .Order(StringComparer.Ordinal);

        Assert.Equal(NngLibraryResolver.ShippedRuntimeIdentifiers.Order(StringComparer.Ordinal), built);

        foreach (var identifier in NngLibraryResolver.ShippedRuntimeIdentifiers)
        {
            var file = identifier.StartsWith("win-", StringComparison.Ordinal) ? "nng.dll"
                : identifier.StartsWith("osx-", StringComparison.Ordinal) ? "libnng.dylib"
                : "libnng.so";
            Assert.True(File.Exists(Path.Combine(runtimes, identifier, "native", file)), $"no {file} for {identifier}");
        }
    }

    [Fact]
    public void DialingAPathThatIsNotThereFailsAtOnce()
    {
        // Through the factory, so its failure path is the one exercised. That the socket it opened is
        // also closed on the way out is *not* asserted here, and deliberately not: measured on nng
        // 1.3.2, leaking 100,000 of them moves no file descriptor (39 -> 39) and no managed byte, and
        // socket ids increment monotonically rather than being reused, so there is nothing a test can
        // read. A test that passes whether or not the cleanup is there is worse than no test, because
        // it claims cover it does not have.
        var elapsed = Stopwatch.StartNew();

        var failure = Assert.Throws<NngException>(() => NngRequestSocket.Dial(
            $"ipc://{Path.Combine(Path.GetTempPath(), $"kicadsharp-absent-{Guid.NewGuid():N}.sock")}",
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan));

        Assert.Equal("nng_dial", failure.Operation);
        Assert.Contains("Connection refused", failure.Message);
        Assert.True(elapsed.Elapsed < Immediately, $"took {elapsed.Elapsed}");
    }

    [Fact]
    public void DialingASocketThatIsBoundButSilentIsBoundedByNng()
    {
        // The trap this pins: a socket that exists and never completes nng's handshake -- a KiCad
        // that has opened its API socket and is not serving yet -- is not refused and is not
        // accepted. It costs nng's own dial timeout, measured at 10.0 s, every attempt. Anything
        // waiting for KiCad to come up has to count seconds rather than attempts, or three
        // "retries" become half a minute of apparent hang.
        var path = Path.Combine(Path.GetTempPath(), $"kicadsharp-silent-{Guid.NewGuid():N}.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(8);

        try
        {
            using var socket = NngRequestSocket.Open();
            var elapsed = Stopwatch.StartNew();

            var failure = Assert.Throws<NngException>(() => socket.Dial($"ipc://{path}"));

            Assert.Contains("Timed out", failure.Message);
            Assert.InRange(elapsed.Elapsed, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ARequestAndItsReplyRoundTripThroughTheWrapper()
    {
        using var peer = NngTestPeer.Start(TimeSpan.Zero);

        // The factory: on the way out it hands back an open socket the caller owns. Being able to
        // send on it afterwards is what says so. The first attempt sends: a dial that returns has
        // already put its connection on the ready list (nni_dialer_add_pipe starts the pipe before the
        // dial completes, in nng 1.3.2).
        using var socket = NngRequestSocket.Dial(peer.Url, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        Assert.True(socket.TrySend([1, 2, 3, 4]));

        var payload = Poll(socket, TimeSpan.FromSeconds(10));
        Assert.NotEmpty(payload);
        Assert.Equal(1, peer.RequestsReceived);
    }

    [Fact]
    public void ReceivingDoesNotBlockWhileTheReplyIsStillComing()
    {
        // TryReceive is the reason this client can be cancelled at all: it answers "not yet" instead
        // of parking in nng until the reply lands.
        using var peer = NngTestPeer.Start(TimeSpan.FromSeconds(2));
        using var socket = NngRequestSocket.Open();
        socket.Dial(peer.Url);
        Assert.True(socket.TrySend([1]));

        var elapsed = Stopwatch.StartNew();
        Assert.False(socket.TryReceive(out _));
        Assert.True(elapsed.Elapsed < TimeSpan.FromMilliseconds(500), $"TryReceive blocked for {elapsed.Elapsed}");
    }

    [Fact]
    public void SendingDoesNotBlockWhenNobodyIsThereToTakeIt()
    {
        // TrySend is the send's half of the same idea (#58). A REQ socket with no connection has
        // nobody to hand the request to, which after the dial is a KiCad that went away, and a
        // blocking nng_sendmsg waits for one for send-timeout -- by default, forever. A socket that is
        // open and not dialled is that case with no race in it: nothing can connect behind the test's
        // back, and send-timeout is nng's own infinite default.
        using var socket = NngRequestSocket.Open();

        var elapsed = Stopwatch.StartNew();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            Assert.False(socket.TrySend([0xAA]));
        }

        Assert.True(elapsed.Elapsed < Immediately, $"100 attempts took {elapsed.Elapsed}");

        // "Not now" sent nothing and queued nothing. Once there is a peer, the next request is the
        // only one it sees -- one of the refused ones, queued, would have gone out first when the
        // connection came up -- and its reply comes back to this socket.
        using var peer = NngTestPeer.Start(TimeSpan.Zero);
        socket.Dial(peer.Url);
        Assert.True(socket.TrySend([0xBB]));

        Assert.NotEmpty(Poll(socket, TimeSpan.FromSeconds(10)));
        Assert.Equal(1, peer.RequestsReceived);
    }

    [Fact]
    public void AnAbandonedRequestDoesNotLeakItsReplyIntoTheNextOne()
    {
        // Giving up on a request leaves it outstanding, and its reply still arrives. This asserts
        // nng discards it: REP tags each request and the reply is matched to the tag, so the next
        // request gets its own answer rather than the previous one's.
        using var peer = NngTestPeer.Start(TimeSpan.FromMilliseconds(600));
        using var socket = NngRequestSocket.Open();
        socket.Dial(peer.Url);

        Assert.True(socket.TrySend([0xAA]));
        Assert.False(socket.TryReceive(out _));         // given up on here

        Thread.Sleep(TimeSpan.FromSeconds(1.5));        // the abandoned reply lands during this

        Assert.True(socket.TrySend([0xBB]));
        var payload = Poll(socket, TimeSpan.FromSeconds(10));

        Assert.NotEmpty(payload);
        Assert.Equal(2, peer.RequestsReceived);
    }

    [Fact]
    public void EachPlaceIsProbedAndReportedOnce()
    {
        // #84. The two roots are the application directory and the directory of the assembly, and in
        // the usual layout, this one included, they are the same directory spelled two ways:
        // AppContext.BaseDirectory ends in a separator and Path.GetDirectoryName does not. Compared
        // as strings they differed, so the same runtimes/ path was probed twice and listed twice
        // under "Looked in:".
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
            Path.GetDirectoryName(typeof(Nng).Assembly.Location));

        var probed = NngLibraryResolver.ProbePaths(typeof(Nng).Assembly).ToArray();
        Assert.Equal(probed.Distinct(StringComparer.Ordinal), probed);

        var reported = NngLibraryResolver.DiagnosticPaths().ToArray();
        Assert.Equal(reported.Distinct(StringComparer.Ordinal), reported);

        // Only the list: here the shipped file is present, so the message also names it once before
        // the list, as the one the loader refused.
        const string LookedIn = "Looked in: ";
        var message = NngLibraryResolver.DescribeFailure();
        Assert.Contains(LookedIn, message);
        var list = message[(message.IndexOf(LookedIn, StringComparison.Ordinal) + LookedIn.Length)..];
        foreach (var path in reported)
        {
            var times = list.Split(path).Length - 1;
            Assert.True(times == 1, $"{path} is listed {times} times in: {list}");
        }
    }

    private static byte[] Poll(NngRequestSocket socket, TimeSpan within)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < within)
        {
            if (socket.TryReceive(out var payload))
            {
                return payload;
            }

            Thread.Sleep(5);
        }

        throw new TimeoutException($"no reply within {within}");
    }
}
