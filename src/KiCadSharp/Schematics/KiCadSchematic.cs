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
        public string Uuid => ReadChild(KiCadTokens.Common.Uuid) ?? string.Empty;

        /// <summary>Gets the symbols placed on this sheet, in document order.</summary>
        public KiCadNodeList<KiCadSchematicSymbol> Symbols => new(Node, KiCadTokens.Common.Symbol, n => new KiCadSchematicSymbol(n));

        /// <summary>Gets the sub-sheets this sheet instantiates, in document order.</summary>
        public KiCadNodeList<KiCadSheet> Sheets => new(Node, KiCadTokens.Schematic.Sheet, n => new KiCadSheet(n));

        // ------------------------------------------------------------------------------- the header

        /// <summary>Gets or sets the file-format version, the date stamp KiCad bumps on every format change.</summary>
        public string Version
        {
            get => ReadChild(KiCadTokens.Common.Version) ?? string.Empty;
            set => WriteChild(KiCadTokens.Common.Version, value, SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets the name of the program that wrote the file, e.g. <c>eeschema</c>.</summary>
        public string Generator
        {
            get => ReadChild(KiCadTokens.Common.Generator) ?? string.Empty;
            set => WriteChild(KiCadTokens.Common.Generator, value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets or sets the generator's own version, e.g. <c>"10.0"</c>. KiCad 7 added it, so it is
        /// <see langword="null"/> on anything older.
        /// </summary>
        public string? GeneratorVersion
        {
            get => ReadChild(KiCadTokens.Common.GeneratorVersion);
            set => WriteChild(KiCadTokens.Common.GeneratorVersion, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the paper size, e.g. <c>A4</c> or <c>A3</c>, or a <c>User</c> size with its dimensions.</summary>
        public string? Paper
        {
            get => ReadChild(KiCadTokens.Common.Paper);
            set => WriteChild(KiCadTokens.Common.Paper, value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets the drawing-frame text, or <see langword="null"/> when the sheet has no
        /// <c>(title_block ...)</c>.
        /// </summary>
        /// <remarks>
        /// Reading this does not add the form. KiCad omits <c>(title_block ...)</c> entirely when
        /// every field is empty — 30 of the 134 sheets KiCad 10 itself ships have none — so a getter
        /// that created one would dirty a document nobody edited and put an empty
        /// <c>(title_block)</c> into the next save. Use <see cref="RequireTitleBlock"/> to give a
        /// sheet its first title.
        /// </remarks>
        public KiCadTitleBlock? TitleBlock =>
            Node.GetChild(KiCadTokens.Common.TitleBlock) is { } node ? new KiCadTitleBlock(node) : null;

        /// <summary>Gets the drawing-frame text, adding an empty <c>(title_block ...)</c> when the sheet has none.</summary>
        /// <returns>The view.</returns>
        public KiCadTitleBlock RequireTitleBlock() => new(Require(KiCadTokens.Common.TitleBlock));

        /// <summary>Gets or sets whether the fonts the sheet uses are embedded in the file.</summary>
        public bool EmbeddedFonts
        {
            get => ReadFlag(KiCadTokens.Common.EmbeddedFonts);
            set => WriteFlag(KiCadTokens.Common.EmbeddedFonts, value);
        }

        // ------------------------------------------------------------------------------ connectivity

        /// <summary>Gets the wire segments drawn on this sheet, in document order.</summary>
        public KiCadNodeList<KiCadWire> Wires => new(Node, KiCadTokens.Schematic.Wire, n => new KiCadWire(n));

        /// <summary>Gets the bus segments drawn on this sheet, in document order.</summary>
        public KiCadNodeList<KiCadBus> Buses => new(Node, KiCadTokens.Schematic.Bus, n => new KiCadBus(n));

        /// <summary>Gets the stubs that tap signals off a bus.</summary>
        public KiCadNodeList<KiCadBusEntry> BusEntries => new(Node, KiCadTokens.Schematic.BusEntry, n => new KiCadBusEntry(n));

        /// <summary>Gets the bus aliases declared in this file. They are file-scoped, not project-scoped.</summary>
        public KiCadNodeList<KiCadBusAlias> BusAliases => new(Node, KiCadTokens.Schematic.BusAlias, n => new KiCadBusAlias(n));

        /// <summary>Gets the junction dots, in document order.</summary>
        public KiCadNodeList<KiCadJunction> Junctions => new(Node, KiCadTokens.Schematic.Junction, n => new KiCadJunction(n));

        /// <summary>Gets the no-connect markers, in document order.</summary>
        public KiCadNodeList<KiCadNoConnect> NoConnects => new(Node, KiCadTokens.Schematic.NoConnect, n => new KiCadNoConnect(n));

        // ------------------------------------------------------------------------------------ naming

        /// <summary>Gets the sheet-local net labels, in document order.</summary>
        public KiCadNodeList<KiCadLabel> Labels => new(Node, KiCadTokens.Schematic.Label, n => new KiCadLabel(n));

        /// <summary>Gets the global net labels — the ones that join across every sheet in the design.</summary>
        public KiCadNodeList<KiCadGlobalLabel> GlobalLabels => new(Node, KiCadTokens.Schematic.GlobalLabel, n => new KiCadGlobalLabel(n));

        /// <summary>Gets the hierarchical labels, each the child half of a parent sheet's pin.</summary>
        public KiCadNodeList<KiCadHierarchicalLabel> HierarchicalLabels =>
            new(Node, KiCadTokens.Schematic.HierarchicalLabel, n => new KiCadHierarchicalLabel(n));

        /// <summary>Gets the net-class flags placed on this sheet.</summary>
        public KiCadNodeList<KiCadNetClassFlag> NetClassFlags => new(Node, KiCadTokens.Schematic.NetClassFlag, n => new KiCadNetClassFlag(n));

        // ------------------------------------------------------------------------------- what is drawn

        /// <summary>Gets the free text drawn on this sheet, in document order.</summary>
        public KiCadNodeList<KiCadSchematicText> TextItems => new(Node, KiCadTokens.Common.Text, n => new KiCadSchematicText(n));

        /// <summary>Gets the text boxes drawn on this sheet.</summary>
        public KiCadNodeList<KiCadTextBox> TextBoxes => new(Node, KiCadTokens.Common.TextBox, n => new KiCadTextBox(n));

        /// <summary>
        /// Gets the polylines drawn directly on this sheet. The view is the same one a symbol's
        /// polylines use; the sheet-only <c>(uuid ...)</c> is reachable through
        /// <c>item.Node.GetChildValue(KiCadTokens.Common.Uuid)</c>.
        /// </summary>
        public KiCadNodeList<KiCadPolyline> Polylines => new(Node, KiCadTokens.Common.Polyline, n => new KiCadPolyline(n));

        /// <summary>Gets the rectangles drawn directly on this sheet — usually the boxes around a functional block.</summary>
        public KiCadNodeList<KiCadRectangle> Rectangles => new(Node, KiCadTokens.Common.Rectangle, n => new KiCadRectangle(n));

        /// <summary>Gets the circles drawn directly on this sheet.</summary>
        public KiCadNodeList<KiCadCircle> Circles => new(Node, KiCadTokens.Common.Circle, n => new KiCadCircle(n));

        /// <summary>Gets the arcs drawn directly on this sheet.</summary>
        public KiCadNodeList<KiCadArc> Arcs => new(Node, KiCadTokens.Common.Arc, n => new KiCadArc(n));

        /// <summary>Gets the Bézier curves drawn directly on this sheet.</summary>
        public KiCadNodeList<KiCadBezier> Beziers => new(Node, KiCadTokens.Common.Bezier, n => new KiCadBezier(n));

        /// <summary>Gets the bitmaps placed on this sheet.</summary>
        public KiCadNodeList<KiCadImage> Images => new(Node, KiCadTokens.Schematic.Image, n => new KiCadImage(n));

        // ---------------------------------------------------------------------------------- the rest

        /// <summary>
        /// Gets the library symbols cached in this file, as the same view a <c>.kicad_sym</c> uses.
        /// </summary>
        /// <remarks>
        /// KiCad copies every symbol a sheet places into <c>(lib_symbols ...)</c> so the file opens
        /// without its libraries. These are therefore definitions, not placements —
        /// <see cref="Symbols"/> is what is actually on the sheet — and the two lists cannot be
        /// confused because the cache is nested one level down.
        /// </remarks>
        public KiCadNodeList<KiCadSymbol> LibrarySymbols =>
            new(Node.GetChild(KiCadTokens.Schematic.LibSymbols), KiCadTokens.Common.Symbol, n => new KiCadSymbol(n));

        /// <summary>Gets the symbol cache as a mutable list, adding the <c>(lib_symbols ...)</c> form when the file has none.</summary>
        /// <returns>The live list.</returns>
        public KiCadNodeList<KiCadSymbol> RequireLibrarySymbols() =>
            new(Require(KiCadTokens.Schematic.LibSymbols), KiCadTokens.Common.Symbol, n => new KiCadSymbol(n));

        /// <summary>
        /// Gets the page numbers this file assigns, one per hierarchical path. On a root sheet the
        /// list holds every path in the design; on a child sheet it holds only <c>"/"</c>.
        /// </summary>
        /// <remarks>
        /// A child sheet in a hierarchy carries no <c>(sheet_instances ...)</c> at all — 79 of the
        /// 134 sheets KiCad 10 ships have none — so reading this must not create one. Use
        /// <see cref="RequireSheetInstances"/> when you mean to write the page map.
        /// </remarks>
        public KiCadNodeList<KiCadSheetInstance> SheetInstances =>
            new(Node.GetChild(KiCadTokens.Schematic.SheetInstances), KiCadTokens.Schematic.Path, n => new KiCadSheetInstance(n));

        /// <summary>Gets the page map as a mutable list, adding the <c>(sheet_instances ...)</c> form when the file has none.</summary>
        /// <returns>The live list.</returns>
        public KiCadNodeList<KiCadSheetInstance> RequireSheetInstances() =>
            new(Require(KiCadTokens.Schematic.SheetInstances), KiCadTokens.Schematic.Path, n => new KiCadSheetInstance(n));

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

            if (!string.Equals(root.Token, KiCadTokens.Schematic.Root, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Expected a ({KiCadTokens.Schematic.Root} ...) file but the root form is ({root.Token} ...).");
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
        public string Uuid => ReadChild(KiCadTokens.Common.Uuid) ?? string.Empty;

        /// <summary>Gets or sets the top-left corner of the box drawn for the sheet.</summary>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.At));
            set => value.Write(Require(KiCadTokens.Common.At), includeRotation: false);
        }

        /// <summary>Gets or sets the box's extent, in millimetres. The pins sit on its edges.</summary>
        public KiCadSize Size
        {
            get => KiCadSize.Read(Node.GetChild(KiCadTokens.Common.Size));
            set => value.Write(Require(KiCadTokens.Common.Size));
        }

        /// <summary>Gets the box's border, or <see langword="null"/> when it carries no <c>(stroke ...)</c>.</summary>
        public KiCadStroke? Stroke => Node.GetChild(KiCadTokens.Common.Stroke) is { } node ? new KiCadStroke(node) : null;

        /// <summary>Gets the <c>(stroke ...)</c> form, adding an empty one when the sheet has none.</summary>
        /// <returns>The view.</returns>
        public KiCadStroke RequireStroke() => new(Require(KiCadTokens.Common.Stroke));

        /// <summary>Gets the box's fill, or <see langword="null"/> when it carries no <c>(fill ...)</c>.</summary>
        public KiCadFill? Fill => Node.GetChild(KiCadTokens.Common.Fill) is { } node ? new KiCadFill(node) : null;

        /// <summary>Gets the <c>(fill ...)</c> form, adding an empty one when the sheet has none.</summary>
        /// <returns>The view.</returns>
        public KiCadFill RequireFill() => new(Require(KiCadTokens.Common.Fill));

        /// <summary>Gets or sets whether the sheet's contents are excluded from the simulator.</summary>
        public bool ExcludeFromSim
        {
            get => ReadFlag(KiCadTokens.Schematic.ExcludeFromSim);
            set => WriteFlag(KiCadTokens.Schematic.ExcludeFromSim, value);
        }

        /// <summary>Gets or sets whether the sheet's contents reach the bill of materials.</summary>
        public bool InBom
        {
            get => ReadFlag(KiCadTokens.Common.InBom);
            set => WriteFlag(KiCadTokens.Common.InBom, value);
        }

        /// <summary>Gets or sets whether the sheet's contents reach the board.</summary>
        public bool OnBoard
        {
            get => ReadFlag(KiCadTokens.Common.OnBoard);
            set => WriteFlag(KiCadTokens.Common.OnBoard, value);
        }

        /// <summary>Gets or sets whether the sheet's contents are marked do-not-populate.</summary>
        public bool Dnp
        {
            get => ReadFlag(KiCadTokens.Schematic.Dnp);
            set => WriteFlag(KiCadTokens.Schematic.Dnp, value);
        }

        /// <summary>Gets the sheet's display name, the <c>Sheetname</c> property.</summary>
        public string? SheetName => GetPropertyValue(KiCadPropertyNames.SheetName);

        /// <summary>Gets the file this sheet instantiates, the <c>Sheetfile</c> property, as written.</summary>
        public string? SheetFile => GetPropertyValue(KiCadPropertyNames.SheetFile);

        /// <summary>Gets the sheet's fields.</summary>
        public KiCadNodeList<KiCadProperty> Properties => new(Node, KiCadTokens.Common.Property, n => new KiCadProperty(n));

        /// <summary>
        /// Gets the connection points drawn on the sheet's box. Each one is joined to the child file
        /// by name, through a hierarchical label spelled the same way.
        /// </summary>
        public KiCadNodeList<KiCadSheetPin> Pins => new(Node, KiCadTokens.Common.Pin, n => new KiCadSheetPin(n));

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
        public string Uuid => ReadChild(KiCadTokens.Common.Uuid) ?? string.Empty;

        /// <summary>Gets the library symbol this is an instance of, e.g. <c>orbion:Conn_01x02</c>.</summary>
        public string LibId => ReadChild(KiCadTokens.Schematic.LibId) ?? string.Empty;

        /// <summary>Gets the unit of a multi-unit symbol; 1 for a single-unit part.</summary>
        public int Unit => Node.GetChild(KiCadTokens.Schematic.Unit) is { } unit && unit.TryGetValue<int>(0, out var value) ? value : 1;

        /// <summary>
        /// Gets or sets where the symbol sits and how far it is turned, in millimetres and degrees.
        /// </summary>
        /// <remarks>
        /// This is the origin the library symbol's own geometry is drawn around, not the outline's
        /// corner, so two symbols of different sizes at the same <c>(at ...)</c> overlap at their
        /// pins' reference point rather than at their edges.
        /// </remarks>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.At));
            set => value.Write(Require(KiCadTokens.Common.At), includeRotation: true);
        }

        /// <summary>
        /// Gets or sets the axis the symbol is flipped about — <c>"x"</c> or <c>"y"</c> — or
        /// <see langword="null"/> when it is not mirrored. Setting <see langword="null"/> removes the
        /// form, which is how KiCad spells "not mirrored".
        /// </summary>
        public string? Mirror
        {
            get => ReadChild(KiCadTokens.Schematic.Mirror);
            set => WriteChild(KiCadTokens.Schematic.Mirror, value, SQuoteStyle.Bare);
        }

        /// <summary>
        /// Gets the body style of a symbol drawn two ways (the De Morgan alternative); 1 is the
        /// normal body. KiCad 9 renamed the token from <c>convert</c> to <c>body_style</c>, and both
        /// spellings are read.
        /// </summary>
        public int BodyStyle =>
            (Node.GetChild(KiCadTokens.Schematic.BodyStyle) ?? Node.GetChild(KiCadTokens.Schematic.Convert)) is { } style
            && style.TryGetValue<int>(0, out var value)
                ? value
                : 1;

        /// <summary>
        /// Gets the library symbol's own name when it differs from <see cref="LibId"/>, which is what
        /// KiCad writes for a symbol edited only on this sheet; <see langword="null"/> otherwise.
        /// </summary>
        public string? LibName => ReadChild(KiCadTokens.Schematic.LibName);

        /// <summary>Gets or sets whether the simulator ignores this symbol.</summary>
        public bool ExcludeFromSim
        {
            get => ReadFlag(KiCadTokens.Schematic.ExcludeFromSim);
            set => WriteFlag(KiCadTokens.Schematic.ExcludeFromSim, value);
        }

        /// <summary>Gets or sets whether the symbol reaches the bill of materials.</summary>
        public bool InBom
        {
            get => ReadFlag(KiCadTokens.Common.InBom);
            set => WriteFlag(KiCadTokens.Common.InBom, value);
        }

        /// <summary>Gets or sets whether the symbol reaches the board.</summary>
        public bool OnBoard
        {
            get => ReadFlag(KiCadTokens.Common.OnBoard);
            set => WriteFlag(KiCadTokens.Common.OnBoard, value);
        }

        /// <summary>Gets or sets whether the symbol is marked do-not-populate.</summary>
        public bool Dnp
        {
            get => ReadFlag(KiCadTokens.Schematic.Dnp);
            set => WriteFlag(KiCadTokens.Schematic.Dnp, value);
        }

        /// <summary>Gets or sets whether the symbol reaches the component-placement file.</summary>
        public bool InPosFiles
        {
            get => ReadFlag(KiCadTokens.Schematic.InPosFiles);
            set => WriteFlag(KiCadTokens.Schematic.InPosFiles, value);
        }

        /// <summary>Gets the symbol's fields.</summary>
        public KiCadNodeList<KiCadProperty> Properties => new(Node, KiCadTokens.Common.Property, n => new KiCadProperty(n));

        /// <summary>
        /// Gets or sets the <c>Reference</c> property — what the editor draws next to the symbol, and
        /// what KiCad falls back to when the sheet path has no instance entry.
        /// </summary>
        public string? ReferenceProperty
        {
            get => GetPropertyValue(KiCadPropertyNames.Reference);
            set
            {
                var property = Properties.FirstOrDefault(p => string.Equals(p.Key, KiCadPropertyNames.Reference, StringComparison.Ordinal));
                if (property is not null && value is not null)
                {
                    property.Value = value;
                }
            }
        }

        /// <summary>True when this is a power or flag symbol, whose designator starts with <c>#</c>.</summary>
        public bool IsPowerSymbol => ReferenceProperty?.StartsWith(KiCadDefaults.GeneratedReferencePrefix) == true;

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
            FindPath(project, sheetPath)?.GetChildValue(KiCadTokens.Schematic.Reference);

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
                && string.Equals(path.GetChildValue(KiCadTokens.Schematic.Reference), reference, StringComparison.Ordinal)
                && path.GetChild(KiCadTokens.Schematic.Unit)?.GetValue(0) == unit.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                return false;
            }

            path ??= CreatePath(project, sheetPath);
            path.SetChildValue(KiCadTokens.Schematic.Reference, reference, SQuoteStyle.Quoted);
            path.SetChildValue(KiCadTokens.Schematic.Unit, unit.ToString(System.Globalization.CultureInfo.InvariantCulture), SQuoteStyle.Bare);
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

            var stale = projectNode.GetChildren(KiCadTokens.Schematic.Path)
                .Where(p => p.GetValue(0) is not { } value || !livePaths.Contains(value))
                .ToList();

            foreach (var path in stale)
            {
                projectNode.Children.Remove(path);
            }

            return stale.Count;
        }

        private SExpression? FindProject(string project) =>
            Node.GetChild(KiCadTokens.Schematic.Instances)?
                .GetChildren(KiCadTokens.Schematic.Project)
                .FirstOrDefault(p => string.Equals(p.GetValue(0), project, StringComparison.Ordinal));

        private SExpression? FindPath(string project, string sheetPath) =>
            FindProject(project)?
                .GetChildren(KiCadTokens.Schematic.Path)
                .FirstOrDefault(p => string.Equals(p.GetValue(0), sheetPath, StringComparison.Ordinal));

        private SExpression CreatePath(string project, string sheetPath)
        {
            var instances = Node.GetChild(KiCadTokens.Schematic.Instances) ?? Node.CreateChild(KiCadTokens.Schematic.Instances);
            var projectNode = FindProject(project);
            if (projectNode is null)
            {
                projectNode = instances.CreateChild(KiCadTokens.Schematic.Project);
                projectNode.AddValue(project, SQuoteStyle.Quoted);
            }

            var path = projectNode.CreateChild(KiCadTokens.Schematic.Path);
            path.AddValue(sheetPath, SQuoteStyle.Quoted);
            return path;
        }
    }
}
