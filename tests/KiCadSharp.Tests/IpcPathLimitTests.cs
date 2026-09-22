using KiCadSharp.Interop;

using static KiCadSharp.KiCadEnvironment;

namespace KiCadSharp.Tests;

/// <summary>
/// <see cref="IpcPathLimit"/>: the longest <c>ipc://</c> path each platform dials, and what is said
/// about a longer one (#108). Every case names its platform, so all three are pinned on any machine;
/// the measurement behind the numbers is <c>NngInteropTests.APathOneByteOverThePlatformsLimitIsAddressInvalidToNng</c>.
/// </summary>
public class IpcPathLimitTests
{
    // The enum is internal, and a theory's parameters are public: the cases name the platform.
    private static SocketPlatform Platform(string name) =>
        name switch
        {
            "Linux" => SocketPlatform.Unix,
            "macOS" => SocketPlatform.MacOS,
            "Windows" => SocketPlatform.Windows,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };

    [Theory]
    [InlineData("Linux", 107)]
    [InlineData("macOS", 103)]
    [InlineData("Windows", 127)]
    public void TheLimitIsSunPathOrMaxAddrLenLessTheNul(string platform, int limit)
    {
        Assert.Equal(limit, IpcPathLimit.For(Platform(platform)));
    }

    [Theory]
    [InlineData("Linux")]
    [InlineData("macOS")]
    [InlineData("Windows")]
    public void APathAtTheLimitFits(string platform)
    {
        Assert.Null(IpcPathLimit.TooLong("ipc://" + new string('p', IpcPathLimit.For(Platform(platform))), Platform(platform)));
    }

    [Theory]
    [InlineData("Linux", "sun_path is 108 bytes")]
    [InlineData("macOS", "sun_path is 104 bytes")]
    [InlineData("Windows", "NNG_MAXADDRLEN is 128")]
    public void APathOneByteOverNamesItsLengthTheLimitAndThePlatform(string name, string reason)
    {
        var platform = Platform(name);
        var limit = IpcPathLimit.For(platform);

        var message = IpcPathLimit.TooLong("ipc://" + new string('p', limit + 1), platform);

        Assert.NotNull(message);
        Assert.Contains($"is {limit + 1} bytes long", message);
        Assert.Contains($"{name} takes at most {limit} bytes", message);
        Assert.Contains(reason, message);
        Assert.Contains("KiCadClientSettings.PipeName", message);
    }

    [Fact]
    public void TheSchemeIsNotCounted()
    {
        // nng takes everything after ipc:// as the path, and the limit is on the path alone. A
        // count that included the six characters of the scheme would refuse the last six paths
        // that dial.
        var path = new string('p', IpcPathLimit.Linux);

        Assert.Null(IpcPathLimit.TooLong("ipc://" + path, SocketPlatform.Unix));
        Assert.NotNull(IpcPathLimit.TooLong("ipc://" + path + "p", SocketPlatform.Unix));
    }

    [Fact]
    public void BytesAreCountedNotCharacters()
    {
        // sun_path holds bytes. 54 of 'é' are 54 characters and 108 bytes of UTF-8, one over Linux.
        var message = IpcPathLimit.TooLong("ipc://" + new string('é', 54), SocketPlatform.Unix);

        Assert.NotNull(message);
        Assert.Contains("is 108 bytes long", message);
    }

    [Fact]
    public void AUrlOfAnotherSchemeIsLeftToNng()
    {
        // The limit is the ipc transport's. Whatever else a caller configures, nng answers for it.
        Assert.Null(IpcPathLimit.TooLong("tcp://" + new string('p', 200), SocketPlatform.Unix));
        Assert.Null(IpcPathLimit.TooLong("IPC://" + new string('p', 200), SocketPlatform.Unix));
    }

    [Fact]
    public void ThisPlatformsLimitIsTheOneForItsSocketPlatform()
    {
        Assert.Equal(IpcPathLimit.For(CurrentSocketPlatform), IpcPathLimit.ForThisPlatform);
        Assert.Equal(
            OperatingSystem.IsWindows() ? IpcPathLimit.Windows : OperatingSystem.IsMacOS() ? IpcPathLimit.MacOS : IpcPathLimit.Linux,
            IpcPathLimit.ForThisPlatform);
    }
}
