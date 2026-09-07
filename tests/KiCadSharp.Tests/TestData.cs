namespace KiCadSharp.Tests;

/// <summary>
/// Locates the vendored KiCad 10 fixtures, and gives a test a scratch directory it owns.
/// </summary>
/// <remarks>
/// The fixtures are copied to the output directory by the project file, so a test can hand a real
/// path to an external tool as well as read it. Nothing here reaches outside the repository: a test
/// that needs the wider corpus is opt-in through <see cref="KiCadCli"/>.
/// </remarks>
public static class TestData
{
    /// <summary>The directory holding the vendored fixtures.</summary>
    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>A 108,583-byte, 35-symbol KiCad 10 symbol library holding 112 pins in 67 sub-units.</summary>
    public static string SymbolLibrary => Path.Combine(Root, "orbion.kicad_sym");

    /// <summary>A standalone KiCad 6+ <c>.kicad_mod</c>: the root token is <c>footprint</c>, not <c>module</c>.</summary>
    public static string Footprint => Path.Combine(Root, "LED_0603_1608Metric.kicad_mod");

    /// <summary>The hierarchy that instantiates one child sheet twice, so every designator is used twice.</summary>
    public static string DuplicateRefsRoot => Path.Combine(Root, "duplicate-refs", "duplicate-refs.kicad_sch");

    /// <summary>
    /// Copies the whole <c>duplicate-refs</c> fixture into a fresh scratch directory and returns the
    /// path of the root schematic inside it. Annotation rewrites files, so no test may run against
    /// the fixture in place.
    /// </summary>
    public static string CopyDuplicateRefs(out string directory)
    {
        directory = NewScratchDirectory();
        var source = Path.Combine(Root, "duplicate-refs");
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        }

        return Path.Combine(directory, "duplicate-refs.kicad_sch");
    }

    /// <summary>Creates an empty directory under the system temp path, unique to this call.</summary>
    public static string NewScratchDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "kicadsharp-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// A <c>kicad-cli</c> to shell out to, or <see langword="null"/>. Set <c>KICADSHARP_KICAD_CLI</c>
    /// to opt in; without it the tests that would use it return early rather than fail, because CI
    /// has no KiCad installed.
    /// </summary>
    public static string? KiCadCli
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("KICADSHARP_KICAD_CLI");
            return string.IsNullOrEmpty(configured) ? null : configured;
        }
    }
}
