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
        /// Gets the default socket path based on the operating system
        /// </summary>
        /// <returns>Default socket path for the current platform</returns>
        public static string GetDefaultSocketPath()
        {
            string? path = GetApiSocket();
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }

            // Use the same logic as the Python implementation
            if (OperatingSystem.IsWindows())
            {
                return $"ipc://{Path.GetTempPath()}\\kicad\\api.sock";
            }
            else
            {
                return "ipc:///tmp/kicad/api.sock";
            }
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
