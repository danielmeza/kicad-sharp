using static KiCadSharp.KiCadEnvironment;

namespace KiCadSharp.Tests;

/// <summary>
/// <see cref="KiCadEnvironment.GetDefaultSocketPath()"/> against KiCad 10.0.6's own rule (#97):
/// <c>KICAD_API_SERVER::Start</c> puts the socket at <c>&lt;temp&gt;/kicad/api.sock</c>, with
/// <c>/tmp</c> as the temp directory on macOS and <c>wxFileName::GetTempDir()</c> everywhere else.
/// </summary>
/// <remarks>
/// Every case goes through the internal overload, which takes the platform, the environment, the
/// file system and the system temp path as arguments. So a test describes Windows and macOS on
/// any machine, and nothing here reads or changes the <c>TMPDIR</c> of the process running it,
/// which other tests use for their scratch directories.
/// </remarks>
public class DefaultSocketPathTests
{
    private sealed class Machine(SocketPlatform platform, string systemTemp = @"C:\Users\me\AppData\Local\Temp\")
    {
        public Dictionary<string, string?> Variables { get; } = [];

        public HashSet<string> Directories { get; } = platform == SocketPlatform.Windows ? [] : ["/tmp"];

        public string DefaultSocketPath() =>
            GetDefaultSocketPath(platform, name => Variables.GetValueOrDefault(name), Directories.Contains, () => systemTemp);
    }

    // ------------------------------------------------------------------------------------ Linux

    [Fact]
    public void Linux_NothingSet_IsTmp()
    {
        Assert.Equal("ipc:///tmp/kicad/api.sock", new Machine(SocketPlatform.Unix).DefaultSocketPath());
    }

    [Fact]
    public void Linux_Tmpdir_IsWhereKiCadPutsTheSocket()
    {
        // The #97 bug: this was ipc:///tmp/kicad/api.sock whatever TMPDIR said. Measured: pcbnew
        // 10.0.6 with TMPDIR=/tmp/alt listens on /tmp/alt/kicad/api.sock.
        var machine = new Machine(SocketPlatform.Unix);
        machine.Variables["TMPDIR"] = "/tmp/alt";
        machine.Directories.Add("/tmp/alt");

        Assert.Equal("ipc:///tmp/alt/kicad/api.sock", machine.DefaultSocketPath());
    }

    [Fact]
    public void Linux_TmpdirThenTmpThenTemp_TheFirstThatIsADirectory()
    {
        var machine = new Machine(SocketPlatform.Unix);
        machine.Variables["TMPDIR"] = "/missing/tmpdir";
        machine.Variables["TMP"] = "/missing/tmp";
        machine.Variables["TEMP"] = "/scratch/temp";
        machine.Directories.Add("/scratch/temp");

        Assert.Equal("ipc:///scratch/temp/kicad/api.sock", machine.DefaultSocketPath());

        machine.Directories.Add("/missing/tmp");
        Assert.Equal("ipc:///missing/tmp/kicad/api.sock", machine.DefaultSocketPath());

        machine.Directories.Add("/missing/tmpdir");
        Assert.Equal("ipc:///missing/tmpdir/kicad/api.sock", machine.DefaultSocketPath());
    }

    [Fact]
    public void Linux_AVariableNamingNoDirectory_IsSkipped_DownToTmp()
    {
        // wxFileName::GetTempDir checks each with DirExists; .NET's Path.GetTempPath does not.
        var machine = new Machine(SocketPlatform.Unix);
        machine.Variables["TMPDIR"] = "/does/not/exist";
        machine.Variables["TMP"] = "";

        Assert.Equal("ipc:///tmp/kicad/api.sock", machine.DefaultSocketPath());
    }

    [Theory]
    [InlineData("/tmp/alt/", "ipc:///tmp/alt/kicad/api.sock")]      // a trailing separator is dropped
    [InlineData("/tmp//alt", "ipc:///tmp/alt/kicad/api.sock")]      // so is an empty component
    [InlineData("//tmp//alt//", "ipc:///tmp/alt/kicad/api.sock")]
    [InlineData("/", "ipc:///kicad/api.sock")]                      // only separators: the root
    public void Linux_TheDirectory_IsWrittenAsWxFileNameWritesIt(string tmpdir, string expected)
    {
        var machine = new Machine(SocketPlatform.Unix);
        machine.Variables["TMPDIR"] = tmpdir;
        machine.Directories.Add(tmpdir);

        Assert.Equal(expected, machine.DefaultSocketPath());
    }

    [Fact]
    public void Linux_NoTempDirectoryAtAll_IsKiCadsWorkingDirectory()
    {
        // wx's last resort. It is relative to KiCad's working directory, not this process's, so it
        // is right only by accident -- but it is what KiCad does, and inventing another would not
        // be any more right.
        var machine = new Machine(SocketPlatform.Unix);
        machine.Directories.Clear();

        Assert.Equal("ipc://./kicad/api.sock", machine.DefaultSocketPath());
    }

    // ------------------------------------------------------------------------------------ macOS

    [Fact]
    public void MacOS_IsTmp_WhateverTmpdirSays()
    {
        // macOS sets TMPDIR for every user (/var/folders/…/T/), and KiCad ignores it there.
        var machine = new Machine(SocketPlatform.MacOS);
        machine.Variables["TMPDIR"] = "/var/folders/xy/abc123/T/";
        machine.Directories.Add("/var/folders/xy/abc123/T/");

        Assert.Equal("ipc:///tmp/kicad/api.sock", machine.DefaultSocketPath());
    }

    // ---------------------------------------------------------------------------------- Windows

    [Fact]
    public void Windows_TheSystemTempPath_WithOneBackslash()
    {
        // The #97 bug: Path.GetTempPath() ends in a backslash and the old code added another, giving
        // ...\Temp\\kicad\api.sock -- a different named pipe from KiCad's.
        var path = new Machine(SocketPlatform.Windows, systemTemp: @"C:\Users\me\AppData\Local\Temp\").DefaultSocketPath();

        Assert.Equal(@"ipc://C:\Users\me\AppData\Local\Temp\kicad\api.sock", path);
        Assert.DoesNotContain(@"\\", path);
    }

    [Fact]
    public void Windows_TheVariablesComeFirst_TmpdirIncluded()
    {
        // wx reads TMPDIR on Windows too, before TMP and TEMP, and only then asks GetTempPath.
        var machine = new Machine(SocketPlatform.Windows);
        machine.Variables["TMP"] = @"D:\tmp";
        machine.Directories.Add(@"D:\tmp");

        Assert.Equal(@"ipc://D:\tmp\kicad\api.sock", machine.DefaultSocketPath());

        machine.Variables["TMPDIR"] = @"E:\scratch\";
        machine.Directories.Add(@"E:\scratch\");

        Assert.Equal(@"ipc://E:\scratch\kicad\api.sock", machine.DefaultSocketPath());
    }

    [Theory]
    [InlineData("C:/msys64/tmp", @"ipc://C:\msys64\tmp\kicad\api.sock")]            // '/' separates too
    [InlineData(@"C:\Temp\\x\", @"ipc://C:\Temp\x\kicad\api.sock")]
    [InlineData(@"\\server\share\tmp\", @"ipc://\\server\share\tmp\kicad\api.sock")]  // UNC keeps its \\
    [InlineData(@"C:\", @"ipc://C:\kicad\api.sock")]
    public void Windows_TheDirectory_IsWrittenAsWxFileNameWritesIt(string temp, string expected)
    {
        var machine = new Machine(SocketPlatform.Windows);
        machine.Variables["TEMP"] = temp;
        machine.Directories.Add(temp);

        Assert.Equal(expected, machine.DefaultSocketPath());
    }

    // ------------------------------------------------------------------------------- every platform

    [Theory]
    [InlineData(nameof(SocketPlatform.Unix))]
    [InlineData(nameof(SocketPlatform.MacOS))]
    [InlineData(nameof(SocketPlatform.Windows))]
    public void KicadApiSocket_WinsWhenSet_AndIsReturnedAsItIs(string platform)
    {
        var machine = new Machine(Enum.Parse<SocketPlatform>(platform));
        machine.Variables["TMPDIR"] = "/tmp";
        machine.Variables["KICAD_API_SOCKET"] = "ipc:///somewhere/else/api-4242.sock";

        Assert.Equal("ipc:///somewhere/else/api-4242.sock", machine.DefaultSocketPath());

        machine.Variables["KICAD_API_SOCKET"] = "";
        Assert.EndsWith("api.sock", machine.DefaultSocketPath());
        Assert.DoesNotContain("somewhere", machine.DefaultSocketPath());
    }

    [Fact]
    public void ThePublicMethod_IsTheRuleOverThisProcess()
    {
        // The wiring: the real platform, environment, file system and temp path.
        var platform = OperatingSystem.IsWindows() ? SocketPlatform.Windows
            : OperatingSystem.IsMacOS() ? SocketPlatform.MacOS
            : SocketPlatform.Unix;

        Assert.Equal(
            GetDefaultSocketPath(platform, Environment.GetEnvironmentVariable, Directory.Exists, Path.GetTempPath),
            KiCadEnvironment.GetDefaultSocketPath());
    }
}
