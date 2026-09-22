using System.Reflection;
using System.Runtime.InteropServices;

namespace KiCadSharp.Interop
{
    /// <summary>
    /// Finds <c>libnng</c> for the P/Invokes in <see cref="Nng"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists at all.</b> The native library ships under
    /// <c>runtimes/&lt;rid&gt;/native/</c>, which is a NuGet convention, not a loader one. The .NET
    /// host only turns that path into a probing directory when the file arrives as a <i>NuGet native
    /// asset</i> and is written into the application's <c>.deps.json</c>. That covers the case that
    /// matters most -- a consumer referencing the <c>KiCadSharp</c> package -- and nothing else.
    /// Measured, with the file present at <c>bin/runtimes/linux-x64/native/libnng.so</c> and no
    /// <c>deps.json</c> entry for it:
    /// </para>
    /// <code>
    /// System.DllNotFoundException: Unable to load shared library 'nng' or one of its dependencies.
    ///   …/bin/Release/net10.0/libnng.so: cannot open shared object file: No such file or directory
    /// </code>
    /// <para>
    /// The loader looks beside the executable and on the OS search path; it never looks one level
    /// down into <c>runtimes/</c>. So a project reference to <c>KiCadSharp</c>, an xcopy deployment,
    /// or anything that lays the tree out by hand fails on the first call with that message. This
    /// resolver adds the one directory the convention implies, which is the whole of step 2 below.
    /// </para>
    /// <para>
    /// <b>And an escape hatch.</b> The package carries <c>libnng</c> for eight runtime identifiers
    /// (<see cref="ShippedRuntimeIdentifiers"/>). Any other, such as <c>linux-musl-x64</c>, has none,
    /// and there <see cref="LibraryPathVariableName"/> names a <c>libnng</c> to load instead (a
    /// distribution package, your own build), which turns "unsupported" into "supply the binary".
    /// Without it there is no way in at all.
    /// </para>
    /// <para>
    /// <b>What that variable names is checked.</b> A library that loads is not necessarily nng, and
    /// the runtime looks an entry point up only when it is first called. So a wrong one used to
    /// surface as a bare <see cref="EntryPointNotFoundException"/> from whichever call came first
    /// (#70). The resolver now looks up every entry point in <see cref="EntryPoints"/> as it loads the
    /// library. If one is missing, it refuses the library with a <see cref="KiCadConnectionException"/>
    /// that names the variable, the path and what is missing.
    /// </para>
    /// <para>
    /// <b>A variable that is set is used, or the connection fails.</b> A value that does not load
    /// at all, because no file is there or the platform loader refuses it, used to be dropped without
    /// a word, and the shipped library was loaded in its place (#83). Now it is refused the same
    /// way, and nothing is loaded instead. A variable that is empty or only whitespace counts as
    /// not set.
    /// </para>
    /// </remarks>
    internal static class NngLibraryResolver
    {
        /// <summary>See <see cref="Nng.LibraryPathVariable"/>.</summary>
        internal const string LibraryPathVariableName = Nng.LibraryPathVariable;

        /// <summary>
        /// The runtime identifiers this package carries a <c>libnng</c> for: the six <c>nng.NET</c>
        /// publishes, whose files are copied out of it, and <c>osx-arm64</c> and <c>win-arm64</c>,
        /// which are built from nng's source in this repository (<c>native/NNG_PIN</c>).
        /// <c>NngInteropTests</c> holds this list to the <c>runtimes/</c> folders a build produces.
        /// </summary>
        internal static readonly string[] ShippedRuntimeIdentifiers =
            ["linux-x64", "linux-arm64", "linux-arm", "osx-x64", "osx-arm64", "win-x64", "win-x86", "win-arm64"];

        /// <summary>
        /// Every nng function <see cref="Nng"/> declares, which is every one this client calls. A
        /// library named by <see cref="LibraryPathVariableName"/> has to export all of them.
        /// <c>NngLibraryVariableTests</c> compares this list with the declarations, so an entry
        /// point cannot be added to <see cref="Nng"/> without being added here.
        /// </summary>
        internal static readonly string[] EntryPoints =
        [
            nameof(Nng.nng_req0_open),
            nameof(Nng.nng_close),
            nameof(Nng.nng_dial),
            nameof(Nng.nng_sendmsg),
            nameof(Nng.nng_recvmsg),
            nameof(Nng.nng_msg_alloc),
            nameof(Nng.nng_msg_free),
            nameof(Nng.nng_msg_append),
            nameof(Nng.nng_msg_body),
            nameof(Nng.nng_msg_len),
            nameof(Nng.nng_strerror),
            nameof(Nng.nng_socket_set_ms),
            nameof(Nng.nng_version),
        ];

        private static IntPtr _handle;

        /// <summary>The file name nng uses on the running platform.</summary>
        internal static string FileName =>
            OperatingSystem.IsWindows() ? "nng.dll" :
            OperatingSystem.IsMacOS() ? "libnng.dylib" :
            "libnng.so";

        /// <summary>
        /// Resolution order: the <see cref="LibraryPathVariableName"/> override, then
        /// <c>runtimes/&lt;rid&gt;/native/</c> under the application and next to this assembly, then
        /// -- by returning <see cref="IntPtr.Zero"/> -- the runtime's own probing, which is what
        /// resolves the NuGet native asset from <c>.deps.json</c>.
        /// </summary>
        /// <exception cref="KiCadConnectionException">
        /// <see cref="LibraryPathVariableName"/> is set and names something that does not load, or a
        /// library that loads and lacks one of the <see cref="EntryPoints"/>.
        /// </exception>
        internal static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, Nng.Library, StringComparison.Ordinal))
            {
                return IntPtr.Zero;
            }

            // One handle for the whole process. The resolver is invoked once per entry point, and
            // there are thirteen of them; there is no reason to hit the file system thirteen times.
            if (_handle != IntPtr.Zero)
            {
                return _handle;
            }

            var configured = Environment.GetEnvironmentVariable(LibraryPathVariableName);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                // Named, so it is that library or none. A value that does not load is refused, and
                // the probing below is never reached: it would load the shipped libnng in place of
                // the one the user asked for, and say nothing (#83).
                IntPtr overridden;
                try
                {
                    overridden = NativeLibrary.Load(configured);
                }
                catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException)
                {
                    throw CouldNotLoad(configured, exception);
                }

                // Loading it proves that it is a shared library, not that it is nng. Every entry
                // point is looked up now, so a wrong library is refused here with one message. It
                // does not fail later, on its first call, or halfway through a dial for a library
                // that has some of them. Thrown from the resolver, the exception reaches the caller
                // of the first nng call, which is NngRequestSocket.Open. Nothing is cached: the next
                // call looks again, and fails the same way until the variable changes.
                var missing = MissingEntryPoints(overridden);
                if (missing.Length > 0)
                {
                    NativeLibrary.Free(overridden);
                    throw NotNng(configured, missing);
                }

                return _handle = overridden;
            }

            foreach (var candidate in ProbePaths(assembly))
            {
                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var loaded))
                {
                    return _handle = loaded;
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// The <c>runtimes/&lt;rid&gt;/native/</c> paths worth looking at, most specific first. Both
        /// the host's runtime identifier and a plain <c>&lt;os&gt;-&lt;arch&gt;</c> are tried,
        /// because a build that pins a RID reports that RID rather than the portable one, and the
        /// folder on disk is always the portable name.
        /// </summary>
        /// <remarks>
        /// Each path appears once (#84). The two roots are usually the same directory spelled two
        /// ways: <see cref="AppContext.BaseDirectory"/> ends in a separator and
        /// <see cref="Path.GetDirectoryName(string)"/> does not. Compared as strings they differed,
        /// so the same file was probed twice and listed twice in <see cref="DescribeFailure"/>.
        /// </remarks>
        internal static IEnumerable<string> ProbePaths(Assembly? assembly = null)
        {
            var roots = new List<string>(2) { AppContext.BaseDirectory };
            if (assembly is not null && !string.IsNullOrEmpty(assembly.Location)
                && Path.GetDirectoryName(assembly.Location) is { Length: > 0 } beside)
            {
                roots.Add(beside);
            }

            string[] identifiers = [RuntimeInformation.RuntimeIdentifier, PortableRuntimeIdentifier()];

            return Distinct(roots.SelectMany(root =>
                identifiers.Select(identifier => Path.Combine(root, "runtimes", identifier, "native", FileName))));
        }

        /// <summary>
        /// <paramref name="paths"/> made absolute, without a trailing separator, and each one once,
        /// in the order it first appears.
        /// </summary>
        internal static IEnumerable<string> Distinct(IEnumerable<string> paths) =>
            paths
                .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
                .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        /// <summary>
        /// What libnng links against on the running platform, and what to do about it.
        /// </summary>
        /// <remarks>
        /// Read out of the shipped binaries rather than guessed:
        /// <list type="bullet">
        /// <item><description>
        /// <b>Linux</b> (<c>x64</c>, <c>arm64</c>, <c>arm</c>): <c>librt.so.1</c>,
        /// <c>libpthread.so.0</c>, <c>libnsl.so.1</c>, <c>libatomic.so.1</c>, <c>libc.so.6</c>.
        /// <c>libatomic.so.1</c> is the one a slim or distroless image routinely lacks -- a bare
        /// <c>ubuntu:24.04</c> does. glibc, so no musl distribution can load this build.
        /// </description></item>
        /// <item><description>
        /// <b>macOS</b>: <c>/usr/lib/libSystem.B.dylib</c> and nothing else. Always present; a load
        /// failure here is not a missing dependency.
        /// </description></item>
        /// <item><description>
        /// <b>Windows</b>: <c>WS2_32</c>, <c>ADVAPI32</c>, <c>KERNEL32</c> and the UCRT, all in-box
        /// since Windows 10 -- plus <c>VCRUNTIME140.dll</c>, which is <b>not</b>. That ships in the
        /// Visual C++ 2015-2022 redistributable. It is on most machines because a great many
        /// applications install it, and it is not guaranteed, and .NET itself does not require it.
        /// </description></item>
        /// </list>
        /// </remarks>
        internal static string NativeDependencyAdvice()
        {
            if (OperatingSystem.IsWindows())
            {
                return "on Windows nng.dll needs VCRUNTIME140.dll, which is not part of Windows and does not come with "
                    + ".NET -- install the Microsoft Visual C++ 2015-2022 Redistributable. Everything else it imports "
                    + "(WS2_32, ADVAPI32, KERNEL32, the UCRT) is in-box. ";
            }

            if (OperatingSystem.IsMacOS())
            {
                return "on macOS libnng.dylib links only /usr/lib/libSystem.B.dylib, which is always present, so this "
                    + "is more likely an architecture mismatch: each libnng.dylib is built for one architecture, x64 or "
                    + "arm64, and it has to be this process's. ";
            }

            return "on Linux libnng.so needs libatomic.so.1, libnsl.so.1, librt.so.1, libpthread.so.0 and glibc. "
                + "A slim or distroless image routinely has no libatomic.so.1 (apt install libatomic1), and a musl "
                + "distribution such as Alpine cannot load this glibc build at all. ";
        }

        /// <summary>
        /// Everywhere the library could plausibly be, for the purpose of saying what went wrong.
        /// Wider than <see cref="ProbePaths"/>: it also covers the flat layout a self-contained or
        /// single-file publish produces, where the native asset is put beside the executable and the
        /// runtime's own probing -- not this resolver -- is what finds it.
        /// </summary>
        internal static IEnumerable<string> DiagnosticPaths() =>
            Distinct(ProbePaths(typeof(NngLibraryResolver).Assembly).Append(Path.Combine(AppContext.BaseDirectory, FileName)));

        /// <summary><c>&lt;os&gt;-&lt;arch&gt;</c>, the shape of the folders in the package.</summary>
        internal static string PortableRuntimeIdentifier()
        {
            var os =
                OperatingSystem.IsWindows() ? "win" :
                OperatingSystem.IsMacOS() ? "osx" :
                OperatingSystem.IsLinux() ? "linux" :
                "unknown";

            var architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            };

            return $"{os}-{architecture}";
        }

        /// <summary>
        /// What to tell someone whose platform has no <c>libnng</c>. Named paths and a way forward,
        /// rather than the loader's "cannot open shared object file".
        /// </summary>
        internal static string DescribeFailure()
        {
            var identifier = PortableRuntimeIdentifier();
            var paths = DiagnosticPaths().ToArray();
            var present = paths.FirstOrDefault(File.Exists);

            string message;
            if (present is not null)
            {
                // The file is there and the loader still would not take it, which means a dependency
                // of libnng is missing rather than anything about this package. Which dependency is
                // entirely platform-specific, so say the one that applies -- naming Linux's libatomic
                // on a Windows machine is worse than saying nothing.
                message = $"KiCadSharp found nng at {present} and the platform loader refused it. "
                    + "That is a dependency of the native library itself, not a missing file: "
                    + NativeDependencyAdvice();
            }
            else if (ShippedRuntimeIdentifiers.Contains(identifier))
            {
                message = $"KiCadSharp could not find nng ({FileName}) for {identifier}, although this package ships "
                    + "one for that platform, so the native asset did not reach the application's output. ";
            }
            else
            {
                message = $"KiCadSharp ships no nng ({FileName}) for {identifier}. nng is published for "
                    + $"{string.Join(", ", ShippedRuntimeIdentifiers)} and no others, which is the same set this "
                    + "library has always carried. ";
            }

            return message
                + $"Set {LibraryPathVariableName} to the full path of a libnng to use instead. Looked in: "
                + string.Join(", ", paths)
                + ", then the runtime's own native library probing.";
        }

        /// <summary>
        /// The <see cref="EntryPoints"/> that <paramref name="library"/> does not export, in the
        /// order they are listed. Empty for an nng this client can use.
        /// </summary>
        internal static string[] MissingEntryPoints(IntPtr library) =>
            EntryPoints.Where(name => !NativeLibrary.TryGetExport(library, name, out _)).ToArray();

        /// <summary>
        /// The failure for a <see cref="LibraryPathVariableName"/> that names a library which loads
        /// and is not nng. It is the type the other two load failures reach the caller as (see
        /// <see cref="NngRequestSocket.Open"/>). Inside it is the
        /// <see cref="EntryPointNotFoundException"/> the runtime would have thrown for the first
        /// missing function.
        /// </summary>
        /// <param name="configured">The value of <see cref="LibraryPathVariableName"/>.</param>
        /// <param name="missing">What <see cref="MissingEntryPoints"/> found missing; not empty.</param>
        /// <param name="identifier">
        /// The runtime identifier to advise for; the running one by default. See
        /// <see cref="WayForward"/>.
        /// </param>
        internal static KiCadConnectionException NotNng(string configured, string[] missing, string? identifier = null)
        {
            var what = missing.Length == EntryPoints.Length
                ? $"is not nng: it exports none of the {EntryPoints.Length} nng functions KiCadSharp calls, "
                    + $"starting with {missing[0]}."
                : $"is not an nng KiCadSharp can use: it does not export {string.Join(", ", missing)} "
                    + $"({missing.Length} of the {EntryPoints.Length} nng functions KiCadSharp calls).";

            return new KiCadConnectionException(
                $"{LibraryPathVariableName} names '{configured}', which loads but {what} {WayForward(identifier)}",
                new EntryPointNotFoundException(
                    $"Unable to find an entry point named '{missing[0]}' in shared library '{configured}'."));
        }

        /// <summary>
        /// The failure for a <see cref="LibraryPathVariableName"/> that names something which does not
        /// load at all (#83). Inside it is the loader's own <see cref="DllNotFoundException"/> or
        /// <see cref="BadImageFormatException"/>, which carries the platform's reason.
        /// </summary>
        /// <param name="configured">The value of <see cref="LibraryPathVariableName"/>.</param>
        /// <param name="reason">What <see cref="NativeLibrary.Load(string)"/> threw.</param>
        /// <param name="identifier">
        /// The runtime identifier to advise for; the running one by default. See
        /// <see cref="WayForward"/>.
        /// </param>
        internal static KiCadConnectionException CouldNotLoad(string configured, Exception reason, string? identifier = null)
        {
            // Only a value with a directory in it is a path. A bare name is for the platform loader
            // to look up on its own search path, and whether a file of that name happens to be in
            // the working directory says nothing about it.
            var isPath = configured.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0;

            var what = isPath && !File.Exists(configured)
                ? "and there is no file at that path."
                : "and the platform loader could not load it. If the file is there, it is not a shared library, "
                    + "it is built for an architecture other than this process's "
                    + $"({RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}), "
                    + "or a library it depends on is missing. The loader's own reason is the inner exception.";

            return new KiCadConnectionException(
                $"{LibraryPathVariableName} names '{configured}', {what} {WayForward(identifier)}",
                reason);
        }

        /// <summary>
        /// What to do about a <see cref="LibraryPathVariableName"/> that was refused. Never "set the
        /// variable", which is already set. Unsetting it is offered only where this package ships a
        /// libnng for <paramref name="identifier"/>; anywhere else it would lead to a library that
        /// is not there.
        /// </summary>
        /// <param name="identifier">A runtime identifier; <see cref="PortableRuntimeIdentifier"/> by default.</param>
        internal static string WayForward(string? identifier = null)
        {
            identifier ??= PortableRuntimeIdentifier();
            return ShippedRuntimeIdentifiers.Contains(identifier)
                ? $"Point {LibraryPathVariableName} at a libnng, or unset it to use the {FileName} this package ships for {identifier}."
                : $"Point {LibraryPathVariableName} at a libnng built for {identifier}: this package ships none for that platform.";
        }
    }
}
