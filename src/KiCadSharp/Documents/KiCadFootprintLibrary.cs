using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SExpressions;

namespace KiCadSharp.Documents
{
    /// <summary>
    /// A file holding footprints — a board (<c>.kicad_pcb</c>) or a single footprint
    /// (<c>.kicad_mod</c>) — as a view over the file's s-expression tree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>.kicad_mod</c> is one footprint at the root of the file, not a library containing one:
    /// the root token is <c>footprint</c> in KiCad 6 and later and <c>module</c> in KiCad 5. Both
    /// are recognised here, and both present as a library of exactly one footprint.
    /// </para>
    /// <para>
    /// Saving writes the parsed document back, so everything the file holds — nets, zones, groups,
    /// setup, tracks, and every token this type does not model — is still there afterwards.
    /// </para>
    /// </remarks>
    public class KiCadFootprintLibrary
    {
        private readonly SDocument _document;
        private readonly SExpression _root;
        private readonly bool _rootIsFootprint;

        /// <summary>Creates a new, empty board-shaped library.</summary>
        /// <param name="generator">The value of the <c>generator</c> token.</param>
        /// <param name="version">The value of the <c>version</c> token.</param>
        public KiCadFootprintLibrary(string? generator = null, string? version = null)
        {
            generator ??= KiCadDefaults.LibraryGenerator;
            version ??= KiCadDefaults.FootprintLibraryVersion;

            _root = new SExpression(KiCadTokens.Board.Root);
            _root.CreateChild(KiCadTokens.Common.Version, version);
            _root.CreateChild(KiCadTokens.Common.Generator).AddValue(generator, SQuoteStyle.Quoted);
            _root.CreateChild(KiCadTokens.Board.General);
            _root.CreateChild(KiCadTokens.Common.Paper).AddValue(KiCadDefaults.Paper, SQuoteStyle.Quoted);
            _root.CreateChild(KiCadTokens.Common.Layers);
            _document = new SDocument();
            _document.Add(_root);
            _rootIsFootprint = false;
        }

        /// <summary>Creates a library over an existing s-expression.</summary>
        /// <param name="expression">A board form, or a single <c>footprint</c>/<c>module</c> form.</param>
        public KiCadFootprintLibrary(SExpression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            _root = expression;
            _rootIsFootprint = IsFootprintToken(expression.Token);
            _document = new SDocument();
            _document.Add(expression);
        }

        private KiCadFootprintLibrary(SDocument document)
        {
            _document = document;
            _root = document.Root ?? throw new InvalidOperationException("The file holds no s-expression.");
            _rootIsFootprint = IsFootprintToken(_root.Token);
        }

        /// <summary>Gets the whole parsed file.</summary>
        public SDocument Document => _document;

        /// <summary>Gets the root form: a board, or the footprint itself for a <c>.kicad_mod</c>.</summary>
        public SExpression Node => _root;

        /// <summary>True when the file is a single footprint rather than a board.</summary>
        public bool IsSingleFootprint => _rootIsFootprint;

        /// <summary>Gets or sets the file format version.</summary>
        public string Version
        {
            get => _root.GetChildValue(KiCadTokens.Common.Version) ?? KiCadDefaults.FootprintLibraryVersion;
            set => _root.SetChildValue(KiCadTokens.Common.Version, value, SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets the name of the program that wrote the file.</summary>
        public string Generator
        {
            get => _root.GetChildValue(KiCadTokens.Common.Generator) ?? KiCadDefaults.LibraryGenerator;
            set => _root.SetChildValue(KiCadTokens.Common.Generator, value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets the footprints in the file. For a <c>.kicad_mod</c> this is the root form itself,
        /// as one element; for a board it is a live view over its <c>footprint</c> children, with
        /// KiCad 5's <c>module</c> children included.
        /// </summary>
        public IReadOnlyList<KiCadFootprint> Footprints
        {
            get
            {
                if (_rootIsFootprint)
                {
                    return new[] { new KiCadFootprint(_root) };
                }

                return _root.Children
                    .Where(c => IsFootprintToken(c.Token))
                    .Select(c => new KiCadFootprint(c))
                    .ToArray();
            }
        }

        /// <summary>Loads a file.</summary>
        /// <param name="filePath">Path to a <c>.kicad_pcb</c> or <c>.kicad_mod</c>.</param>
        /// <returns>The library.</returns>
        public static KiCadFootprintLibrary Load(string filePath) => new(SDocument.Load(filePath));

        /// <summary>Loads a file asynchronously.</summary>
        /// <param name="filePath">Path to a <c>.kicad_pcb</c> or <c>.kicad_mod</c>.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The library.</returns>
        public static async Task<KiCadFootprintLibrary> LoadAsync(string filePath, CancellationToken cancellationToken = default) =>
            new(await SDocument.LoadAsync(filePath, cancellationToken).ConfigureAwait(false));

        /// <summary>Parses a file from text.</summary>
        /// <param name="text">The file contents.</param>
        /// <returns>The library.</returns>
        public static KiCadFootprintLibrary Parse(string text) => new(SDocument.Parse(text));

        /// <summary>Appends a footprint.</summary>
        /// <param name="footprint">The footprint.</param>
        /// <returns>The footprint, now a child of this file.</returns>
        /// <exception cref="InvalidOperationException">The file is a single footprint and cannot hold another.</exception>
        public KiCadFootprint AddFootprint(KiCadFootprint footprint)
        {
            ArgumentNullException.ThrowIfNull(footprint);
            if (_rootIsFootprint)
            {
                throw new InvalidOperationException("A .kicad_mod holds exactly one footprint, at the root of the file.");
            }

            _root.AddChild(footprint.Node);
            return footprint;
        }

        /// <summary>Removes the first footprint with the given name.</summary>
        /// <param name="id">The footprint's name.</param>
        /// <returns>True when a footprint was removed.</returns>
        public bool RemoveFootprint(string id)
        {
            if (_rootIsFootprint)
            {
                return false;
            }

            var footprint = GetFootprint(id);
            return footprint is not null && _root.Children.Remove(footprint.Node);
        }

        /// <summary>Gets the first footprint with the given name.</summary>
        /// <param name="id">The footprint's name.</param>
        /// <returns>The footprint, or <see langword="null"/>.</returns>
        public KiCadFootprint? GetFootprint(string id) =>
            Footprints.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));

        /// <summary>
        /// Writes the file back out. An untouched file comes out byte for byte; a changed one differs
        /// only where it was changed.
        /// </summary>
        /// <param name="filePath">Destination path.</param>
        public void Save(string filePath) => _document.Save(filePath);

        /// <summary>Writes the file back out asynchronously.</summary>
        /// <param name="filePath">Destination path.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the file is written.</returns>
        public Task SaveAsync(string filePath, CancellationToken cancellationToken = default) =>
            _document.SaveAsync(filePath, cancellationToken);

        /// <summary>Renders the file to text.</summary>
        /// <returns>The file contents.</returns>
        public string ToText() => _document.ToText();

        /// <summary>Writes one footprint out as a <c>.kicad_mod</c>.</summary>
        /// <param name="footprint">The footprint to write.</param>
        /// <param name="filePath">Destination path.</param>
        /// <remarks>The footprint's own bytes are reproduced; it is not re-formatted.</remarks>
        public static void SaveFootprint(KiCadFootprint footprint, string filePath)
        {
            ArgumentNullException.ThrowIfNull(footprint);
            new SExpressionWriter().WriteToFile(footprint.Node, filePath);
        }

        private static bool IsFootprintToken(string token) =>
            string.Equals(token, KiCadTokens.Footprint.Root, StringComparison.Ordinal)
            || string.Equals(token, KiCadTokens.Footprint.LegacyRoot, StringComparison.Ordinal);
    }

    /// <summary>
    /// One footprint, as a view over its <c>(footprint "Name" ...)</c> form.
    /// </summary>
    /// <remarks>
    /// Every property here reads and writes the form in place. That is what keeps <c>descr</c>,
    /// <c>tags</c>, <c>property</c>, <c>fp_rect</c>, <c>fp_text_box</c>, <c>fp_curve</c>, zones,
    /// groups, and whatever KiCad adds next: they are never copied out, so they cannot be left
    /// behind.
    /// </remarks>
    public class KiCadFootprint : KiCadNode
    {
        /// <summary>Creates a view over a <c>(footprint ...)</c> or <c>(module ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadFootprint(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates a new footprint with the reference and value text KiCad expects.</summary>
        /// <param name="id">The footprint's name.</param>
        public KiCadFootprint(string id)
            : base(new SExpression(KiCadTokens.Footprint.Root))
        {
            ArgumentNullException.ThrowIfNull(id);
            Node.AddValue(id, SQuoteStyle.Quoted);
            Node.SetChildValue(KiCadTokens.Common.Layer, KiCadLayerNames.FCu, SQuoteStyle.Quoted);
            AddFpText(KiCadTokens.Footprint.TextTypeReference, "REF**", 0, 0, KiCadLayerNames.FSilkS);
            AddFpText(KiCadTokens.Footprint.TextTypeValue, id, 0, 1.27, KiCadLayerNames.FFab);
        }

        /// <summary>Gets or sets the footprint's name, the first value of the form.</summary>
        public string Id
        {
            get => Node.GetValue(0) ?? "Unknown";
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the layer the footprint sits on.</summary>
        public string Layer
        {
            get => ReadChild(KiCadTokens.Common.Layer) ?? KiCadLayerNames.FCu;
            set => WriteChild(KiCadTokens.Common.Layer, value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets or sets where the footprint is placed and how far it is turned, the
        /// <c>(at x y [rotation])</c> form.
        /// </summary>
        /// <remarks>
        /// A <c>.kicad_mod</c> on its own is drawn about the origin and carries no <c>at</c>, so this
        /// reads <c>(0, 0)</c> there. A footprint reached through <see cref="KiCadBoard.Footprints"/>
        /// is a <em>placed</em> one and this is where it sits — the first thing anyone asks a board.
        /// Every child of the footprint is drawn relative to it.
        /// </remarks>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.At));
            set => value.Write(Require(KiCadTokens.Common.At), includeRotation: false);
        }

        /// <summary>
        /// Gets or sets the footprint's UUID — how a <see cref="KiCadGroup"/> and the schematic's
        /// <c>path</c> refer to it. <see langword="null"/> on a file old enough to have used
        /// <see cref="Tstamp"/> instead.
        /// </summary>
        public string? Uuid
        {
            get => ReadChild(KiCadTokens.Common.Uuid);
            set => WriteChild(KiCadTokens.Common.Uuid, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets whether the footprint is locked against being moved.</summary>
        public bool Locked
        {
            get => ReadFlag(KiCadTokens.Common.Locked);
            set => WriteFlag(KiCadTokens.Common.Locked, value);
        }

        /// <summary>Gets or sets the footprint's description, the <c>(descr "...")</c> token.</summary>
        public string? Description
        {
            get => ReadChild(KiCadTokens.Footprint.Descr);
            set => WriteChild(KiCadTokens.Footprint.Descr, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the footprint's search keywords, the <c>(tags "...")</c> token.</summary>
        public string? Tags
        {
            get => ReadChild(KiCadTokens.Footprint.Tags);
            set => WriteChild(KiCadTokens.Footprint.Tags, value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets or sets the KiCad 5 edit timestamp, or 0 when the file has none. KiCad 7 replaced it
        /// with a UUID and no longer writes it.
        /// </summary>
        public long Tedit
        {
            get => ReadHex(KiCadTokens.Footprint.Tedit);
            set => Node.SetChildValue(KiCadTokens.Footprint.Tedit, value.ToString("X", CultureInfo.InvariantCulture), SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets the KiCad 5 placement timestamp, or 0 when the file has none.</summary>
        public long Tstamp
        {
            get => ReadHex(KiCadTokens.Footprint.Tstamp);
            set => Node.SetChildValue(KiCadTokens.Footprint.Tstamp, value.ToString("X", CultureInfo.InvariantCulture), SQuoteStyle.Bare);
        }

        /// <summary>Gets the values of the <c>(attr ...)</c> token, e.g. <c>smd</c>, <c>through_hole</c>.</summary>
        public IReadOnlyList<string> Attributes
        {
            get
            {
                var attr = Node.GetChild(KiCadTokens.Footprint.Attr);
                return attr is null ? Array.Empty<string>() : attr.Values.ToArray();
            }
        }

        /// <summary>Gets the footprint's fields, as a live view over its <c>property</c> children.</summary>
        public KiCadNodeList<KiCadProperty> Properties => new(Node, KiCadTokens.Common.Property, n => new KiCadProperty(n));

        /// <summary>Gets the 3D models attached to the footprint.</summary>
        public KiCadNodeList<KiCadModel> Models => new(Node, KiCadTokens.Footprint.Model, n => new KiCadModel(n));

        /// <summary>Gets the footprint's text items.</summary>
        public KiCadNodeList<KiCadFpText> TextItems => new(Node, KiCadTokens.Footprint.FpText, n => new KiCadFpText(n));

        /// <summary>Gets the footprint's pads.</summary>
        public KiCadNodeList<KiCadPad> Pads => new(Node, KiCadTokens.Footprint.Pad, n => new KiCadPad(n));

        /// <summary>Gets the footprint's lines.</summary>
        public KiCadNodeList<KiCadFpLine> Lines => new(Node, KiCadTokens.Footprint.FpLine, n => new KiCadFpLine(n));

        /// <summary>Gets the footprint's rectangles.</summary>
        public KiCadNodeList<KiCadFpRect> Rectangles => new(Node, KiCadTokens.Footprint.FpRect, n => new KiCadFpRect(n));

        /// <summary>Gets the footprint's circles.</summary>
        public KiCadNodeList<KiCadFpCircle> Circles => new(Node, KiCadTokens.Footprint.FpCircle, n => new KiCadFpCircle(n));

        /// <summary>Gets the footprint's arcs.</summary>
        public KiCadNodeList<KiCadFpArc> Arcs => new(Node, KiCadTokens.Footprint.FpArc, n => new KiCadFpArc(n));

        /// <summary>Gets the footprint's polygons.</summary>
        public KiCadNodeList<KiCadFpPoly> Polygons => new(Node, KiCadTokens.Footprint.FpPoly, n => new KiCadFpPoly(n));

        /// <summary>Gets the value of a named field.</summary>
        /// <param name="key">The field key, e.g. <c>Reference</c>.</param>
        /// <returns>The value, or <see langword="null"/>.</returns>
        public string? GetPropertyValue(string key) =>
            Properties.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal))?.Value;

        /// <summary>Appends a text item.</summary>
        /// <param name="type">The kind of text: <c>reference</c>, <c>value</c> or <c>user</c>.</param>
        /// <param name="text">The text.</param>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        /// <param name="layer">The layer to draw it on.</param>
        /// <returns>The text item.</returns>
        public KiCadFpText AddFpText(string type, string text, double x, double y, string layer)
        {
            var item = new KiCadFpText(type, text, new KiCadPosition(x, y), layer);
            Node.AddChild(item.Node);
            return item;
        }

        /// <summary>Appends a pad.</summary>
        /// <param name="number">The pad number.</param>
        /// <param name="type">The pad type, e.g. <c>smd</c>, <c>thru_hole</c>.</param>
        /// <param name="shape">The pad shape, e.g. <c>rect</c>, <c>roundrect</c>.</param>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        /// <param name="width">Pad width, millimetres.</param>
        /// <param name="height">Pad height, millimetres.</param>
        /// <param name="layers">The layers the pad is on.</param>
        /// <returns>The pad.</returns>
        public KiCadPad AddPad(string number, string type, string shape, double x, double y, double width, double height, IEnumerable<string> layers)
        {
            var pad = new KiCadPad(number, type, shape, new KiCadPosition(x, y), new KiCadSize(width, height), layers);
            Node.AddChild(pad.Node);
            return pad;
        }

        /// <summary>Appends a line.</summary>
        /// <param name="startX">Start X.</param>
        /// <param name="startY">Start Y.</param>
        /// <param name="endX">End X.</param>
        /// <param name="endY">End Y.</param>
        /// <param name="layer">The layer.</param>
        /// <param name="width">Stroke width, millimetres.</param>
        /// <returns>The line.</returns>
        public KiCadFpLine AddLine(double startX, double startY, double endX, double endY, string layer, double width = 0.12)
        {
            var line = new KiCadFpLine(new SExpression(KiCadTokens.Footprint.FpLine))
            {
                Start = new KiCadPosition(startX, startY),
                End = new KiCadPosition(endX, endY),
                Layer = layer,
                Width = width,
            };

            Node.AddChild(line.Node);
            return line;
        }

        /// <summary>Appends a circle.</summary>
        /// <param name="centerX">Centre X.</param>
        /// <param name="centerY">Centre Y.</param>
        /// <param name="endX">A point on the circumference, X.</param>
        /// <param name="endY">A point on the circumference, Y.</param>
        /// <param name="layer">The layer.</param>
        /// <param name="width">Stroke width, millimetres.</param>
        /// <returns>The circle.</returns>
        public KiCadFpCircle AddCircle(double centerX, double centerY, double endX, double endY, string layer, double width = 0.12)
        {
            var circle = new KiCadFpCircle(new SExpression(KiCadTokens.Footprint.FpCircle))
            {
                Center = new KiCadPosition(centerX, centerY),
                End = new KiCadPosition(endX, endY),
                Layer = layer,
                Width = width,
            };

            Node.AddChild(circle.Node);
            return circle;
        }

        /// <summary>Appends a 3D model reference.</summary>
        /// <param name="path">The model path, usually with a <c>${KICAD…_3DMODEL_DIR}</c> prefix.</param>
        /// <returns>The model.</returns>
        public KiCadModel AddModel(string path)
        {
            var model = new KiCadModel(path);
            Node.AddChild(model.Node);
            return model;
        }

        /// <summary>Deep-copies the footprint under a new name.</summary>
        /// <param name="newId">The copy's name.</param>
        /// <returns>The copy, with no parent.</returns>
        public KiCadFootprint CloneAs(string newId)
        {
            ArgumentNullException.ThrowIfNull(newId);
            return new KiCadFootprint(Node.Clone()) { Id = newId };
        }

        private long ReadHex(string token)
        {
            var text = ReadChild(token);
            if (text is null)
            {
                return 0;
            }

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text[2..];
            }

            return long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }
    }

    /// <summary>
    /// A footprint drawing element that sits on a layer and has a stroke.
    /// </summary>
    public abstract class KiCadFpItem : KiCadNode
    {
        /// <summary>Creates a view over the form.</summary>
        /// <param name="node">The form.</param>
        protected KiCadFpItem(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the layer the element is drawn on.</summary>
        public string Layer
        {
            get => ReadChild(KiCadTokens.Common.Layer) ?? KiCadLayerNames.FSilkS;
            set => WriteChild(KiCadTokens.Common.Layer, value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets or sets the stroke width. KiCad 7+ writes <c>(stroke (width w) ...)</c>; KiCad 5 and
        /// 6 wrote a bare <c>(width w)</c>, and both are read here.
        /// </summary>
        public double Width
        {
            get
            {
                var stroke = Node.GetChild(KiCadTokens.Common.Stroke);
                if (stroke is not null && stroke.GetChild(KiCadTokens.Common.Width) is { } strokeWidth && strokeWidth.TryGetValue<double>(0, out var fromStroke))
                {
                    return fromStroke;
                }

                return ReadChildDouble(KiCadTokens.Common.Width, 0, 0.12);
            }

            set
            {
                if (Node.GetChild(KiCadTokens.Common.Stroke) is { } stroke)
                {
                    stroke.SetChildValue(KiCadTokens.Common.Width, Numbers.Format(value), SQuoteStyle.Bare);
                    return;
                }

                WriteChildDouble(KiCadTokens.Common.Width, value);
            }
        }

        /// <summary>Gets the stroke, creating a <c>(stroke ...)</c> child if there is none.</summary>
        public KiCadStroke? Stroke => Node.GetChild(KiCadTokens.Common.Stroke) is { } node ? new KiCadStroke(node) : null;

        /// <summary>Gets the <c>(stroke ...)</c> form, adding an empty one when the node has none.</summary>
        /// <returns>The view.</returns>
        public KiCadStroke RequireStroke() => new(Require(KiCadTokens.Common.Stroke));
    }

    /// <summary>A line: <c>(fp_line (start x y) (end x y) (stroke ...) (layer "..."))</c>.</summary>
    public class KiCadFpLine : KiCadFpItem
    {
        /// <summary>Creates a view over an <c>(fp_line ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadFpLine(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty line.</summary>
        public KiCadFpLine()
            : base(new SExpression(KiCadTokens.Footprint.FpLine))
        {
        }

        /// <summary>Gets or sets the start point.</summary>
        public KiCadPosition Start
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.Start));
            set => value.Write(Require(KiCadTokens.Common.Start), includeRotation: false);
        }

        /// <summary>Gets or sets the end point.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.End));
            set => value.Write(Require(KiCadTokens.Common.End), includeRotation: false);
        }
    }

    /// <summary>A rectangle: <c>(fp_rect (start x y) (end x y) (stroke ...) (fill ...) (layer "..."))</c>.</summary>
    public class KiCadFpRect : KiCadFpItem
    {
        /// <summary>Creates a view over an <c>(fp_rect ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadFpRect(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty rectangle.</summary>
        public KiCadFpRect()
            : base(new SExpression(KiCadTokens.Footprint.FpRect))
        {
        }

        /// <summary>Gets or sets the first corner.</summary>
        public KiCadPosition Start
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.Start));
            set => value.Write(Require(KiCadTokens.Common.Start), includeRotation: false);
        }

        /// <summary>Gets or sets the opposite corner.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.End));
            set => value.Write(Require(KiCadTokens.Common.End), includeRotation: false);
        }

        /// <summary>Gets the fill, creating a <c>(fill ...)</c> child if there is none.</summary>
        public KiCadFill? Fill => Node.GetChild(KiCadTokens.Common.Fill) is { } node ? new KiCadFill(node) : null;

        /// <summary>Gets the <c>(fill ...)</c> form, adding an empty one when the node has none.</summary>
        /// <returns>The view.</returns>
        public KiCadFill RequireFill() => new(Require(KiCadTokens.Common.Fill));
    }

    /// <summary>A circle: <c>(fp_circle (center x y) (end x y) (stroke ...) (layer "..."))</c>.</summary>
    public class KiCadFpCircle : KiCadFpItem
    {
        /// <summary>Creates a view over an <c>(fp_circle ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadFpCircle(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty circle.</summary>
        public KiCadFpCircle()
            : base(new SExpression(KiCadTokens.Footprint.FpCircle))
        {
        }

        /// <summary>Gets or sets the centre.</summary>
        public KiCadPosition Center
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.Center));
            set => value.Write(Require(KiCadTokens.Common.Center), includeRotation: false);
        }

        /// <summary>Gets or sets a point on the circumference.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.End));
            set => value.Write(Require(KiCadTokens.Common.End), includeRotation: false);
        }
    }

    /// <summary>An arc: <c>(fp_arc (start x y) (mid x y) (end x y) (stroke ...) (layer "..."))</c>.</summary>
    public class KiCadFpArc : KiCadFpItem
    {
        /// <summary>Creates a view over an <c>(fp_arc ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadFpArc(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty arc.</summary>
        public KiCadFpArc()
            : base(new SExpression(KiCadTokens.Footprint.FpArc))
        {
        }

        /// <summary>Gets or sets the start point.</summary>
        public KiCadPosition Start
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.Start));
            set => value.Write(Require(KiCadTokens.Common.Start), includeRotation: false);
        }

        /// <summary>Gets or sets the mid point the arc passes through, KiCad 6 and later.</summary>
        public KiCadPosition Mid
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.Mid));
            set => value.Write(Require(KiCadTokens.Common.Mid), includeRotation: false);
        }

        /// <summary>Gets or sets the end point.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.End));
            set => value.Write(Require(KiCadTokens.Common.End), includeRotation: false);
        }

        /// <summary>Gets or sets the KiCad 5 sweep angle, or 0 when the file uses a mid point instead.</summary>
        public double Angle
        {
            get => ReadChildDouble(KiCadTokens.Common.Angle);
            set => WriteChildDouble(KiCadTokens.Common.Angle, value);
        }
    }

    /// <summary>A polygon: <c>(fp_poly (pts (xy x y) ...) (stroke ...) (layer "..."))</c>.</summary>
    public class KiCadFpPoly : KiCadFpItem
    {
        /// <summary>Creates a view over an <c>(fp_poly ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadFpPoly(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty polygon.</summary>
        public KiCadFpPoly()
            : base(new SExpression(KiCadTokens.Footprint.FpPoly))
        {
        }

        /// <summary>Gets the vertices, in order.</summary>
        public IReadOnlyList<KiCadPosition> Points =>
            (Node.GetChild(KiCadTokens.Common.Pts)?.GetChildren(KiCadTokens.Common.Xy) ?? Enumerable.Empty<SExpression>())
                .Select(xy => new KiCadPosition(xy.GetValueAsDouble(0), xy.GetValueAsDouble(1)))
                .ToArray();

        /// <summary>Appends a vertex.</summary>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        public void AddPoint(double x, double y)
        {
            var points = Node.GetChild(KiCadTokens.Common.Pts) ?? Node.CreateChild(KiCadTokens.Common.Pts);
            points.CreateChild(KiCadTokens.Common.Xy, Numbers.Format(x), Numbers.Format(y));
        }
    }

    /// <summary>Footprint text: <c>(fp_text reference "REF**" (at ...) (layer "...") (effects ...))</c>.</summary>
    public class KiCadFpText : KiCadNode
    {
        /// <summary>Creates a view over an <c>(fp_text ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadFpText(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty text item.</summary>
        public KiCadFpText()
            : base(new SExpression(KiCadTokens.Footprint.FpText))
        {
        }

        /// <summary>Creates a text item.</summary>
        /// <param name="type">The kind of text: <c>reference</c>, <c>value</c> or <c>user</c>.</param>
        /// <param name="text">The text.</param>
        /// <param name="position">Where the text sits.</param>
        /// <param name="layer">The layer to draw it on.</param>
        public KiCadFpText(string type, string text, KiCadPosition position, string layer)
            : base(new SExpression(KiCadTokens.Footprint.FpText))
        {
            Node.AddValue(type, SQuoteStyle.Bare);
            Node.AddValue(text, SQuoteStyle.Quoted);
            Position = position;
            Layer = layer;
        }

        /// <summary>Gets or sets the kind of text.</summary>
        public string Type
        {
            get => Node.GetValue(0) ?? KiCadTokens.Footprint.TextTypeUser;
            set => WriteValue(0, value, SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets the text.</summary>
        public string Text
        {
            get => Node.GetValue(1) ?? string.Empty;
            set => WriteValue(1, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets where the text sits.</summary>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.At));
            set => value.Write(Require(KiCadTokens.Common.At), includeRotation: true);
        }

        /// <summary>Gets or sets the layer.</summary>
        public string Layer
        {
            get => ReadChild(KiCadTokens.Common.Layer) ?? KiCadLayerNames.FSilkS;
            set => WriteChild(KiCadTokens.Common.Layer, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets the text rendering, creating an <c>(effects ...)</c> if there is none.</summary>
        public KiCadFontEffects? FontEffects => Node.GetChild(KiCadTokens.Common.Effects) is { } node ? new KiCadFontEffects(node) : null;

        /// <summary>Gets the <c>(effects ...)</c> form, adding an empty one when the node has none.</summary>
        /// <returns>The view.</returns>
        public KiCadFontEffects RequireFontEffects() => new(Require(KiCadTokens.Common.Effects));

        /// <summary>Gets or sets the glyph size.</summary>
        public KiCadSize Size
        {
            get => KiCadSize.Read(Node.GetChild(KiCadTokens.Common.Effects)?.GetChild(KiCadTokens.Common.Font)?.GetChild(KiCadTokens.Common.Size), 1);
            set => RequireFontEffects().Size = value;
        }

        /// <summary>Gets or sets the pen thickness.</summary>
        public double Thickness
        {
            get => FontEffects?.Thickness ?? 0.15;
            set => RequireFontEffects().Thickness = value;
        }

        /// <summary>Gets or sets whether the text is italic.</summary>
        public bool Italic
        {
            get => FontEffects?.Italic ?? false;
            set => RequireFontEffects().Italic = value;
        }

        /// <summary>
        /// Gets or sets whether the text is hidden. KiCad 7+ writes <c>(hide yes)</c> inside
        /// <c>(effects ...)</c>; KiCad 5 wrote a bare <c>hide</c> value on the form itself.
        /// </summary>
        public bool Hide
        {
            get => (FontEffects?.Hide ?? false)
                || Node.Values.Any(v => string.Equals(v, KiCadTokens.Common.Hide, StringComparison.Ordinal));
            set => RequireFontEffects().Hide = value;
        }
    }

    /// <summary>A pad: <c>(pad "1" smd roundrect (at x y) (size w h) (layers ...) ...)</c>.</summary>
    public class KiCadPad : KiCadNode
    {
        /// <summary>Creates a view over a <c>(pad ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadPad(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty pad.</summary>
        public KiCadPad()
            : base(new SExpression(KiCadTokens.Footprint.Pad))
        {
        }

        /// <summary>Creates a pad.</summary>
        /// <param name="number">The pad number.</param>
        /// <param name="type">The pad type, e.g. <c>smd</c>.</param>
        /// <param name="shape">The pad shape, e.g. <c>roundrect</c>.</param>
        /// <param name="position">Where the pad sits.</param>
        /// <param name="size">The pad size.</param>
        /// <param name="layers">The layers the pad is on.</param>
        public KiCadPad(string number, string type, string shape, KiCadPosition position, KiCadSize size, IEnumerable<string> layers)
            : base(new SExpression(KiCadTokens.Footprint.Pad))
        {
            ArgumentNullException.ThrowIfNull(layers);
            Node.AddValue(number, SQuoteStyle.Quoted);
            Node.AddValue(type, SQuoteStyle.Bare);
            Node.AddValue(shape, SQuoteStyle.Bare);
            Position = position;
            Size = size;
            Layers = layers.ToArray();
        }

        /// <summary>Gets or sets the pad number.</summary>
        public string Number
        {
            get => Node.GetValue(0) ?? "1";
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the pad type.</summary>
        public string Type
        {
            get => Node.GetValue(1) ?? "smd";
            set => WriteValue(1, value, SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets the pad shape.</summary>
        public string Shape
        {
            get => Node.GetValue(2) ?? "rect";
            set => WriteValue(2, value, SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets where the pad sits.</summary>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.At));
            set => value.Write(Require(KiCadTokens.Common.At), includeRotation: false);
        }

        /// <summary>Gets or sets the pad size.</summary>
        public KiCadSize Size
        {
            get => KiCadSize.Read(Node.GetChild(KiCadTokens.Common.Size), 1);
            set => value.Write(Require(KiCadTokens.Common.Size));
        }

        /// <summary>Gets or sets the layers the pad is on.</summary>
        public IReadOnlyList<string> Layers
        {
            get
            {
                var layers = Node.GetChild(KiCadTokens.Common.Layers);
                return layers is null ? Array.Empty<string>() : layers.Values.ToArray();
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);
                var layers = Require(KiCadTokens.Common.Layers);
                layers.Values.Clear();
                foreach (var layer in value)
                {
                    layers.Values.Add(layer, SQuoteStyle.Quoted);
                }
            }
        }

        /// <summary>Gets the drill, or <see langword="null"/> for a surface-mount pad.</summary>
        public KiCadDrill? Drill => Node.GetChild(KiCadTokens.Common.Drill) is { } drill ? new KiCadDrill(drill) : null;

        /// <summary>
        /// Gets or sets the net this pad is connected to, or <see langword="null"/> when it has none.
        /// </summary>
        /// <remarks>
        /// Both spellings are read: KiCad 9 and earlier wrote <c>(net 1 "GND")</c>, KiCad 10 writes
        /// <c>(net "GND")</c> and keeps no codes anywhere. A set goes back into whichever slot the
        /// pad already keeps the name in.
        /// </remarks>
        public string? Net
        {
            get => KiCadNetRef.ReadName(Node.GetChild(KiCadTokens.Common.Net));
            set
            {
                if (value is null)
                {
                    Node.RemoveChild(KiCadTokens.Common.Net);
                    return;
                }

                KiCadNetRef.WriteName(Require(KiCadTokens.Common.Net), value);
            }
        }
    }

    /// <summary>A drill: <c>(drill 0.8)</c> or <c>(drill oval w h)</c>, with an optional offset.</summary>
    public class KiCadDrill : KiCadNode
    {
        /// <summary>Creates a view over a <c>(drill ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadDrill(SExpression node)
            : base(node)
        {
        }

        /// <summary>True when the drill is an oval slot rather than a round hole.</summary>
        public bool IsOval => string.Equals(Node.GetValue(0), "oval", StringComparison.Ordinal);

        /// <summary>True when the drill is a plain round hole.</summary>
        public bool IsRound => !IsOval;

        /// <summary>Gets the diameter of a round hole, or 0 for an oval one.</summary>
        public double Size => IsOval ? 0 : Node.GetValueAsDouble(0);

        /// <summary>Gets the width: the diameter for a round hole, the long axis for an oval one.</summary>
        public double Width => IsOval ? Node.GetValueAsDouble(1) : Node.GetValueAsDouble(0);

        /// <summary>Gets the height: the diameter for a round hole, the short axis for an oval one.</summary>
        public double Height => IsOval ? Node.GetValueAsDouble(2) : Node.GetValueAsDouble(0);

        /// <summary>Gets the drill offset from the pad centre, or <see langword="null"/> when there is none.</summary>
        public KiCadPosition? Offset =>
            Node.GetChild(KiCadTokens.Footprint.Offset) is { } offset ? KiCadPosition.Read(offset) : null;
    }

    /// <summary>A 3D model reference: <c>(model "path" (offset (xyz ...)) (scale (xyz ...)) (rotate (xyz ...)))</c>.</summary>
    public class KiCadModel : KiCadNode
    {
        /// <summary>Creates a view over a <c>(model ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadModel(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty model reference.</summary>
        public KiCadModel()
            : base(new SExpression(KiCadTokens.Footprint.Model))
        {
        }

        /// <summary>Creates a model reference.</summary>
        /// <param name="path">The model path.</param>
        public KiCadModel(string path)
            : base(new SExpression(KiCadTokens.Footprint.Model))
        {
            ArgumentNullException.ThrowIfNull(path);
            Node.AddValue(path, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the model path.</summary>
        public string Path
        {
            get => Node.GetValue(0) ?? string.Empty;
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the model offset.</summary>
        public KiCadXyz Offset
        {
            get => KiCadXyz.Read(Node.GetChild(KiCadTokens.Footprint.Offset), 0);
            set => value.Write(Require(KiCadTokens.Footprint.Offset));
        }

        /// <summary>Gets or sets the model scale.</summary>
        public KiCadXyz Scale
        {
            get => KiCadXyz.Read(Node.GetChild(KiCadTokens.Common.Scale), 1);
            set => value.Write(Require(KiCadTokens.Common.Scale));
        }

        /// <summary>Gets or sets the model rotation, in degrees.</summary>
        public KiCadXyz Rotation
        {
            get => KiCadXyz.Read(Node.GetChild(KiCadTokens.Footprint.Rotate), 0);
            set => value.Write(Require(KiCadTokens.Footprint.Rotate));
        }
    }
}
