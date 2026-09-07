using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KiCadSharp.Schematics
{
    /// <summary>
    /// One place a sheet appears in a design.
    /// </summary>
    /// <remarks>
    /// A sheet <em>file</em> and a sheet <em>instance</em> are different things, and conflating them
    /// is what produces duplicate reference designators: one file instantiated twice is two
    /// instances, each of which needs its own designators, and both of them are written back into
    /// that one file.
    /// </remarks>
    public sealed class SheetInstance
    {
        internal SheetInstance(string path, IReadOnlyList<string> names, KiCadSchematic schematic)
        {
            Path = path;
            Names = names;
            Schematic = schematic;
        }

        /// <summary>
        /// The hierarchical path KiCad files this instance's designators under: the root sheet's
        /// UUID followed by the UUID of every <c>(sheet ...)</c> stepped through, <c>/</c>-separated
        /// and leading-slashed, e.g. <c>/6ba5fed3-…/afa5cb65-…</c>.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// The human-readable path — the <c>Sheetname</c> of each sheet stepped through. Empty for
        /// the root sheet, which has no name of its own.
        /// </summary>
        public IReadOnlyList<string> Names { get; }

        /// <summary>The file this instance shows. Two instances of one file share this object.</summary>
        public KiCadSchematic Schematic { get; }

        /// <summary>Gets the display path, e.g. <c>/PWR1</c>.</summary>
        public string DisplayPath => Names.Count == 0 ? "/" : "/" + string.Join("/", Names);

        /// <inheritdoc />
        public override string ToString() => DisplayPath;
    }

    /// <summary>One symbol as it appears in one sheet instance.</summary>
    /// <param name="Sheet">The sheet instance it appears in.</param>
    /// <param name="Symbol">The symbol in the file.</param>
    public readonly record struct SymbolPlacement(SheetInstance Sheet, KiCadSchematicSymbol Symbol);

    /// <summary>
    /// A whole design: a root schematic and every sheet instance reachable from it.
    /// </summary>
    /// <remarks>
    /// Files are loaded once and shared, so a sheet instantiated twice is one
    /// <see cref="KiCadSchematic"/> reached by two <see cref="SheetInstance"/>s. That is what lets a
    /// change made for one instance and a change made for the other both land in the one file.
    /// </remarks>
    public sealed class SchematicHierarchy
    {
        private const int MaxDepth = 64;

        private readonly Dictionary<string, KiCadSchematic> _byPath;

        private SchematicHierarchy(
            string projectName,
            KiCadSchematic root,
            IReadOnlyList<SheetInstance> sheetInstances,
            Dictionary<string, KiCadSchematic> byPath)
        {
            ProjectName = projectName;
            Root = root;
            SheetInstances = sheetInstances;
            _byPath = byPath;
        }

        /// <summary>
        /// The project name KiCad files instance data under. Taken from the <c>.kicad_pro</c> beside
        /// the root schematic when there is one, otherwise from the root schematic's file name.
        /// </summary>
        public string ProjectName { get; }

        /// <summary>The root schematic.</summary>
        public KiCadSchematic Root { get; }

        /// <summary>
        /// Every sheet instance in the design, depth first in document order, starting with the root
        /// sheet itself.
        /// </summary>
        public IReadOnlyList<SheetInstance> SheetInstances { get; }

        /// <summary>Every distinct schematic file in the design.</summary>
        public IReadOnlyCollection<KiCadSchematic> Schematics => _byPath.Values;

        /// <summary>
        /// Every symbol placement in the design: one entry per symbol per sheet instance, in walk
        /// order. A symbol in a file instantiated twice appears twice.
        /// </summary>
        public IEnumerable<SymbolPlacement> Placements
        {
            get
            {
                foreach (var sheet in SheetInstances)
                {
                    foreach (var symbol in sheet.Schematic.Symbols)
                    {
                        yield return new SymbolPlacement(sheet, symbol);
                    }
                }
            }
        }

        /// <summary>
        /// Walks a design from its root schematic, loading every sheet file it reaches.
        /// </summary>
        /// <param name="rootSchematicPath">Path to the root <c>.kicad_sch</c>.</param>
        /// <returns>The hierarchy.</returns>
        /// <exception cref="FileNotFoundException">A sheet names a file that is not there.</exception>
        /// <exception cref="InvalidOperationException">The hierarchy is deeper than 64 sheets, which means it is recursive.</exception>
        public static SchematicHierarchy Load(string rootSchematicPath)
        {
            ArgumentNullException.ThrowIfNull(rootSchematicPath);
            var rootFull = Path.GetFullPath(rootSchematicPath);

            var byPath = new Dictionary<string, KiCadSchematic>(StringComparer.Ordinal);
            var root = Open(rootFull, byPath);

            var instances = new List<SheetInstance>();
            Walk(root, "/" + root.Uuid, Array.Empty<string>(), instances, byPath, 0);

            return new SchematicHierarchy(ProjectNameFor(rootFull), root, instances, byPath);
        }

        /// <summary>Writes back every file in the design that changed. Untouched files are not rewritten.</summary>
        /// <returns>The paths that were written.</returns>
        public IReadOnlyList<string> Save()
        {
            var written = new List<string>();
            foreach (var (path, schematic) in _byPath)
            {
                if (schematic.IsModified)
                {
                    schematic.Save(path);
                    written.Add(path);
                }
            }

            return written;
        }

        private static void Walk(
            KiCadSchematic schematic,
            string path,
            IReadOnlyList<string> names,
            List<SheetInstance> instances,
            Dictionary<string, KiCadSchematic> byPath,
            int depth)
        {
            if (depth > MaxDepth)
            {
                throw new InvalidOperationException(
                    $"Sheet hierarchy is more than {MaxDepth} deep at '{path}'; a sheet probably instantiates itself.");
            }

            instances.Add(new SheetInstance(path, names, schematic));

            var directory = Path.GetDirectoryName(schematic.FilePath!)!;
            foreach (var sheet in schematic.Sheets)
            {
                var file = sheet.SheetFile;
                if (string.IsNullOrEmpty(file))
                {
                    continue;
                }

                var childPath = Path.GetFullPath(Path.Combine(directory, file));
                if (!File.Exists(childPath))
                {
                    throw new FileNotFoundException(
                        $"Sheet '{sheet.SheetName ?? sheet.Uuid}' in '{schematic.FilePath}' names '{file}', which is not there.",
                        childPath);
                }

                var childNames = new List<string>(names) { sheet.SheetName ?? sheet.Uuid };
                Walk(Open(childPath, byPath), path + "/" + sheet.Uuid, childNames, instances, byPath, depth + 1);
            }
        }

        private static KiCadSchematic Open(string fullPath, Dictionary<string, KiCadSchematic> byPath)
        {
            if (byPath.TryGetValue(fullPath, out var existing))
            {
                return existing;
            }

            var schematic = KiCadSchematic.Load(fullPath);
            byPath[fullPath] = schematic;
            return schematic;
        }

        private static string ProjectNameFor(string rootSchematicPath)
        {
            var directory = Path.GetDirectoryName(rootSchematicPath);
            var stem = Path.GetFileNameWithoutExtension(rootSchematicPath);

            if (directory is not null && File.Exists(Path.Combine(directory, stem + KiCadFileExtensions.Project)))
            {
                return stem;
            }

            if (directory is not null)
            {
                var project = Directory.GetFiles(directory, "*" + KiCadFileExtensions.Project).OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
                if (project is not null)
                {
                    return Path.GetFileNameWithoutExtension(project);
                }
            }

            return stem;
        }
    }
}
