using System.Text;

namespace KiCadSharp.Tests;

/// <summary>
/// <see cref="SocketPaths"/>: every path it hands out fits in a socket address, whatever
/// <c>TMPDIR</c> is (#80).
/// </summary>
public class SocketPathsTests
{
    [Fact]
    public void ATempPathTooLongForASocketFallsBackToTmp()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // No /tmp, and nng's ipc:// there is a named pipe.
        }

        Assert.Equal("/tmp", SocketPaths.RootFor("/" + new string('t', 118) + "/"));
    }

    [Fact]
    public void AShortTempPathIsKept()
    {
        var tempPath = OperatingSystem.IsWindows() ? @"C:\t\" : "/t/";

        Assert.Equal(tempPath, SocketPaths.RootFor(tempPath));
    }

    [Fact]
    public void EveryPathFitsAndIsNew()
    {
        var first = SocketPaths.New("silent");
        var second = SocketPaths.New("silent");

        Assert.NotEqual(first, second);
        Assert.InRange(Encoding.UTF8.GetByteCount(first), 1, OperatingSystem.IsWindows() ? SocketPaths.WindowsLimit : SocketPaths.UnixLimit);
        Assert.False(File.Exists(first));
        Assert.StartsWith("ipc://", SocketPaths.NewUrl("absent"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDirectoryIsTheRunnersAlone()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix file modes.
        }

        var directory = Path.GetDirectoryName(SocketPaths.New("peer"))!;

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
    }
}
