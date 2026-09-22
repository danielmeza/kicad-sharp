using System.Collections;
using System.Globalization;

namespace KiCadSharp
{
    /// <summary>
    /// When KiCad launches API plugins (see below), it will set several environment variables that can be used by the client to know where to connect to the IPC API.
    /// https://dev-docs.kicad.org/en/apis-and-binding/ipc-api/for-addon-developers/index.html#_connecting_to_kicad
    /// </summary>
    /// <remarks>
    /// A plugin also inherits KiCad's own environment, which carries the library paths under names
    /// that include KiCad's major version (<c>KICAD10_SYMBOL_DIR</c> on KiCad 10). Their getters find
    /// the version that is there; see <see cref="GetVersionedVariable(string)"/>.
    /// </remarks>
    public static class KiCadEnvironment
    {
        /// <summary>
        /// The full path to the socket or pipe that the client should connect to.
        /// </summary>
        /// <returns>Socket or pipe path from environment, or null if not set</returns>
        public static string? GetApiSocket()
        {
            return Environment.GetEnvironmentVariable("KICAD_API_SOCKET");
        }

        /// <summary>
        /// A token that uniquely identifies a KiCad instance
        /// </summary>
        /// <returns>API token from environment, or null if not set</returns>
        public static string? GetApiToken()
        {
            return Environment.GetEnvironmentVariable("KICAD_API_TOKEN");
        }

        public static string? GetProjectDirectory()
        {
            return Environment.GetEnvironmentVariable("KIPRJMOD");
        }

        public static string? GetUserTemplateDirectory()
        {
            return Environment.GetEnvironmentVariable("KICAD_USER_TEMPLATE_DIR");
        }

        // ---------------------------------------------------------------- versioned path variables
        //
        // KiCad names its library paths after the major version it is running: KICAD10_SYMBOL_DIR
        // on KiCad 10, KICAD9_SYMBOL_DIR on KiCad 9. These getters used to read the KICAD9_* names
        // only, so in a plugin KiCad 10 launched they returned null, or a value left over from
        // KiCad 9 (#82). They now go through GetVersionedVariable, which finds the version there.

        /// <summary>
        /// The base path of KiCad's stock 3D models (<c>.3dshapes</c> folders):
        /// <c>KICAD<i>n</i>_3DMODEL_DIR</c>, such as <c>KICAD10_3DMODEL_DIR</c>.
        /// </summary>
        /// <returns>The path, or <see langword="null"/> when no version of the variable is set.</returns>
        /// <remarks>The version is chosen as <see cref="GetVersionedVariable(string)"/> describes.</remarks>
        public static string? GetModelsDirectory() => GetVersionedVariable("3DMODEL_DIR");

        /// <summary>
        /// The base path of KiCad's stock footprint libraries (<c>.pretty</c> folders):
        /// <c>KICAD<i>n</i>_FOOTPRINT_DIR</c>, such as <c>KICAD10_FOOTPRINT_DIR</c>.
        /// </summary>
        /// <returns>The path, or <see langword="null"/> when no version of the variable is set.</returns>
        /// <remarks>The version is chosen as <see cref="GetVersionedVariable(string)"/> describes.</remarks>
        public static string? GetFootprintDirectory() => GetVersionedVariable("FOOTPRINT_DIR");

        /// <summary>
        /// The base path of KiCad's stock symbol libraries:
        /// <c>KICAD<i>n</i>_SYMBOL_DIR</c>, such as <c>KICAD10_SYMBOL_DIR</c>.
        /// </summary>
        /// <returns>The path, or <see langword="null"/> when no version of the variable is set.</returns>
        /// <remarks>The version is chosen as <see cref="GetVersionedVariable(string)"/> describes.</remarks>
        public static string? GetSymbolDirectory() => GetVersionedVariable("SYMBOL_DIR");

        /// <summary>
        /// The base path of KiCad's stock design block libraries:
        /// <c>KICAD<i>n</i>_DESIGN_BLOCK_DIR</c>, such as <c>KICAD10_DESIGN_BLOCK_DIR</c>.
        /// </summary>
        /// <returns>The path, or <see langword="null"/> when no version of the variable is set.</returns>
        /// <remarks>The version is chosen as <see cref="GetVersionedVariable(string)"/> describes.</remarks>
        public static string? GetDesignBlockDirectory() => GetVersionedVariable("DESIGN_BLOCK_DIR");

        /// <summary>
        /// The directory of project templates installed with KiCad:
        /// <c>KICAD<i>n</i>_TEMPLATE_DIR</c>, such as <c>KICAD10_TEMPLATE_DIR</c>. The user's own
        /// templates are <see cref="GetUserTemplateDirectory"/>.
        /// </summary>
        /// <returns>The path, or <see langword="null"/> when no version of the variable is set.</returns>
        /// <remarks>The version is chosen as <see cref="GetVersionedVariable(string)"/> describes.</remarks>
        public static string? GetTemplateDirectory() => GetVersionedVariable("TEMPLATE_DIR");

        /// <summary>
        /// The directory the Plugin and Content Manager installs plugins, libraries and other
        /// downloaded content into: <c>KICAD<i>n</i>_3RD_PARTY</c>, such as <c>KICAD10_3RD_PARTY</c>.
        /// </summary>
        /// <returns>The path, or <see langword="null"/> when no version of the variable is set.</returns>
        /// <remarks>The version is chosen as <see cref="GetVersionedVariable(string)"/> describes.</remarks>
        public static string? GetThirdPartyDirectory() => GetVersionedVariable("3RD_PARTY");

        /// <summary>
        /// Reads one of KiCad's versioned variables, <c>KICAD<i>n</i>_<paramref name="baseName"/></c>,
        /// for the newest version <i>n</i> that is set.
        /// </summary>
        /// <param name="baseName">
        /// What follows the version in the name. KiCad 10 sets <c>SYMBOL_DIR</c>, <c>FOOTPRINT_DIR</c>,
        /// <c>3DMODEL_DIR</c>, <c>TEMPLATE_DIR</c>, <c>DESIGN_BLOCK_DIR</c> and <c>3RD_PARTY</c>.
        /// </param>
        /// <returns>The value, or <see langword="null"/> when no version of the variable is set.</returns>
        /// <exception cref="ArgumentException"><paramref name="baseName"/> is null or empty.</exception>
        /// <remarks>
        /// <para>
        /// KiCad builds these names from the major version it is running, as <c>"KICAD%d_%s"</c>
        /// (<c>ENV_VAR::GetVersionedEnvVarName</c> in KiCad 10.0.6's <c>common/env_vars.cpp</c>), and
        /// writes them into its own environment at startup, which a plugin it launches inherits.
        /// KiCad 10 sets <c>KICAD10_*</c>, KiCad 9 set <c>KICAD9_*</c>, and a 10.99 nightly still sets
        /// <c>KICAD10_*</c>. So no one name is right for every KiCad.
        /// </para>
        /// <para>
        /// The newest version set is the running KiCad's. KiCad defines every one of these for its
        /// own version, and none for any other. Another version's name reaches a plugin only when
        /// something else put it there: the user's own environment, or a value carried over in
        /// <c>kicad_common.json</c> from the previous version's settings, which KiCad 10 exports as
        /// well. In practice that name is an older one. Measured: KiCad 10.0.6 hands a plugin such a
        /// <c>KICAD9_SYMBOL_DIR</c> next to its own <c>KICAD10_SYMBOL_DIR</c>.
        /// </para>
        /// <para>The lookup:</para>
        /// <list type="number">
        /// <item><description>
        /// Every variable named <c>KICAD</c>, then decimal digits, then <c>_</c> and
        /// <paramref name="baseName"/> is a candidate, as KiCad's own <c>ENV_VAR::IsVersionedEnvVar</c>
        /// accepts them. Anything else is not: the unversioned names of KiCad 5
        /// (<c>KICAD_SYMBOL_DIR</c>, <c>KISYSMOD</c>, <c>KISYS3DMOD</c>) are not read.
        /// </description></item>
        /// <item><description>
        /// A candidate whose value is empty counts as unset, as it does in
        /// <c>COMMON_SETTINGS::InitializeEnvironment</c>.
        /// </description></item>
        /// <item><description>
        /// Of the rest, the highest version wins, compared as numbers, so <c>KICAD10</c> beats
        /// <c>KICAD9</c>. When no candidate is left, the result is <see langword="null"/>.
        /// </description></item>
        /// </list>
        /// <para>
        /// On Windows a name matches regardless of case, because Windows' environment does. Elsewhere
        /// it must match exactly.
        /// </para>
        /// <para>
        /// When the running version is known, for instance from <see cref="KiCad.GetVersion"/>, pass it
        /// to <see cref="GetVersionedVariable(string, uint)"/> instead.
        /// </para>
        /// </remarks>
        public static string? GetVersionedVariable(string baseName)
        {
            return GetVersionedVariable(baseName, null, ProcessEnvironment(), ProcessNameComparison);
        }

        /// <summary>
        /// Reads <c>KICAD<i>n</i>_<paramref name="baseName"/></c> for a known KiCad major version, and
        /// otherwise the newest version that is set.
        /// </summary>
        /// <param name="baseName">What follows the version in the name, as for <see cref="GetVersionedVariable(string)"/>.</param>
        /// <param name="majorVersion">
        /// The major version of the running KiCad, such as <see cref="KiCadVersion.Major"/> from
        /// <see cref="KiCad.GetVersion"/>. A 10.99 nightly is major version 10.
        /// </param>
        /// <returns>The value, or <see langword="null"/> when no version of the variable is set.</returns>
        /// <exception cref="ArgumentException"><paramref name="baseName"/> is null or empty.</exception>
        /// <remarks>
        /// <c>KICAD</c><paramref name="majorVersion"/><c>_</c><paramref name="baseName"/> is read first.
        /// When it is not set, or is empty, this falls back to <see cref="GetVersionedVariable(string)"/>,
        /// which takes the newest version set. That is KiCad's own rule
        /// (<c>ENV_VAR::GetVersionedEnvVarValue</c>): the exact name, then any versioned one.
        /// KiCad's fallback takes whichever name sorts first; this one takes the newest version.
        /// </remarks>
        public static string? GetVersionedVariable(string baseName, uint majorVersion)
        {
            return GetVersionedVariable(baseName, majorVersion, ProcessEnvironment(), ProcessNameComparison);
        }

        /// <summary>
        /// The lookup behind both public overloads, over an environment the caller supplies, so a test
        /// can describe one without touching the process's.
        /// </summary>
        internal static string? GetVersionedVariable(
            string baseName,
            uint? majorVersion,
            IEnumerable<KeyValuePair<string, string?>> environment,
            StringComparison nameComparison)
        {
            ArgumentException.ThrowIfNullOrEmpty(baseName);
            ArgumentNullException.ThrowIfNull(environment);

            // The known version first, then the newest. A tie, which only a name like KICAD010_* can
            // produce, goes to the name that sorts first, so the result does not depend on the order
            // the environment happens to be enumerated in.
            return environment
                .Where(entry => !string.IsNullOrEmpty(entry.Value))
                .Select(entry => (entry.Key, entry.Value, Version: ParseVersion(entry.Key, baseName, nameComparison)))
                .Where(candidate => candidate.Version is not null)
                .OrderByDescending(candidate => majorVersion is not null && candidate.Version == majorVersion)
                .ThenByDescending(candidate => candidate.Version)
                .ThenBy(candidate => candidate.Key, StringComparer.Ordinal)
                .Select(candidate => candidate.Value)
                .FirstOrDefault();
        }

        /// <summary>
        /// The version in <c>KICAD<i>n</i>_<paramref name="baseName"/></c>, or <see langword="null"/>
        /// when <paramref name="name"/> is not that. Mirrors <c>ENV_VAR::IsVersionedEnvVar</c>.
        /// </summary>
        private static uint? ParseVersion(string name, string baseName, StringComparison comparison)
        {
            const string prefix = "KICAD";

            // At least one digit between the prefix and "_<baseName>".
            if (name.Length < prefix.Length + 1 + 1 + baseName.Length
                || !name.StartsWith(prefix, comparison)
                || !name.EndsWith(baseName, comparison)
                || name[name.Length - baseName.Length - 1] != '_')
            {
                return null;
            }

            var digits = name.AsSpan(prefix.Length, name.Length - prefix.Length - baseName.Length - 1);

            foreach (var c in digits)
            {
                if (!char.IsAsciiDigit(c))
                {
                    return null;
                }
            }

            // NumberStyles.None: digits only, no sign or spaces. A version too large for a uint is
            // not one KiCad could have written, and is skipped rather than wrapped.
            return uint.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
                ? version
                : null;
        }

        private static StringComparison ProcessNameComparison =>
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private static IEnumerable<KeyValuePair<string, string?>> ProcessEnvironment()
        {
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                yield return new KeyValuePair<string, string?>((string)entry.Key, entry.Value as string);
            }
        }

        public static string? GetPythonVirtualEnvironment()
        {
            return Environment.GetEnvironmentVariable("VIRTUAL_ENV");
        }


        /// <summary>
        /// The address of KiCad's API server: <c>KICAD_API_SOCKET</c> when it is set, and otherwise
        /// the address KiCad 10 listens on, worked out the way KiCad works it out.
        /// </summary>
        /// <returns>An <c>ipc://</c> address, such as <c>ipc:///tmp/kicad/api.sock</c>.</returns>
        /// <remarks>
        /// <para>
        /// KiCad sets <c>KICAD_API_SOCKET</c> only for a plugin it launches. Any other client (a
        /// test, a CLI, an application started by hand) needs the path KiCad chose, which
        /// <c>KICAD_API_SERVER::Start</c> builds as <c>&lt;temp&gt;/kicad/api.sock</c>
        /// (<c>common/api/api_server.cpp</c>, lines 79-86, in KiCad 10.0.6). <c>&lt;temp&gt;</c> is:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// <b>macOS:</b> <c>/tmp</c>, always. KiCad ignores <c>TMPDIR</c> there, which macOS sets
        /// for every user.
        /// </description></item>
        /// <item><description>
        /// <b>Linux and other Unix systems:</b> <c>wxStandardPaths::GetTempDir()</c>, which is
        /// <c>wxFileName::GetTempDir()</c>. That takes the first of <c>TMPDIR</c>, <c>TMP</c> and
        /// <c>TEMP</c> that names an existing directory, then <c>/tmp</c>. A variable naming a
        /// directory that does not exist is skipped.
        /// </description></item>
        /// <item><description>
        /// <b>Windows:</b> the same three variables first, then the Win32 <c>GetTempPath</c>.
        /// <see cref="Path.GetTempPath"/> calls <c>GetTempPath2</c> where it exists, which answers
        /// the same for every process not running as SYSTEM. <c>ipc://</c> on Windows is a named pipe, not a file:
        /// nng opens <c>\\.\pipe\</c> followed by the path, so the path has to match KiCad's
        /// character for character.
        /// </description></item>
        /// </list>
        /// <para>
        /// The directory is then written the way <c>wxFileName</c> writes it: trailing and repeated
        /// separators dropped, and on Windows every <c>/</c> written as <c>\</c>. So a
        /// <c>TMPDIR</c> of <c>/tmp//x/</c> gives <c>ipc:///tmp/x/kicad/api.sock</c>, and a Windows
        /// temp path of <c>C:\Users\me\AppData\Local\Temp\</c> gives
        /// <c>ipc://C:\Users\me\AppData\Local\Temp\kicad\api.sock</c>.
        /// </para>
        /// <para>
        /// This is the first KiCad's address. A second KiCad started while the first holds the
        /// socket listens on <c>api-&lt;pid&gt;.sock</c> in the same directory
        /// (<c>api_server.cpp</c>, lines 115-125). No default can know that process id.
        /// </para>
        /// <para>
        /// <b>KiCad from Flathub</b> is an exception on Linux (#110). It runs with
        /// <c>TMPDIR=/var/tmp</c> inside its sandbox, and the sandbox's <c>/var/tmp</c> is
        /// <c>~/.var/app/org.kicad.KiCad/cache/tmp</c> on the host, so a client outside the sandbox
        /// finds nothing at the address above. When no socket exists there, and one exists at
        /// <c>~/.var/app/org.kicad.KiCad/cache/tmp/kicad/api.sock</c>, the result is that one; see
        /// <see cref="FlathubSocketFile"/>. Existence is <see cref="File.Exists(string)"/>, which is
        /// true for a socket. A native KiCad that is listening at the address above always wins. A
        /// socket file left behind by a KiCad that did not exit cleanly counts as existing, so the
        /// dial then fails with "Connection refused", as it does for a native KiCad's; KiCad removes
        /// such a file the next time it starts (<c>api_server.cpp</c>, lines 96-112).
        /// </para>
        /// <para>
        /// The path can be longer than the platform allows. KiCad does not shorten it. On Linux a
        /// socket path has to fit in <c>sun_path</c>, 108 bytes with its terminating NUL, so a
        /// <c>TMPDIR</c> longer than 92 characters leaves KiCad with no socket at all. The
        /// address returned here is still the one KiCad chose, and dialling it fails with a
        /// <see cref="KiCadConnectionException"/>. See docs/ipc.md.
        /// </para>
        /// </remarks>
        public static string GetDefaultSocketPath()
        {
            return GetDefaultSocketPath(
                CurrentSocketPlatform,
                Environment.GetEnvironmentVariable,
                Directory.Exists,
                Path.GetTempPath,
                File.Exists,
                () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        /// <summary>The three ways KiCad places its socket; see <see cref="GetDefaultSocketPath()"/>.</summary>
        internal enum SocketPlatform
        {
            Unix,
            MacOS,
            Windows,
        }

        private static SocketPlatform CurrentSocketPlatform =>
            OperatingSystem.IsWindows() ? SocketPlatform.Windows
            : OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() ? SocketPlatform.MacOS
            : SocketPlatform.Unix;

        /// <summary>
        /// <see cref="GetDefaultSocketPath()"/> over a platform, an environment, a file system, a
        /// system temp directory and a home directory the caller supplies, so a test can describe
        /// each one. <paramref name="fileExists"/> answers whether a socket file is there;
        /// <paramref name="homeDirectory"/> is where a Flathub KiCad's sandbox data lives under.
        /// </summary>
        internal static string GetDefaultSocketPath(
            SocketPlatform platform,
            Func<string, string?> getVariable,
            Func<string, bool> directoryExists,
            Func<string> systemTempPath,
            Func<string, bool> fileExists,
            Func<string?> homeDirectory)
        {
            var configured = getVariable("KICAD_API_SOCKET");
            if (!string.IsNullOrEmpty(configured))
            {
                return configured;
            }

            if (platform == SocketPlatform.MacOS)
            {
                return "ipc:///tmp/kicad/api.sock";
            }

            var directory = TempDirectory(platform, getVariable, directoryExists, systemTempPath);
            var socket = SocketFileIn(directory, windows: platform == SocketPlatform.Windows);

            // A KiCad from Flathub listens where its sandbox's /var/tmp is on the host (#110). Only
            // when nothing is at KiCad's own address: a native KiCad that is listening wins over a
            // socket file the Flatpak left behind.
            if (platform == SocketPlatform.Unix && !fileExists(socket))
            {
                var flathub = FlathubSocketFile(homeDirectory());
                if (flathub is not null && fileExists(flathub))
                {
                    socket = flathub;
                }
            }

            return "ipc://" + socket;
        }

        /// <summary>
        /// Where a KiCad from Flathub listens, seen from the host:
        /// <c>&lt;home&gt;/.var/app/org.kicad.KiCad/cache/tmp/kicad/api.sock</c>; or
        /// <see langword="null"/> when there is no home directory to put it under.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The Flathub manifest (<c>flathub/org.kicad.KiCad</c>, <c>org.kicad.KiCad.yml</c>,
        /// <c>finish-args</c>, at <c>279821a</c>) starts KiCad with <c>TMPDIR=/var/tmp</c>. Commit
        /// <c>761588f</c> (2025-02-19) had first shared the host's <c>/tmp</c> into the sandbox,
        /// "Required for default path of KiCad's IPC API"; <c>8d96620</c> (2025-02-21) replaced that
        /// with the variable. Flatpak binds the sandbox's <c>/var/tmp</c> to
        /// <c>&lt;app data&gt;/cache/tmp</c>, and the app data directory is
        /// <c>g_get_home_dir()/.var/app/&lt;app id&gt;</c> (flatpak 1.14.6,
        /// <c>common/flatpak-run.c</c>, lines 3617 and 2096). <c>g_get_home_dir()</c> is
        /// <c>$HOME</c>, then the passwd entry, which is also how
        /// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> finds
        /// <see cref="Environment.SpecialFolder.UserProfile"/> on Unix.
        /// </para>
        /// <para>
        /// MEASURED with org.kicad.KiCad 10.0.6 from Flathub on flatpak 1.14.6: inside the sandbox
        /// pcbnew has <c>TMPDIR=/var/tmp</c>; <c>/var/tmp</c> is a bind mount of
        /// <c>~/.var/app/org.kicad.KiCad/cache/tmp</c>; <c>/tmp</c> and <c>$XDG_RUNTIME_DIR</c> are
        /// private tmpfs mounts the host never sees. pcbnew listens on
        /// <c>/var/tmp/kicad/api.sock</c>, and a process on the host connects to
        /// <c>~/.var/app/org.kicad.KiCad/cache/tmp/kicad/api.sock</c> as it is. It is an ordinary
        /// socket inode on the host's file system: nothing has to be granted with
        /// <c>flatpak override</c>. kipy tries the same path (<c>_default_socket_path</c> in
        /// <c>kipy/kicad.py</c>). See docs/ipc.md.
        /// </para>
        /// </remarks>
        private static string? FlathubSocketFile(string? home) =>
            string.IsNullOrEmpty(home) ? null : SocketFileIn(home + "/.var/app/org.kicad.KiCad/cache/tmp", windows: false);

        private static readonly string[] TempVariables = ["TMPDIR", "TMP", "TEMP"];

        /// <summary>
        /// <c>wxFileName::GetTempDir()</c>, the same in wxWidgets 3.2.9 (what Ubuntu builds KiCad
        /// 10.0.6 against) and 3.3.1 (what KiCad's own Windows build pins in <c>vcpkg.json</c>):
        /// <c>src/common/filename.cpp</c>, lines 1215-1266 and 1228-1279.
        /// </summary>
        private static string TempDirectory(
            SocketPlatform platform,
            Func<string, string?> getVariable,
            Func<string, bool> directoryExists,
            Func<string> systemTempPath)
        {
            foreach (var name in TempVariables)
            {
                var value = getVariable(name);
                if (!string.IsNullOrEmpty(value) && directoryExists(value))
                {
                    return value;
                }
            }

            if (platform == SocketPlatform.Windows)
            {
                var system = systemTempPath();
                if (!string.IsNullOrEmpty(system))
                {
                    return system;
                }
            }
            else if (directoryExists("/tmp"))
            {
                return "/tmp";
            }

            // wx's last resort: the working directory -- KiCad's, which is not this process's.
            return ".";
        }

        /// <summary>
        /// <c>&lt;directory&gt;/kicad/api.sock</c>, written as <c>wxFileName</c> writes it after
        /// <c>AssignDir</c>, <c>AppendDir</c> and <c>SetFullName</c>. An empty component, from a
        /// trailing or a repeated separator, is dropped (<c>wxFileName::DoSetPath</c>), and the
        /// native separator joins the rest. On Windows both <c>\</c> and <c>/</c> separate, and a
        /// UNC path keeps its leading <c>\\</c>.
        /// </summary>
        private static string SocketFileIn(string directory, bool windows)
        {
            char[] separators = windows ? ['\\', '/'] : ['/'];
            var separator = windows ? '\\' : '/';

            var root = "";
            if (directory.Length > 0 && separators.Contains(directory[0]))
            {
                root = windows && directory.Length > 1 && separators.Contains(directory[1]) ? @"\\" : separator.ToString();
            }

            var components = directory.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            return root + string.Join(separator, [.. components, "kicad", "api.sock"]);
        }

        /// <summary>
        /// Generates a random client name for KiCad API connections
        /// </summary>
        /// <returns>Random client name</returns>
        public static string GenerateRandomClientName()
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            var suffix = new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[random.Next(s.Length)]).ToArray());

            return $"anonymous-{suffix}";
        }


        public static bool IsRunningOnKiCad() => !string.IsNullOrWhiteSpace(GetApiToken()) && !string.IsNullOrWhiteSpace(GetApiSocket());
    }
}
