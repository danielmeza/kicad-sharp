using SExpressions;

namespace KiCadSharp.Tests;

/// <summary>
/// The layer underneath the document model already keeps a file intact. These tests are here to
/// make that the baseline every other test is read against: when a KiCadSharp round trip loses
/// bytes, it is KiCadSharp losing them, not the parser.
/// </summary>
public class UnderlyingFidelityTests
{
    public static TheoryData<string> EveryFixture()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(TestData.Root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(TestData.Root, file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryFixture))]
    public void EveryFixture_RoundTripsByteForByte(string relativePath)
    {
        var path = Path.Combine(TestData.Root, relativePath);
        if (Path.GetExtension(path) is ".kicad_pro")
        {
            return; // JSON, not an s-expression.
        }

        var original = File.ReadAllBytes(path);
        var document = SDocument.Load(path);

        var output = Path.Combine(TestData.NewScratchDirectory(), Path.GetFileName(path));
        document.Save(output);

        Assert.Equal(original.Length, new FileInfo(output).Length);
        Assert.Equal(original, File.ReadAllBytes(output));
    }

    [Fact]
    public void EditingOneAtom_ChangesOnlyThatAtom()
    {
        var document = SDocument.Load(TestData.SymbolLibrary);
        var before = document.ToText();

        var generator = document.Root!.GetChild("generator")!;
        Assert.Equal("orbion-extract-symbols", generator.GetValueAsString());
        generator.SetValue(0, "kicad-sharp");

        var after = document.ToText();

        // 108,583 bytes in; the two texts differ by exactly the length difference of the one atom.
        Assert.Equal(108_583, before.Length);
        Assert.Equal(before.Length - "orbion-extract-symbols".Length + "kicad-sharp".Length, after.Length);

        var firstDifference = FirstDifference(before, after);
        Assert.Equal("orbion-extract-symbols", before.Substring(firstDifference, "orbion-extract-symbols".Length));
        Assert.Equal("kicad-sharp", after.Substring(firstDifference, "kicad-sharp".Length));
    }

    private static int FirstDifference(string a, string b)
    {
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            if (a[i] != b[i])
            {
                return i;
            }
        }

        return Math.Min(a.Length, b.Length);
    }
}
