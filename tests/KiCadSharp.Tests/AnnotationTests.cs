using System.Diagnostics;

using KiCadSharp.Schematics;

namespace KiCadSharp.Tests;

/// <summary>
/// Annotation, over the <c>duplicate-refs</c> fixture: one child sheet instantiated twice, so every
/// designator in it is used twice.
/// </summary>
/// <remarks>
/// The measured consequence, from the fixture's own README: the netlist reports 14 components but
/// only 7 distinct references, and <c>GND</c> carries 4 nodes where it should carry 8 — half the
/// board's ground connections do not exist. <c>kicad-cli sch erc --severity-all</c> finds 22
/// violations and names this in none of them.
/// </remarks>
public class AnnotationTests
{
    // ------------------------------------------------------------------------------ the hierarchy

    [Fact]
    public void Hierarchy_SeesTwoInstancesOfOneFile()
    {
        var root = TestData.CopyDuplicateRefs(out _);
        var hierarchy = SchematicHierarchy.Load(root);

        Assert.Equal("duplicate-refs", hierarchy.ProjectName);

        // Root sheet, plus PWR1 and PWR2.
        Assert.Equal(3, hierarchy.SheetInstances.Count);
        Assert.Equal(new[] { "/", "/PWR1", "/PWR2" }, hierarchy.SheetInstances.Select(s => s.DisplayPath));

        // Two files: the root and the one child, loaded once and shared.
        Assert.Equal(2, hierarchy.Schematics.Count);
        Assert.Same(hierarchy.SheetInstances[1].Schematic, hierarchy.SheetInstances[2].Schematic);

        // Paths are the root sheet's UUID followed by each sheet's own.
        var rootUuid = hierarchy.Root.Uuid;
        Assert.Equal("/" + rootUuid, hierarchy.SheetInstances[0].Path);
        Assert.StartsWith("/" + rootUuid + "/", hierarchy.SheetInstances[1].Path);
        Assert.NotEqual(hierarchy.SheetInstances[1].Path, hierarchy.SheetInstances[2].Path);

        // 14 symbols in the child, seen twice; nothing on the root sheet.
        Assert.Equal(28, hierarchy.Placements.Count());
    }

    [Fact]
    public void BeforeAnnotation_SevenDesignatorsAreUsedInTwoSheets()
    {
        var root = TestData.CopyDuplicateRefs(out _);
        var hierarchy = SchematicHierarchy.Load(root);

        var duplicates = SchematicAnnotator.FindDuplicateReferences(hierarchy);

        // The 7 the fixture's README names, plus the 7 power and flag symbols it does not list
        // because the netlist does not carry them.
        Assert.Equal(14, duplicates.Count);
        Assert.Equal(
            new[] { "C90", "D90", "D91", "F1", "J90", "Q90", "R90" },
            duplicates.Select(d => d.Key).Where(k => !k.StartsWith(KiCadDefaults.GeneratedReferencePrefix)));
        Assert.All(duplicates, d => Assert.Equal(new[] { "/PWR1", "/PWR2" }, d.Value));
    }

    // ----------------------------------------------------------------------------- what it changes

    [Fact]
    public void Annotate_GivesEveryPlacementItsOwnDesignator()
    {
        var root = TestData.CopyDuplicateRefs(out _);
        var hierarchy = SchematicHierarchy.Load(root);

        var result = SchematicAnnotator.Annotate(hierarchy);

        Assert.Equal(28, result.PlacementCount);
        Assert.Equal(28, result.ReferenceCount);
        Assert.True(result.IsFullyAnnotated);
        Assert.Equal(14, result.Changes.Count);
        Assert.Empty(SchematicAnnotator.FindDuplicateReferences(hierarchy));

        // The first instance keeps what the sheet was authored with; only the second is renumbered.
        Assert.All(result.Changes, c => Assert.Equal("/PWR2", c.SheetName));
    }

    [Fact]
    public void Annotate_KeepsThePrefixAndOnlyRenumbers()
    {
        var root = TestData.CopyDuplicateRefs(out _);
        var hierarchy = SchematicHierarchy.Load(root);

        var result = SchematicAnnotator.Annotate(hierarchy);

        foreach (var change in result.Changes)
        {
            Assert.Equal(PrefixOf(change.From), PrefixOf(change.To));
        }

        var second = hierarchy.SheetInstances[2];
        var references = second.Schematic.Symbols
            .Select(s => s.GetInstanceReference(hierarchy.ProjectName, second.Path))
            .ToList();

        Assert.Contains("J1", references);
        Assert.Contains("C1", references);

        // Zero padding is part of how the designator was written, so it is kept: #PWR001 -> #PWR006,
        // not #PWR6.
        Assert.Contains("#PWR006", references);
        Assert.DoesNotContain("#PWR6", references);
    }

    [Fact]
    public void Annotate_LeavesTheFirstInstanceAlone()
    {
        var root = TestData.CopyDuplicateRefs(out _);
        var hierarchy = SchematicHierarchy.Load(root);

        SchematicAnnotator.Annotate(hierarchy);

        var first = hierarchy.SheetInstances[1];
        var references = first.Schematic.Symbols
            .Select(s => s.GetInstanceReference(hierarchy.ProjectName, first.Path))
            .ToList();

        Assert.Equal(
            new[] { "C90", "D90", "D91", "F1", "J90", "Q90", "R90" },
            references.Where(r => r is not null && !r.StartsWith(KiCadDefaults.GeneratedReferencePrefix)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Annotate_DoesNotTouchTheReferenceProperty()
    {
        var root = TestData.CopyDuplicateRefs(out _);
        var hierarchy = SchematicHierarchy.Load(root);

        SchematicAnnotator.Annotate(hierarchy);

        // The property is what the sheet was authored with and what the editor draws. A symbol on a
        // sheet used twice has no single correct value for it, and the netlist does not read it.
        var child = hierarchy.SheetInstances[1].Schematic;
        Assert.Equal(
            new[] { "C90", "D90", "D91", "F1", "J90", "Q90", "R90" },
            child.Symbols.Select(s => s.ReferenceProperty).Where(r => r is not null && !r.StartsWith(KiCadDefaults.GeneratedReferencePrefix)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Annotate_LeavesTheAuthoringProjectsInstancesAlone()
    {
        var root = TestData.CopyDuplicateRefs(out _);
        var hierarchy = SchematicHierarchy.Load(root);

        SchematicAnnotator.Annotate(hierarchy);

        // The child was authored standalone, under its own project name. Those entries are what the
        // sheet needs to still open on its own, and pruning must not reach them.
        var symbol = hierarchy.SheetInstances[1].Schematic.Symbols.First(s => s.ReferenceProperty == "J90");
        var projects = symbol.Node.GetChild("instances")!.GetChildren("project").Select(p => p.GetValue(0)).ToList();

        Assert.Equal(new[] { "power-input", "duplicate-refs" }, projects.Order(StringComparer.Ordinal).Reverse());
    }

    // --------------------------------------------------------------------------------- idempotency

    [Fact]
    public void Annotate_Twice_ChangesNothingTheSecondTime()
    {
        var root = TestData.CopyDuplicateRefs(out var directory);

        var first = SchematicAnnotator.AnnotateFile(root);
        Assert.Equal(14, first.Changes.Count);

        var afterFirst = Directory.GetFiles(directory, "*.kicad_sch").ToDictionary(f => f, File.ReadAllBytes);

        var hierarchy = SchematicHierarchy.Load(root);
        var second = SchematicAnnotator.Annotate(hierarchy);

        Assert.True(second.IsUnchanged);
        Assert.Empty(second.Changes);
        Assert.Equal(0, second.PrunedInstanceCount);

        // Nothing was modified, so nothing is rewritten.
        Assert.Empty(hierarchy.Save());
        foreach (var (path, bytes) in afterFirst)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void Annotate_OnlyRewritesTheFilesItChanged()
    {
        var root = TestData.CopyDuplicateRefs(out _);
        var hierarchy = SchematicHierarchy.Load(root);

        var rootBefore = File.ReadAllBytes(root);
        SchematicAnnotator.Annotate(hierarchy);
        var written = hierarchy.Save();

        // The root sheet holds no symbols, so annotation has nothing to write there.
        Assert.Single(written);
        Assert.EndsWith("power-input.kicad_sch", written[0]);
        Assert.Equal(rootBefore, File.ReadAllBytes(root));
    }

    [Fact]
    public void Annotate_ChangesOnlyTheInstanceBlocks()
    {
        var root = TestData.CopyDuplicateRefs(out var directory);
        var child = Path.Combine(directory, "power-input.kicad_sch");

        var before = File.ReadAllText(child);
        SchematicAnnotator.AnnotateFile(root);
        var after = File.ReadAllText(child);

        // Everything outside the (instances ...) blocks is untouched: strip them from both texts and
        // what is left is identical, byte for byte.
        Assert.Equal(WithoutInstanceBlocks(before), WithoutInstanceBlocks(after));
        Assert.Equal(45_693, before.Length);
    }

    // ------------------------------------------------------------------ what KiCad itself then says

    [Fact]
    public void Annotate_MakesKiCadsNetlistCorrect()
    {
        var cli = TestData.KiCadCli;
        if (cli is null)
        {
            return; // Opt in with KICADSHARP_KICAD_CLI; CI has no KiCad.
        }

        var root = TestData.CopyDuplicateRefs(out var directory);

        var (beforeOut, beforeErr) = Netlist(cli, root, Path.Combine(directory, "before.net"));
        Assert.Contains("annotation errors", beforeOut + beforeErr);
        Assert.Equal(7, DistinctComponentReferences(Path.Combine(directory, "before.net")));
        Assert.Equal(4, NodeCount(Path.Combine(directory, "before.net"), "GND"));

        SchematicAnnotator.AnnotateFile(root);

        var (afterOut, afterErr) = Netlist(cli, root, Path.Combine(directory, "after.net"));
        Assert.DoesNotContain("annotation errors", afterOut + afterErr);
        Assert.Equal(14, DistinctComponentReferences(Path.Combine(directory, "after.net")));
        Assert.Equal(8, NodeCount(Path.Combine(directory, "after.net"), "GND"));
    }

    // ---------------------------------------------------------------------------------- plumbing

    private static string PrefixOf(string reference) => reference.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');

    private static string WithoutInstanceBlocks(string text)
    {
        var output = new System.Text.StringBuilder(text.Length);
        var index = 0;
        while (true)
        {
            var start = text.IndexOf("(instances", index, StringComparison.Ordinal);
            if (start < 0)
            {
                output.Append(text, index, text.Length - index);
                return output.ToString();
            }

            output.Append(text, index, start - index);

            var depth = 0;
            var i = start;
            do
            {
                if (text[i] == '(')
                {
                    depth++;
                }
                else if (text[i] == ')')
                {
                    depth--;
                }

                i++;
            }
            while (depth > 0 && i < text.Length);

            index = i;
        }
    }

    private static (string StdOut, string StdErr) Netlist(string cli, string schematic, string output)
    {
        var start = new ProcessStartInfo(cli)
        {
            WorkingDirectory = Path.GetDirectoryName(schematic)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in new[] { "sch", "export", "netlist", "--format", "kicadsexpr", "-o", output, schematic })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (stdout, stderr);
    }

    private static int DistinctComponentReferences(string netlist) =>
        System.Text.RegularExpressions.Regex
            .Matches(File.ReadAllText(netlist), @"\(comp\s+\(ref ""([^""]+)""\)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static int NodeCount(string netlist, string net)
    {
        var text = File.ReadAllText(netlist);
        var match = System.Text.RegularExpressions.Regex.Match(
            text,
            @"\(net\s+\(code ""\d+""\)\s+\(name """ + System.Text.RegularExpressions.Regex.Escape(net) + @"""\)(.*?)(?=\n\t\t\(net\s|\n\t\)\n)",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        return match.Success
            ? System.Text.RegularExpressions.Regex.Matches(match.Groups[1].Value, @"\(node\s").Count
            : -1;
    }
}
