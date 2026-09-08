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
    /// <b>And an escape hatch.</b> Upstream publishes <c>libnng</c> for six runtime identifiers
    /// (<see cref="ShippedRuntimeIdentifiers"/>). <c>osx-arm64</c>, <c>win-arm64</c> and
    /// <c>linux-musl-*</c> are not among them, and were not before this either -- the same six
    /// arrived through <c>nng.NET</c>. On those, <see cref="LibraryPathVariableName"/> names a
    /// <c>libnng</c> to load instead (<c>brew install nng</c>, a distribution package, your own
    /// build), which turns "unsupported" into "supply the binary". Without it there is no way in at
    /// all.
    /// </para>
    /// </remarks>
    internal static class NngLibraryResolver
    {
        /// <summary>See <see cref="Nng.LibraryPathVariable"/>.</summary>
        internal const string LibraryPathVariableName = Nng.LibraryPathVariable;

        /// <summary>
        /// The runtime identifiers this package carries a <c>libnng</c> for. Exactly the set
        /// <c>nng.NET</c> shipped, because these are the same files.
        /// </summary>
        internal static readonly string[] ShippedRuntimeIdentifiers =
            ["linux-x64", "linux-arm64", "linux-arm", "osx-x64", "win-x64", "win-x86"];

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
            if (!string.IsNullOrWhiteSpace(configured) && NativeLibrary.TryLoad(configured, out var overridden))
            {
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
        internal static IEnumerable<string> ProbePaths(Assembly? assembly = null)
        {
            var roots = new List<string>(2) { AppContext.BaseDirectory };
            var beside = assembly is null || string.IsNullOrEmpty(assembly.Location)
                ? null
                : Path.GetDirectoryName(assembly.Location);
            if (!string.IsNullOrEmpty(beside) && !roots.Contains(beside))
            {
                roots.Add(beside);
            }

            var identifiers = new List<string>(2) { RuntimeInformation.RuntimeIdentifier };
            var portable = PortableRuntimeIdentifier();
            if (!identifiers.Contains(portable))
            {
                identifiers.Add(portable);
            }

            foreach (var root in roots)
            {
                foreach (var identifier in identifiers)
                {
                    yield return Path.Combine(root, "runtimes", identifier, "native", FileName);
                }
            }
        }

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
                    + "is more likely an architecture mismatch: nng is published for osx-x64 and not osx-arm64. ";
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
        internal static IEnumerable<string> DiagnosticPaths()
        {
            foreach (var path in ProbePaths(typeof(NngLibraryResolver).Assembly))
            {
                yield return path;
            }

            yield return Path.Combine(AppContext.BaseDirectory, FileName);
        }

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
    }
}
