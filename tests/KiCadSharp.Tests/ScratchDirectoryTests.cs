using System.Diagnostics;

namespace KiCadSharp.Tests;

/// <summary>
/// The helper every test that writes a file goes through (#79): what it creates, it deletes.
/// </summary>
public class ScratchDirectoryTests
{
    [Fact]
    public void Dispose_DeletesTheDirectoryAndEverythingInIt()
    {
        var scratch = TestData.NewScratchDirectory();
        var nested = Directory.CreateDirectory(Path.Combine(scratch, "Library.pretty")).FullName;
        File.WriteAllText(Path.Combine(scratch, "board.kicad_pcb"), "(kicad_pcb)");
        File.WriteAllText(Path.Combine(nested, "part.kicad_mod"), "(footprint \"part\")");

        scratch.Dispose();

        Assert.Equal(ScratchDirectory.KeepFiles, Directory.Exists(scratch.Path));
        scratch.Dispose(); // a second dispose is harmless
    }

    [Fact]
    public void ItIsNamedAfterTheTestThatAskedForIt_UnderTheProjectsFolder()
    {
        using var first = TestData.NewScratchDirectory();
        using var second = TestData.NewScratchDirectory();

        Assert.True(Directory.Exists(first));
        Assert.Empty(Directory.EnumerateFileSystemEntries(first));
        Assert.NotEqual(first.Path, second.Path);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "kicadsharp-tests"), Path.GetDirectoryName(first.Path));
        Assert.StartsWith(
            nameof(ItIsNamedAfterTheTestThatAskedForIt_UnderTheProjectsFolder) + "-",
            Path.GetFileName(first.Path),
            StringComparison.Ordinal);
        Assert.Equal(first.Path, (string)first);
    }

    [Fact]
    public void Dispose_NeitherThrowsNorWaitsLong_WhileAFileInItIsStillOpen()
    {
        // What a kicad-cli that has not quite let go of its output looks like. Linux and macOS
        // delete the directory anyway; Windows refuses while the handle is open, and the directory is
        // then left behind rather than failing the test.
        var scratch = TestData.NewScratchDirectory();
        var held = Path.Combine(scratch, "held.kicad_pcb");
        var clock = Stopwatch.StartNew();
        using (new FileStream(held, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            scratch.Dispose();
        }

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Dispose took {clock.Elapsed}");
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(ScratchDirectory.KeepFiles, Directory.Exists(scratch.Path));
        }
        else if (!ScratchDirectory.KeepFiles)
        {
            Directory.Delete(scratch.Path, recursive: true);
        }
    }

    [Fact]
    public void Dispose_GivesUpQuietly_OnADirectoryItCannotDelete()
    {
        // The give-up path: a sub-directory the process may not write to, so the file in it cannot
        // be removed however long Dispose retries. It must return, soon, and leave it.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var scratch = TestData.NewScratchDirectory();
        var locked = Directory.CreateDirectory(Path.Combine(scratch, "locked")).FullName;
        File.WriteAllText(Path.Combine(locked, "board.kicad_pcb"), "(kicad_pcb)");
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var clock = Stopwatch.StartNew();
            scratch.Dispose();

            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Dispose took {clock.Elapsed}");
            if (!Environment.IsPrivilegedProcess)
            {
                Assert.True(Directory.Exists(locked)); // root may delete it anyway
            }
        }
        finally
        {
            if (Directory.Exists(locked))
            {
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            if (!ScratchDirectory.KeepFiles && Directory.Exists(scratch.Path))
            {
                Directory.Delete(scratch.Path, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    public void TheOptOut_IsOnForAnyValueButEmptyZeroOrFalse(string? value, bool keep) =>
        Assert.Equal(keep, ScratchDirectory.IsKeep(value));
}
