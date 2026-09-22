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
/// which other tests use for their scratch directories. Which socket files exist is described the
/// same way, so no test depends on a KiCad, native or Flatpak, having run on the machine.
/// </remarks>
public class DefaultSocketPathTests
{
    private sealed class Machine(SocketPlatform platform, string systemTemp = @"C:\Users\me\AppData\Local\Temp\")
    {
        public Dictionary<string, string?> Variables { get; } = [];

        public HashSet<string> Directories { get; } = platform == SocketPlatform.Windows ? [] : ["/tmp"];

        /// <summary>The socket files that exist, by path. None does until a test says so.</summary>
        public HashSet<string> Files { get; } = [];

        /// <summary>Every path the rule asked <see cref="Files"/> about, in the order it asked.</summary>
        public List<string> FilesAsked { get; } = [];

        /// <summary>The home directory, as <c>Environment.GetFolderPath(UserProfile)</c> would give it.</summary>
        public string? Home { get; set; } = platform == SocketPlatform.Windows ? @"C:\Users\me" : "/home/me";

        public string DefaultSocketPath() =>
            GetDefaultSocketPath(
                platform,
                name => Variables.GetValueOrDefault(name),
                Directories.Contains,
                () => systemTemp,
                path =>
                {
                    FilesAsked.Add(path);
                    return Files.Contains(path);
                },
                () => Home);
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

    // ------------------------------------------------ KiCad from Flathub, outside its sandbox (#110)
    //
    // MEASURED against org.kicad.KiCad 10.0.6 from Flathub (flatpak 1.14.6). The manifest starts
    // KiCad with --env=TMPDIR=/var/tmp, and flatpak binds the sandbox's /var/tmp to
    // ~/.var/app/org.kicad.KiCad/cache/tmp on the host. So pcbnew listens on
    // /var/tmp/kicad/api.sock inside, which is ~/.var/app/org.kicad.KiCad/cache/tmp/kicad/api.sock
    // outside, and a host process connects to that socket as it is, with no flatpak override. A
    // client on the host with no TMPDIR used to dial ipc:///tmp/kicad/api.sock, where nothing
    // listens: "Connection refused".

    private const string FlathubSocket = "/home/me/.var/app/org.kicad.KiCad/cache/tmp/kicad/api.sock";

    [Fact]
    public void Linux_NoSocketWhereKiCadsRuleSays_ButOneInTheFlathubSandbox_IsTheFlathubOne()
    {
        var machine = new Machine(SocketPlatform.Unix);
        machine.Files.Add(FlathubSocket);

        Assert.Equal("ipc://" + FlathubSocket, machine.DefaultSocketPath());
    }

    [Fact]
    public void Linux_ASocketWhereKiCadsRuleSays_WinsOverTheFlathubOne()
    {
        // kipy looks in the Flathub directory first. Here KiCad's own rule comes first: a native
        // KiCad that is listening is not passed over for a socket file the Flatpak left behind.
        var machine = new Machine(SocketPlatform.Unix);
        machine.Files.Add("/tmp/kicad/api.sock");
        machine.Files.Add(FlathubSocket);

        Assert.Equal("ipc:///tmp/kicad/api.sock", machine.DefaultSocketPath());
    }

    [Fact]
    public void Linux_TheFlathubSocket_IsLookedForAfterTmpdirWasHonoured()
    {
        var machine = new Machine(SocketPlatform.Unix);
        machine.Variables["TMPDIR"] = "/tmp/alt";
        machine.Directories.Add("/tmp/alt");
        machine.Files.Add(FlathubSocket);

        Assert.Equal("ipc://" + FlathubSocket, machine.DefaultSocketPath());

        machine.Files.Add("/tmp/alt/kicad/api.sock");
        Assert.Equal("ipc:///tmp/alt/kicad/api.sock", machine.DefaultSocketPath());
    }

    [Fact]
    public void Linux_NoSocketAnywhere_IsStillTheAddressKiCadsRuleGives()
    {
        // No KiCad has started yet, or a native one is about to: the address is the one it will
        // listen on, as before. Both places were looked at, KiCad's own first.
        var machine = new Machine(SocketPlatform.Unix);

        Assert.Equal("ipc:///tmp/kicad/api.sock", machine.DefaultSocketPath());
        Assert.Equal(new[] { "/tmp/kicad/api.sock", FlathubSocket }, machine.FilesAsked);
    }

    [Theory]
    [InlineData("/var/home/alice", "ipc:///var/home/alice/.var/app/org.kicad.KiCad/cache/tmp/kicad/api.sock")]  // Silverblue
    [InlineData("/home/alice/", "ipc:///home/alice/.var/app/org.kicad.KiCad/cache/tmp/kicad/api.sock")]          // a trailing separator
    public void Linux_TheFlathubDirectory_IsUnderTheHomeDirectory(string home, string expected)
    {
        // flatpak_get_data_dir is g_get_home_dir() + ".var/app/" + the app id: the home directory
        // as $HOME names it, not a fixed /home/<user>.
        var machine = new Machine(SocketPlatform.Unix) { Home = home };
        machine.Files.Add(expected["ipc://".Length..]);

        Assert.Equal(expected, machine.DefaultSocketPath());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Linux_NoHomeDirectory_LeavesTheFlathubSocketUnlookedFor(string? home)
    {
        var machine = new Machine(SocketPlatform.Unix) { Home = home };
        machine.Files.Add("/.var/app/org.kicad.KiCad/cache/tmp/kicad/api.sock");

        Assert.Equal("ipc:///tmp/kicad/api.sock", machine.DefaultSocketPath());
        Assert.Equal(new[] { "/tmp/kicad/api.sock" }, machine.FilesAsked);
    }

    [Theory]
    [InlineData(nameof(SocketPlatform.MacOS))]
    [InlineData(nameof(SocketPlatform.Windows))]
    public void NotLinux_NeverLooksForAFlathubSocket(string platform)
    {
        // Flatpak is Linux only. macOS and Windows are the rule as before, and no file is consulted.
        var machine = new Machine(Enum.Parse<SocketPlatform>(platform));
        machine.Files.Add(machine.Home + "/.var/app/org.kicad.KiCad/cache/tmp/kicad/api.sock");

        var path = machine.DefaultSocketPath();

        Assert.Empty(machine.FilesAsked);
        Assert.DoesNotContain(".var", path);
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
        machine.Files.Add(FlathubSocket);

        Assert.Equal("ipc:///somewhere/else/api-4242.sock", machine.DefaultSocketPath());
        Assert.Empty(machine.FilesAsked);

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
            GetDefaultSocketPath(
                platform,
                Environment.GetEnvironmentVariable,
                Directory.Exists,
                Path.GetTempPath,
                File.Exists,
                () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            KiCadEnvironment.GetDefaultSocketPath());
    }
}
