using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KiCadSharp.Schematics
{
    /// <summary>
    /// Knobs for <see cref="SchematicAnnotator"/>.
    /// </summary>
    public sealed class AnnotationOptions
    {
        /// <summary>
        /// Keep a reference designator that is already numbered and not already taken. On by
        /// default, and it is what makes a second run change nothing: after the first pass every
        /// designator is unique, so every one of them is kept.
        /// </summary>
        /// <remarks>
        /// Turn it off to renumber a whole design from scratch. The result is still deterministic —
        /// numbering follows the hierarchy walk — but every designator may move.
        /// </remarks>
        public bool KeepExistingReferences { get; set; } = true;

        /// <summary>
        /// Annotate power and flag symbols — the ones whose designator starts with <c>#</c>. On by
        /// default: they are parts like any other and KiCad numbers them too.
        /// </summary>
        public bool AnnotatePowerSymbols { get; set; } = true;

        /// <summary>
        /// Remove instance entries filed under this project whose sheet path no longer exists. On by
        /// default. Entries filed under any <em>other</em> project name are never touched, which is
        /// what lets a shared sheet keep the designators it was authored with.
        /// </summary>
        public bool PruneStaleInstances { get; set; } = true;

        /// <summary>
        /// The number to start each prefix at.
        /// </summary>
        public int StartAt { get; set; } = 1;
    }

    /// <summary>One designator this run changed.</summary>
    /// <param name="SheetPath">The hierarchical path of the sheet instance.</param>
    /// <param name="SheetName">The readable path of the sheet instance.</param>
    /// <param name="SymbolUuid">The symbol's UUID within its file.</param>
    /// <param name="From">What the symbol had before, as read.</param>
    /// <param name="To">What it has now.</param>
    public readonly record struct AnnotationChange(string SheetPath, string SheetName, string SymbolUuid, string From, string To)
    {
        /// <inheritdoc />
        public override string ToString() => $"{SheetName}: {From} -> {To}";
    }

    /// <summary>What one annotation run did.</summary>
    /// <param name="Changes">Every designator that moved, in walk order.</param>
    /// <param name="PlacementCount">How many symbol placements were considered.</param>
    /// <param name="ReferenceCount">How many distinct designators the design now has.</param>
    /// <param name="PrunedInstanceCount">How many stale instance entries were removed.</param>
    public sealed record AnnotationResult(
        IReadOnlyList<AnnotationChange> Changes,
        int PlacementCount,
        int ReferenceCount,
        int PrunedInstanceCount)
    {
        /// <summary>True when the design was already annotated and nothing moved.</summary>
        public bool IsUnchanged => Changes.Count == 0 && PrunedInstanceCount == 0;

        /// <summary>True when every placement ended up with a designator nothing else uses.</summary>
        public bool IsFullyAnnotated => ReferenceCount == PlacementCount;
    }

    /// <summary>
    /// Gives every part in a design a reference designator unique to the sheet instance it appears in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// KiCad has no <c>annotate</c> command — <c>kicad-cli</c> cannot do this, and the only thing it
    /// says about an unannotated design is one line on stderr from the netlist exporter. An
    /// unannotated hierarchy is not a cosmetic problem: two sheet instances of one file give their
    /// parts the same designators, the netlist merges those parts, and connections silently
    /// disappear. On the fixture in this repository a ground net loses half its nodes and ERC does
    /// not mention it.
    /// </para>
    /// <para>
    /// What this writes is the <c>(instances (project … (path … (reference …) (unit …))))</c> block
    /// of each symbol, one entry per sheet instance. It does not touch the <c>Reference</c>
    /// property: that is the designator the sheet was authored with, it is what the editor draws,
    /// and a symbol on a sheet used twice cannot have one correct value for it. The netlist reads
    /// the instance, which is what makes the difference.
    /// </para>
    /// </remarks>
    public static class SchematicAnnotator
    {
        /// <summary>
        /// Annotates a design in memory. Call <see cref="SchematicHierarchy.Save"/> to write it.
        /// </summary>
        /// <param name="hierarchy">The design.</param>
        /// <param name="options">Knobs, or <see langword="null"/> for the defaults.</param>
        /// <returns>What the run did.</returns>
        public static AnnotationResult Annotate(SchematicHierarchy hierarchy, AnnotationOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(hierarchy);
            options ??= new AnnotationOptions();

            var placements = hierarchy.Placements.ToList();
            var project = hierarchy.ProjectName;

            // What each placement is called now: the instance entry for its own sheet path when
            // there is one, and otherwise the Reference property the sheet was authored with.
            var current = new string[placements.Count];
            for (var i = 0; i < placements.Count; i++)
            {
                var (sheet, symbol) = placements[i];
                current[i] = symbol.GetInstanceReference(project, sheet.Path)
                    ?? symbol.ReferenceProperty
                    ?? "?";
            }

            var assigned = new string?[placements.Count];
            var taken = new HashSet<string>(StringComparer.Ordinal);
            var pending = new List<int>();

            // Pass one: keep what is already numbered and not already claimed. This is the whole of
            // the idempotency guarantee — after one run nothing collides, so a second run keeps
            // every designator and writes nothing.
            for (var i = 0; i < placements.Count; i++)
            {
                if (!options.AnnotatePowerSymbols && placements[i].Symbol.IsPowerSymbol)
                {
                    assigned[i] = current[i];
                    taken.Add(current[i]);
                    continue;
                }

                var designator = Designator.Parse(current[i]);
                if (options.KeepExistingReferences && designator.HasNumber && taken.Add(current[i]))
                {
                    assigned[i] = current[i];
                }
                else
                {
                    pending.Add(i);
                }
            }

            // Pass two: everything left gets the lowest free number for its own prefix, keeping the
            // zero padding it was written with so #PWR001 stays six characters wide.
            var next = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var i in pending)
            {
                var designator = Designator.Parse(current[i]);
                var number = next.TryGetValue(designator.Prefix, out var n) ? n : options.StartAt - 1;

                string candidate;
                do
                {
                    number++;
                    candidate = designator.With(number);
                }
                while (!taken.Add(candidate));

                next[designator.Prefix] = number;
                assigned[i] = candidate;
            }

            // Write. Nothing is written for a placement whose designator did not move, so an
            // already-annotated design leaves every file unmodified and Save() writes nothing.
            var changes = new List<AnnotationChange>();
            for (var i = 0; i < placements.Count; i++)
            {
                var (sheet, symbol) = placements[i];
                var reference = assigned[i]!;

                if (symbol.SetInstanceReference(project, sheet.Path, reference, symbol.Unit)
                    && !string.Equals(current[i], reference, StringComparison.Ordinal))
                {
                    changes.Add(new AnnotationChange(sheet.Path, sheet.DisplayPath, symbol.Uuid, current[i], reference));
                }
            }

            var pruned = 0;
            if (options.PruneStaleInstances)
            {
                pruned = Prune(hierarchy, project);
            }

            return new AnnotationResult(changes, placements.Count, taken.Count, pruned);
        }

        /// <summary>
        /// Loads a design, annotates it, and writes back the files that changed.
        /// </summary>
        /// <param name="rootSchematicPath">Path to the root <c>.kicad_sch</c>.</param>
        /// <param name="options">Knobs, or <see langword="null"/> for the defaults.</param>
        /// <returns>What the run did.</returns>
        public static AnnotationResult AnnotateFile(string rootSchematicPath, AnnotationOptions? options = null)
        {
            var hierarchy = SchematicHierarchy.Load(rootSchematicPath);
            var result = Annotate(hierarchy, options);
            hierarchy.Save();
            return result;
        }

        /// <summary>
        /// Reports the designators that are used by more than one placement, without changing anything.
        /// </summary>
        /// <param name="hierarchy">The design.</param>
        /// <returns>Each duplicated designator and the sheet instances that use it, ordered by designator.</returns>
        public static IReadOnlyList<KeyValuePair<string, IReadOnlyList<string>>> FindDuplicateReferences(SchematicHierarchy hierarchy)
        {
            ArgumentNullException.ThrowIfNull(hierarchy);
            var project = hierarchy.ProjectName;

            var bySheet = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (sheet, symbol) in hierarchy.Placements)
            {
                var reference = symbol.GetInstanceReference(project, sheet.Path) ?? symbol.ReferenceProperty ?? "?";
                if (!bySheet.TryGetValue(reference, out var sheets))
                {
                    bySheet[reference] = sheets = new List<string>();
                }

                sheets.Add(sheet.DisplayPath);
            }

            return bySheet
                .Where(pair => pair.Value.Count > 1)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new KeyValuePair<string, IReadOnlyList<string>>(pair.Key, pair.Value))
                .ToList();
        }

        private static int Prune(SchematicHierarchy hierarchy, string project)
        {
            // A file used by several sheet instances has several live paths; pruning has to know all
            // of them before it removes any, or annotating the second instance would delete the first.
            var liveBySchematic = new Dictionary<KiCadSchematic, HashSet<string>>();
            foreach (var sheet in hierarchy.SheetInstances)
            {
                if (!liveBySchematic.TryGetValue(sheet.Schematic, out var paths))
                {
                    liveBySchematic[sheet.Schematic] = paths = new HashSet<string>(StringComparer.Ordinal);
                }

                paths.Add(sheet.Path);
            }

            var pruned = 0;
            foreach (var (schematic, paths) in liveBySchematic)
            {
                foreach (var symbol in schematic.Symbols)
                {
                    pruned += symbol.PruneInstances(project, paths);
                }
            }

            return pruned;
        }

        /// <summary>A reference designator split into the prefix that must not change and the number that may.</summary>
        /// <param name="Prefix">Everything before the trailing digits.</param>
        /// <param name="Number">The trailing digits as a number, or 0 when there are none.</param>
        /// <param name="Padding">
        /// The width to pad a new number to, or 0 for none. Only a designator that was written with
        /// a leading zero is padded: <c>#PWR001</c> is three digits wide on purpose, whereas
        /// <c>J90</c> is just the number ninety and renumbering it to <c>J01</c> would be wrong.
        /// </param>
        private readonly record struct Designator(string Prefix, int Number, int Padding)
        {
            internal bool HasNumber { get; init; }

            internal static Designator Parse(string reference)
            {
                var end = reference.Length;
                while (end > 0 && char.IsAsciiDigit(reference[end - 1]))
                {
                    end--;
                }

                var digits = reference[end..];
                if (digits.Length == 0)
                {
                    // "R?" is KiCad's spelling of "not annotated"; the ? is not part of the prefix.
                    var bare = reference.EndsWith('?') ? reference[..^1] : reference;
                    return new Designator(bare, 0, 0) { HasNumber = false };
                }

                var padding = digits[0] == '0' ? digits.Length : 0;
                return new Designator(reference[..end], int.Parse(digits, CultureInfo.InvariantCulture), padding)
                {
                    HasNumber = true,
                };
            }

            /// <summary>Renders this prefix with a new number, keeping any zero padding it was written with.</summary>
            internal string With(int number)
            {
                var digits = number.ToString(CultureInfo.InvariantCulture);
                return Prefix + (Padding > 0 ? digits.PadLeft(Padding, '0') : digits);
            }
        }
    }
}
