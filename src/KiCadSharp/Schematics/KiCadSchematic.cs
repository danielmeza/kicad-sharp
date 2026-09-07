using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Schematics
{
    /// <summary>
    /// One schematic sheet file (<c>.kicad_sch</c>), as a view over its s-expression tree.
    /// </summary>
    /// <remarks>
    /// Like the rest of the document layer this holds no state of its own: it reads and writes the
    /// parsed document, so saving a sheet nobody changed reproduces it byte for byte.
    /// </remarks>
    public sealed class KiCadSchematic : KiCadNode
    {
        private readonly SDocument _document;

        private KiCadSchematic(SDocument document, SExpression root, string? filePath)
            : base(root)
        {
            _document = document;
            FilePath = filePath;
        }

        /// <summary>Gets the whole parsed file.</summary>
        public SDocument Document => _document;

        /// <summary>Gets the path the sheet was loaded from, or <see langword="null"/> when it was parsed from text.</summary>
        public string? FilePath { get; }

        /// <summary>Gets the sheet's own UUID — the first segment of every path inside it.</summary>
        public string Uuid => ReadChild("uuid") ?? string.Empty;

        /// <summary>Gets the symbols placed on this sheet, in document order.</summary>
        public KiCadNodeList<KiCadSchematicSymbol> Symbols => new(Node, "symbol", n => new KiCadSchematicSymbol(n));

        /// <summary>Gets the sub-sheets this sheet instantiates, in document order.</summary>
        public KiCadNodeList<KiCadSheet> Sheets => new(Node, "sheet", n => new KiCadSheet(n));

        /// <summary>True when anything in the file has been changed since it was parsed.</summary>
        public bool IsModified => _document.IsModified;

        /// <summary>Loads a schematic from a file.</summary>
        /// <param name="filePath">Path to the <c>.kicad_sch</c>.</param>
        /// <returns>The schematic.</returns>
        /// <exception cref="InvalidOperationException">The file is not a schematic.</exception>
        public static KiCadSchematic Load(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);
            var full = Path.GetFullPath(filePath);
            return From(SDocument.Load(full), full);
        }

        /// <summary>Loads a schematic from a file, reading it asynchronously.</summary>
        /// <param name="filePath">Path to the <c>.kicad_sch</c>.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The schematic.</returns>
        public static async Task<KiCadSchematic> LoadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(filePath);
            var full = Path.GetFullPath(filePath);
            return From(await SDocument.LoadAsync(full, cancellationToken).ConfigureAwait(false), full);
        }

        /// <summary>Parses a schematic from text.</summary>
        /// <param name="text">The file contents.</param>
        /// <returns>The schematic.</returns>
        public static KiCadSchematic Parse(string text) => From(SDocument.Parse(text), null);

        /// <summary>Writes the schematic back out.</summary>
        /// <param name="filePath">Where to write, or <see langword="null"/> for where it was loaded from.</param>
        /// <exception cref="InvalidOperationException">No path was given and the schematic was parsed from text.</exception>
        public void Save(string? filePath = null)
        {
            var target = filePath ?? FilePath
                ?? throw new InvalidOperationException("This schematic was not loaded from a file; pass a path to save it.");
            _document.Save(target);
        }

        /// <summary>Renders the schematic to text.</summary>
        /// <returns>The file contents.</returns>
        public string ToText() => _document.ToText();

        private static KiCadSchematic From(SDocument document, string? filePath)
        {
            var root = document.Root
                ?? throw new InvalidOperationException($"'{filePath ?? "<text>"}' holds no s-expression.");

            if (!string.Equals(root.Token, "kicad_sch", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected a (kicad_sch ...) file but the root form is ({root.Token} ...).");
            }

            return new KiCadSchematic(document, root, filePath);
        }
    }

    /// <summary>
    /// A sub-sheet placed on a schematic: <c>(sheet (uuid ...) (property "Sheetname" ...) (property "Sheetfile" ...))</c>.
    /// </summary>
    public sealed class KiCadSheet : KiCadNode
    {
        /// <summary>Creates a view over a <c>(sheet ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadSheet(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets the sheet's UUID — the segment it contributes to a hierarchical path.</summary>
        public string Uuid => ReadChild("uuid") ?? string.Empty;

        /// <summary>Gets the sheet's display name, the <c>Sheetname</c> property.</summary>
        public string? SheetName => GetPropertyValue("Sheetname");

        /// <summary>Gets the file this sheet instantiates, the <c>Sheetfile</c> property, as written.</summary>
        public string? SheetFile => GetPropertyValue("Sheetfile");

        /// <summary>Gets the sheet's fields.</summary>
        public KiCadNodeList<KiCadProperty> Properties => new(Node, "property", n => new KiCadProperty(n));

        /// <summary>Gets the value of a named field.</summary>
        /// <param name="key">The field key.</param>
        /// <returns>The value, or <see langword="null"/>.</returns>
        public string? GetPropertyValue(string key) =>
            Properties.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal))?.Value;
    }

    /// <summary>
    /// A symbol placed on a schematic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reference designator that matters is <em>not</em> the <c>Reference</c> property. It lives
    /// in the <c>(instances ...)</c> block, keyed by project name and by the full path of the sheet
    /// instance the symbol appears in — and one symbol in one file has as many of them as the sheet
    /// has instances. The property is what the editor shows and what KiCad falls back to when the
    /// path is not listed; the netlist reads the instance.
    /// </para>
    /// </remarks>
    public sealed class KiCadSchematicSymbol : KiCadNode
    {
        /// <summary>Creates a view over a <c>(symbol ...)</c> form on a sheet.</summary>
        /// <param name="node">The form.</param>
        public KiCadSchematicSymbol(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets the symbol's UUID, unique within its file.</summary>
        public string Uuid => ReadChild("uuid") ?? string.Empty;

        /// <summary>Gets the library symbol this is an instance of, e.g. <c>orbion:Conn_01x02</c>.</summary>
        public string LibId => ReadChild("lib_id") ?? string.Empty;

        /// <summary>Gets the unit of a multi-unit symbol; 1 for a single-unit part.</summary>
        public int Unit => Node.GetChild("unit") is { } unit && unit.TryGetValue<int>(0, out var value) ? value : 1;

        /// <summary>Gets the symbol's fields.</summary>
        public KiCadNodeList<KiCadProperty> Properties => new(Node, "property", n => new KiCadProperty(n));

        /// <summary>
        /// Gets or sets the <c>Reference</c> property — what the editor draws next to the symbol, and
        /// what KiCad falls back to when the sheet path has no instance entry.
        /// </summary>
        public string? ReferenceProperty
        {
            get => GetPropertyValue("Reference");
            set
            {
                var property = Properties.FirstOrDefault(p => string.Equals(p.Key, "Reference", StringComparison.Ordinal));
                if (property is not null && value is not null)
                {
                    property.Value = value;
                }
            }
        }

        /// <summary>True when this is a power or flag symbol, whose designator starts with <c>#</c>.</summary>
        public bool IsPowerSymbol => ReferenceProperty?.StartsWith('#') == true;

        /// <summary>Gets the value of a named field.</summary>
        /// <param name="key">The field key.</param>
        /// <returns>The value, or <see langword="null"/>.</returns>
        public string? GetPropertyValue(string key) =>
            Properties.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal))?.Value;

        /// <summary>
        /// Reads the reference designator this symbol carries in one sheet instance.
        /// </summary>
        /// <param name="project">The project name the instance is filed under.</param>
        /// <param name="sheetPath">The full path of the sheet instance, e.g. <c>/root-uuid/sheet-uuid</c>.</param>
        /// <returns>The reference, or <see langword="null"/> when that path has no entry.</returns>
        public string? GetInstanceReference(string project, string sheetPath) =>
            FindPath(project, sheetPath)?.GetChildValue("reference");

        /// <summary>
        /// Writes the reference designator for one sheet instance, creating the <c>instances</c>,
        /// <c>project</c> and <c>path</c> forms it needs and leaving every other project alone.
        /// </summary>
        /// <param name="project">The project name to file the instance under.</param>
        /// <param name="sheetPath">The full path of the sheet instance.</param>
        /// <param name="reference">The reference designator.</param>
        /// <param name="unit">The unit of a multi-unit symbol.</param>
        /// <returns>True when anything actually changed.</returns>
        public bool SetInstanceReference(string project, string sheetPath, string reference, int unit)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(sheetPath);
            ArgumentNullException.ThrowIfNull(reference);

            var path = FindPath(project, sheetPath);
            if (path is not null
                && string.Equals(path.GetChildValue("reference"), reference, StringComparison.Ordinal)
                && path.GetChild("unit")?.GetValue(0) == unit.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                return false;
            }

            path ??= CreatePath(project, sheetPath);
            path.SetChildValue("reference", reference, SQuoteStyle.Quoted);
            path.SetChildValue("unit", unit.ToString(System.Globalization.CultureInfo.InvariantCulture), SQuoteStyle.Bare);
            return true;
        }

        /// <summary>
        /// Removes the instance entries filed under <paramref name="project"/> whose sheet path is not
        /// in <paramref name="livePaths"/>. Entries under any other project are left alone.
        /// </summary>
        /// <param name="project">The project name to prune within.</param>
        /// <param name="livePaths">The sheet paths that still exist.</param>
        /// <returns>How many entries were removed.</returns>
        public int PruneInstances(string project, IReadOnlyCollection<string> livePaths)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(livePaths);

            var projectNode = FindProject(project);
            if (projectNode is null)
            {
                return 0;
            }

            var stale = projectNode.GetChildren("path")
                .Where(p => p.GetValue(0) is not { } value || !livePaths.Contains(value))
                .ToList();

            foreach (var path in stale)
            {
                projectNode.Children.Remove(path);
            }

            return stale.Count;
        }

        private SExpression? FindProject(string project) =>
            Node.GetChild("instances")?
                .GetChildren("project")
                .FirstOrDefault(p => string.Equals(p.GetValue(0), project, StringComparison.Ordinal));

        private SExpression? FindPath(string project, string sheetPath) =>
            FindProject(project)?
                .GetChildren("path")
                .FirstOrDefault(p => string.Equals(p.GetValue(0), sheetPath, StringComparison.Ordinal));

        private SExpression CreatePath(string project, string sheetPath)
        {
            var instances = Node.GetChild("instances") ?? Node.CreateChild("instances");
            var projectNode = FindProject(project);
            if (projectNode is null)
            {
                projectNode = instances.CreateChild("project");
                projectNode.AddValue(project, SQuoteStyle.Quoted);
            }

            var path = projectNode.CreateChild("path");
            path.AddValue(sheetPath, SQuoteStyle.Quoted);
            return path;
        }
    }
}
