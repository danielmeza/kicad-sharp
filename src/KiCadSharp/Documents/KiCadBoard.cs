using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SExpressions;

namespace KiCadSharp.Documents
{
    /// <summary>
    /// A board file (<c>.kicad_pcb</c>) as a view over the file's s-expression tree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="KiCadFootprintLibrary"/> also opens a <c>.kicad_pcb</c>, but only to reach the
    /// footprints in it. This is the board itself: the stackup, the layer table, the net list, the
    /// copper, the zones and the drawings. Both may be open on the same file at the same time —
    /// neither owns it, because neither holds any state.
    /// </para>
    /// <para>
    /// Nothing here is copied out of the file. Every property reads the node it names and every
    /// setter writes back into that same node, so saving an untouched board reproduces the file byte
    /// for byte and saving a changed one differs only where it was changed. Reading is likewise
    /// non-destructive: a getter never adds a form the file does not have, which is why the optional
    /// parts of a board — <see cref="General"/>, <see cref="TitleBlock"/>, <see cref="Setup"/> —
    /// read as <see langword="null"/> and have a matching <c>Require…</c> method for when you mean
    /// to create them.
    /// </para>
    /// <para>
    /// Not every token is named by a property, and that is fine: <c>(embedded_files …)</c>,
    /// <c>(pcbplotparams …)</c>, the design rules and whatever KiCad adds next are all still there
    /// after a save, and reachable through <see cref="Node"/>.
    /// </para>
    /// </remarks>
    public sealed class KiCadBoard
    {
        private readonly SDocument _document;
        private readonly SExpression _root;

        /// <summary>
        /// The layer table of a two-layer board, the rows and the ordinals KiCad itself writes.
        /// </summary>
        /// <remarks>
        /// A board with an empty <c>(layers)</c> form is not a board: MEASURED against KiCad 10.0.6,
        /// <c>kicad-cli pcb drc</c> refuses it with <c>Failed to load board: 0 is not a valid layer
        /// count</c> and <c>pcbnew.LoadBoard</c> returns nothing at all. So a new board starts with a
        /// table rather than with a placeholder, and a caller who wants four layers edits it.
        /// </remarks>
        private static readonly (int Ordinal, string Name, string Type, string? UserName)[] DefaultLayers =
        [
            (0, KiCadLayerNames.FCu, KiCadLayerNames.TypeSignal, null),
            (2, KiCadLayerNames.BCu, KiCadLayerNames.TypeSignal, null),
            (5, KiCadLayerNames.FSilkS, KiCadLayerNames.TypeUser, KiCadLayerNames.FSilkscreen),
            (7, KiCadLayerNames.BSilkS, KiCadLayerNames.TypeUser, KiCadLayerNames.BSilkscreen),
            (1, KiCadLayerNames.FMask, KiCadLayerNames.TypeUser, null),
            (3, KiCadLayerNames.BMask, KiCadLayerNames.TypeUser, null),
            (13, KiCadLayerNames.FPaste, KiCadLayerNames.TypeUser, null),
            (15, KiCadLayerNames.BPaste, KiCadLayerNames.TypeUser, null),
            (25, KiCadLayerNames.EdgeCuts, KiCadLayerNames.TypeUser, null),
            (27, KiCadLayerNames.Margin, KiCadLayerNames.TypeUser, null),
            (31, KiCadLayerNames.FCrtYd, KiCadLayerNames.TypeUser, KiCadLayerNames.FCourtyard),
            (29, KiCadLayerNames.BCrtYd, KiCadLayerNames.TypeUser, KiCadLayerNames.BCourtyard),
            (35, KiCadLayerNames.FFab, KiCadLayerNames.TypeUser, null),
            (33, KiCadLayerNames.BFab, KiCadLayerNames.TypeUser, null),
            (19, KiCadLayerNames.CmtsUser, KiCadLayerNames.TypeUser, KiCadLayerNames.UserComments),
            (17, KiCadLayerNames.DwgsUser, KiCadLayerNames.TypeUser, KiCadLayerNames.UserDrawings),
        ];

        /// <summary>Creates a new, empty board with the forms KiCad expects at the top of the file.</summary>
        /// <param name="generator">The value of the <c>generator</c> token.</param>
        /// <param name="version">
        /// The value of the <c>version</c> token. The default, <c>20241229</c>, is the KiCad 9 board
        /// format — the one this type writes, because it writes a <c>(net n "NAME")</c> table and
        /// numbers the nets on copper. KiCad 10 stamps <c>20260206</c> and names them instead
        /// (MEASURED: pcbnew 10.0.6 rewrites a 20241229 file as 20260206 with no net table at all),
        /// so passing that here would claim a spelling this type does not produce. KiCad reads both.
        /// </param>
        public KiCadBoard(string generator = "KiCadSharp", string version = "20241229")
        {
            _root = new SExpression(KiCadTokens.Board.Root);
            _root.CreateChild(KiCadTokens.Common.Version, version);
            _root.CreateChild(KiCadTokens.Common.Generator).AddValue(generator, SQuoteStyle.Quoted);
            _root.CreateChild(KiCadTokens.Board.General).CreateChild(KiCadTokens.Common.Thickness, "1.6");
            _root.CreateChild(KiCadTokens.Common.Paper).AddValue("A4", SQuoteStyle.Quoted);

            var layers = _root.CreateChild(KiCadTokens.Common.Layers);
            foreach (var (ordinal, name, type, userName) in DefaultLayers)
            {
                layers.AddChild(new KiCadBoardLayer(ordinal, name, type, userName).Node);
            }

            _document = new SDocument();
            _document.Add(_root);
        }

        /// <summary>Creates a board over an existing <c>(kicad_pcb …)</c> form.</summary>
        /// <param name="expression">The form.</param>
        /// <exception cref="ArgumentException">The form is not a board.</exception>
        public KiCadBoard(SExpression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            RequireBoardToken(expression, nameof(expression));
            _root = expression;
            _document = new SDocument();
            _document.Add(expression);
        }

        private KiCadBoard(SDocument document)
        {
            _document = document;
            _root = document.Root ?? throw new InvalidOperationException("The file holds no s-expression.");
            RequireBoardToken(_root, null);
        }

        /// <summary>Gets the whole parsed file, comments and all.</summary>
        public SDocument Document => _document;

        /// <summary>Gets the <c>(kicad_pcb …)</c> form every property here reads and writes through.</summary>
        public SExpression Node => _root;

        /// <summary>Gets or sets the file format version, KiCad's <c>(version …)</c> date stamp.</summary>
        public string Version
        {
            get => _root.GetChildValue(KiCadTokens.Common.Version) ?? "20241229";
            set => _root.SetChildValue(KiCadTokens.Common.Version, value, SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets the name of the program that wrote the file.</summary>
        public string Generator
        {
            get => _root.GetChildValue(KiCadTokens.Common.Generator) ?? "KiCadSharp";
            set => _root.SetChildValue(KiCadTokens.Common.Generator, value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets or sets the version of the program that wrote the file, KiCad 8's
        /// <c>(generator_version "10.0")</c>. <see langword="null"/> on a file written before it
        /// existed; setting it adds the token.
        /// </summary>
        public string? GeneratorVersion
        {
            get => _root.GetChildValue(KiCadTokens.Common.GeneratorVersion);
            set
            {
                if (value is null)
                {
                    _root.RemoveChild(KiCadTokens.Common.GeneratorVersion);
                    return;
                }

                _root.SetChildValue(KiCadTokens.Common.GeneratorVersion, value, SQuoteStyle.Quoted);
            }
        }

        /// <summary>Gets or sets the sheet size, the <c>(paper "A4")</c> token.</summary>
        public string Paper
        {
            get => _root.GetChildValue(KiCadTokens.Common.Paper) ?? "A4";
            set => _root.SetChildValue(KiCadTokens.Common.Paper, value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets the board-wide numbers — <c>(general (thickness 1.6) (legacy_teardrops no))</c> — or
        /// <see langword="null"/> when the file has no <c>general</c> form.
        /// </summary>
        public KiCadBoardGeneral? General =>
            _root.GetChild(KiCadTokens.Board.General) is { } general ? new KiCadBoardGeneral(general) : null;

        /// <summary>
        /// Gets the sheet's title block, or <see langword="null"/>. A board KiCad has never had a
        /// title typed into has no <c>title_block</c> form at all, so this is genuinely absent
        /// rather than empty.
        /// </summary>
        public KiCadTitleBlock? TitleBlock =>
            _root.GetChild(KiCadTokens.Common.TitleBlock) is { } block ? new KiCadTitleBlock(block) : null;

        /// <summary>
        /// Gets the manufacturing settings — the stackup, the plot parameters, the clearances — or
        /// <see langword="null"/> when the file has no <c>setup</c> form.
        /// </summary>
        public KiCadBoardSetup? Setup =>
            _root.GetChild(KiCadTokens.Board.Setup) is { } setup ? new KiCadBoardSetup(setup) : null;

        /// <summary>
        /// Gets the layer table, in the order the file lists it.
        /// </summary>
        /// <remarks>
        /// Each entry is a form whose token is the layer's ordinal — <c>(0 "F.Cu" signal)</c> — so
        /// unlike the rest of the board this cannot be a <see cref="KiCadNodeList{T}"/>, which
        /// selects children by one shared token. It is still live: the list is the children of the
        /// <c>layers</c> form, read at the moment you ask.
        /// </remarks>
        public IReadOnlyList<KiCadBoardLayer> Layers =>
            _root.GetChild(KiCadTokens.Common.Layers) is { } layers
                ? layers.Children.Select(c => new KiCadBoardLayer(c)).ToArray()
                : Array.Empty<KiCadBoardLayer>();

        /// <summary>Gets the net list, as a live view over the board's <c>net</c> children.</summary>
        /// <remarks>
        /// Empty on a KiCad 10 board, and correctly so: board format <c>20260206</c> has no net table
        /// — copper, zones and pads each name their own net. MEASURED on a board saved by pcbnew
        /// 10.0.6: zero <c>(net …)</c> forms at the top level. Read the net off the item there, through
        /// <see cref="KiCadTrackItem.NetName"/>, <see cref="KiCadZone.NetName"/> or
        /// <see cref="KiCadPad.Net"/>.
        /// </remarks>
        public KiCadNodeList<KiCadNet> Nets => new(_root, KiCadTokens.Common.Net, n => new KiCadNet(n));

        /// <summary>
        /// Gets the placed footprints, as the same <see cref="KiCadFootprint"/> view a
        /// <c>.kicad_mod</c> gives — a footprint on a board and a footprint in a file are one type,
        /// because they are one s-expression form.
        /// </summary>
        /// <remarks>
        /// KiCad 5 spelled the form <c>module</c>. A board old enough to use that spelling is
        /// readable through <see cref="KiCadFootprintLibrary"/>, which accepts both tokens; this
        /// list is the KiCad 6+ <c>footprint</c> spelling only.
        /// </remarks>
        public KiCadNodeList<KiCadFootprint> Footprints => new(_root, KiCadTokens.Footprint.Root, n => new KiCadFootprint(n));

        /// <summary>Gets the straight track segments.</summary>
        public KiCadNodeList<KiCadTrackSegment> Segments => new(_root, KiCadTokens.Board.Segment, n => new KiCadTrackSegment(n));

        /// <summary>Gets the curved track segments.</summary>
        public KiCadNodeList<KiCadTrackArc> TrackArcs => new(_root, KiCadTokens.Board.TrackArc, n => new KiCadTrackArc(n));

        /// <summary>Gets the vias.</summary>
        public KiCadNodeList<KiCadVia> Vias => new(_root, KiCadTokens.Board.Via, n => new KiCadVia(n));

        /// <summary>Gets the copper zones and keepouts.</summary>
        public KiCadNodeList<KiCadZone> Zones => new(_root, KiCadTokens.Board.Zone, n => new KiCadZone(n));

        /// <summary>Gets the board-level lines — silkscreen, courtyard, board outline.</summary>
        public KiCadNodeList<KiCadGrLine> GraphicLines => new(_root, KiCadTokens.Board.GrLine, n => new KiCadGrLine(n));

        /// <summary>Gets the board-level rectangles.</summary>
        public KiCadNodeList<KiCadGrRect> GraphicRectangles => new(_root, KiCadTokens.Board.GrRect, n => new KiCadGrRect(n));

        /// <summary>Gets the board-level circles.</summary>
        public KiCadNodeList<KiCadGrCircle> GraphicCircles => new(_root, KiCadTokens.Board.GrCircle, n => new KiCadGrCircle(n));

        /// <summary>Gets the board-level arcs.</summary>
        public KiCadNodeList<KiCadGrArc> GraphicArcs => new(_root, KiCadTokens.Board.GrArc, n => new KiCadGrArc(n));

        /// <summary>Gets the board-level polygons.</summary>
        public KiCadNodeList<KiCadGrPoly> GraphicPolygons => new(_root, KiCadTokens.Board.GrPoly, n => new KiCadGrPoly(n));

        /// <summary>Gets the board-level Bézier curves.</summary>
        public KiCadNodeList<KiCadGrCurve> GraphicCurves => new(_root, KiCadTokens.Board.GrCurve, n => new KiCadGrCurve(n));

        /// <summary>Gets the board-level text.</summary>
        public KiCadNodeList<KiCadGrText> Texts => new(_root, KiCadTokens.Board.GrText, n => new KiCadGrText(n));

        /// <summary>Gets the dimensions — the measured annotations on the drawing layers.</summary>
        public KiCadNodeList<KiCadDimension> Dimensions => new(_root, KiCadTokens.Board.Dimension, n => new KiCadDimension(n));

        /// <summary>Gets the groups, each naming the items it holds by their UUIDs.</summary>
        public KiCadNodeList<KiCadGroup> Groups => new(_root, KiCadTokens.Board.Group, n => new KiCadGroup(n));

        /// <summary>
        /// Gets or sets whether the file carries its fonts inside it, the KiCad 9
        /// <c>(embedded_fonts no)</c> flag.
        /// </summary>
        public bool EmbeddedFonts
        {
            get => _root.GetChild(KiCadTokens.Common.EmbeddedFonts) is { } flag && (flag.GetValue(0) is null || flag.GetValueAsBool());
            set => _root.SetChildValue(KiCadTokens.Common.EmbeddedFonts, value ? KiCadTokens.Common.Yes : KiCadTokens.Common.No, SQuoteStyle.Bare);
        }

        /// <summary>Loads a board.</summary>
        /// <param name="filePath">Path to a <c>.kicad_pcb</c>.</param>
        /// <returns>The board.</returns>
        public static KiCadBoard Load(string filePath) => new(SDocument.Load(filePath));

        /// <summary>Loads a board asynchronously.</summary>
        /// <param name="filePath">Path to a <c>.kicad_pcb</c>.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The board.</returns>
        public static async Task<KiCadBoard> LoadAsync(string filePath, CancellationToken cancellationToken = default) =>
            new(await SDocument.LoadAsync(filePath, cancellationToken).ConfigureAwait(false));

        /// <summary>Parses a board from text.</summary>
        /// <param name="text">The file contents.</param>
        /// <returns>The board.</returns>
        public static KiCadBoard Parse(string text) => new(SDocument.Parse(text));

        /// <summary>
        /// Writes the board back out. An untouched board comes out byte for byte; a changed one
        /// differs only where it was changed.
        /// </summary>
        /// <param name="filePath">Destination path.</param>
        public void Save(string filePath) => _document.Save(filePath);

        /// <summary>Writes the board back out asynchronously.</summary>
        /// <param name="filePath">Destination path.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the file is written.</returns>
        public Task SaveAsync(string filePath, CancellationToken cancellationToken = default) =>
            _document.SaveAsync(filePath, cancellationToken);

        /// <summary>Renders the board to text.</summary>
        /// <returns>The file contents.</returns>
        public string ToText() => _document.ToText();

        /// <summary>Gets the <c>general</c> form, adding an empty one when the file has none.</summary>
        /// <returns>The view.</returns>
        public KiCadBoardGeneral RequireGeneral() => new(_root.GetChild(KiCadTokens.Board.General) ?? _root.CreateChild(KiCadTokens.Board.General));

        /// <summary>Gets the title block, adding an empty one when the file has none.</summary>
        /// <returns>The view.</returns>
        public KiCadTitleBlock RequireTitleBlock() => new(_root.GetChild(KiCadTokens.Common.TitleBlock) ?? _root.CreateChild(KiCadTokens.Common.TitleBlock));

        /// <summary>Gets the setup, adding an empty one when the file has none.</summary>
        /// <returns>The view.</returns>
        public KiCadBoardSetup RequireSetup() => new(_root.GetChild(KiCadTokens.Board.Setup) ?? _root.CreateChild(KiCadTokens.Board.Setup));

        /// <summary>Gets the layer with the given canonical name, e.g. <c>F.Cu</c>.</summary>
        /// <param name="name">The layer name as the file spells it, not its user name.</param>
        /// <returns>The layer, or <see langword="null"/>.</returns>
        public KiCadBoardLayer? GetLayer(string name) =>
            Layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.Ordinal));

        /// <summary>Gets the net with the given code.</summary>
        /// <param name="code">The net code; 0 is the no-net net every board carries.</param>
        /// <returns>The net, or <see langword="null"/>.</returns>
        public KiCadNet? GetNet(int code) => Nets.FirstOrDefault(n => n.Code == code);

        /// <summary>Gets the net with the given name.</summary>
        /// <param name="name">The net name, e.g. <c>GND</c>.</param>
        /// <returns>The net, or <see langword="null"/>.</returns>
        public KiCadNet? GetNet(string name) =>
            Nets.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.Ordinal));

        /// <summary>Appends a net.</summary>
        /// <param name="code">The net code, which must be unique in the file.</param>
        /// <param name="name">The net name.</param>
        /// <returns>The net.</returns>
        public KiCadNet AddNet(int code, string name)
        {
            var net = new KiCadNet(code, name);
            _root.AddChild(net.Node);
            return net;
        }

        private static void RequireBoardToken(SExpression expression, string? parameterName)
        {
            if (string.Equals(expression.Token, KiCadTokens.Board.Root, StringComparison.Ordinal))
            {
                return;
            }

            var message = $"Expected a ({KiCadTokens.Board.Root} ...) form but got ({expression.Token} ...).";
            throw parameterName is null
                ? new InvalidOperationException(message)
                : new ArgumentException(message, parameterName);
        }
    }

    /// <summary>
    /// The board-wide numbers: <c>(general (thickness 1.6) (legacy_teardrops no))</c>.
    /// </summary>
    public sealed class KiCadBoardGeneral : KiCadNode
    {
        /// <summary>Creates a view over a <c>(general …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadBoardGeneral(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the finished board thickness in millimetres.</summary>
        public double Thickness
        {
            get => ReadChildDouble(KiCadTokens.Common.Thickness, 0, 1.6);
            set => WriteChildDouble(KiCadTokens.Common.Thickness, value);
        }

        /// <summary>
        /// Gets or sets whether teardrops are the pre-KiCad 7 kind that were drawn as zones rather
        /// than generated. KiCad writes the flag on every board so that opening an old file cannot
        /// silently change its copper.
        /// </summary>
        public bool LegacyTeardrops
        {
            get => ReadFlag(KiCadTokens.Board.LegacyTeardrops);
            set => WriteFlag(KiCadTokens.Board.LegacyTeardrops, value);
        }
    }

    /// <summary>
    /// The sheet's title block: <c>(title_block (title "…") (date "…") (rev "…") (company "…")
    /// (comment 1 "…"))</c>.
    /// </summary>
    /// <remarks>
    /// Every field is optional and KiCad omits the ones that are blank, so setting one to
    /// <see langword="null"/> removes it rather than writing an empty string — that is what keeps a
    /// board KiCad wrote and a board this library wrote spelled the same way.
    /// </remarks>
    public sealed class KiCadTitleBlock : KiCadNode
    {
        /// <summary>Creates a view over a <c>(title_block …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadTitleBlock(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the sheet title.</summary>
        public string? Title
        {
            get => ReadChild(KiCadTokens.Common.Title);
            set => WriteChild(KiCadTokens.Common.Title, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the sheet date, as free text — KiCad does not parse it.</summary>
        public string? Date
        {
            get => ReadChild(KiCadTokens.Common.Date);
            set => WriteChild(KiCadTokens.Common.Date, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the revision, the <c>(rev "…")</c> token.</summary>
        public string? Revision
        {
            get => ReadChild(KiCadTokens.Common.Rev);
            set => WriteChild(KiCadTokens.Common.Rev, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the company name.</summary>
        public string? Company
        {
            get => ReadChild(KiCadTokens.Common.Company);
            set => WriteChild(KiCadTokens.Common.Company, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets the numbered comment lines, as a live view over the <c>comment</c> children.</summary>
        public KiCadNodeList<KiCadTitleBlockComment> Comments =>
            new(Node, KiCadTokens.Common.Comment, n => new KiCadTitleBlockComment(n));

        /// <summary>
        /// The numbers of the comment lines the file actually carries, in document order. KiCad's
        /// dialog offers nine slots and writes only the ones that were filled in.
        /// </summary>
        public IReadOnlyList<int> CommentNumbers =>
            Node.GetChildren(KiCadTokens.Common.Comment)
                .Select(c => c.TryGetValue<int>(0, out var number) ? number : 0)
                .Where(number => number > 0)
                .ToArray();

        /// <summary>Gets the text of the comment with the given number.</summary>
        /// <param name="number">The comment line, 1 to 9.</param>
        /// <returns>The text, or <see langword="null"/> when that line is blank.</returns>
        public string? GetComment(int number) =>
            Comments.FirstOrDefault(c => c.Number == number)?.Text;

        /// <summary>Sets the text of a comment line, adding the line when it is not there yet.</summary>
        /// <param name="number">The comment line, 1 to 9.</param>
        /// <param name="text">The text, or <see langword="null"/> to drop the line entirely.</param>
        /// <returns>The comment, or <see langword="null"/> when the line was dropped.</returns>
        public KiCadTitleBlockComment? SetComment(int number, string? text)
        {
            var existing = Comments.FirstOrDefault(c => c.Number == number);

            if (text is null)
            {
                if (existing is not null)
                {
                    Node.Children.Remove(existing.Node);
                }

                return null;
            }

            if (existing is not null)
            {
                existing.Text = text;
                return existing;
            }

            var comment = new KiCadTitleBlockComment(number, text);
            Node.AddChild(comment.Node);
            return comment;
        }
    }

    /// <summary>One numbered line of a title block: <c>(comment 1 "…")</c>.</summary>
    public sealed class KiCadTitleBlockComment : KiCadNode
    {
        /// <summary>Creates a view over a <c>(comment …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadTitleBlockComment(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates a comment line.</summary>
        /// <param name="number">The comment line, 1 to 9.</param>
        /// <param name="text">The text.</param>
        public KiCadTitleBlockComment(int number, string text)
            : base(new SExpression(KiCadTokens.Common.Comment))
        {
            ArgumentNullException.ThrowIfNull(text);
            Node.AddValue(number.ToString(CultureInfo.InvariantCulture), SQuoteStyle.Bare);
            Node.AddValue(text, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets which of the nine comment lines this is.</summary>
        public int Number
        {
            get => Node.TryGetValue<int>(0, out var value) ? value : 0;
            set => WriteValue(0, value.ToString(CultureInfo.InvariantCulture), SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets the text of the line.</summary>
        public string Text
        {
            get => Node.GetValue(1) ?? string.Empty;
            set => WriteValue(1, value, SQuoteStyle.Quoted);
        }
    }

    /// <summary>
    /// One row of the layer table: <c>(0 "F.Cu" signal)</c>, or <c>(5 "F.SilkS" user
    /// "F.Silkscreen")</c> when the layer has been renamed.
    /// </summary>
    /// <remarks>
    /// The form's token is the layer's ordinal, not a word. That is unusual enough to be worth
    /// saying twice: everything else on a board is <c>(token …)</c> with a fixed token, so the layer
    /// table is the one place a <see cref="KiCadNodeList{T}"/> cannot be used.
    /// </remarks>
    public sealed class KiCadBoardLayer : KiCadNode
    {
        /// <summary>Creates a view over one row of a <c>(layers …)</c> form.</summary>
        /// <param name="node">The row.</param>
        public KiCadBoardLayer(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates a layer row.</summary>
        /// <param name="ordinal">KiCad's layer number: even for copper, odd for the rest.</param>
        /// <param name="name">The canonical name, e.g. <c>F.Cu</c>.</param>
        /// <param name="type">The layer type: <c>signal</c>, <c>power</c>, <c>mixed</c>, <c>jumper</c> or <c>user</c>.</param>
        /// <param name="userName">The name shown in the editor, when it differs from <paramref name="name"/>.</param>
        public KiCadBoardLayer(int ordinal, string name, string type, string? userName = null)
            : base(new SExpression(ordinal.ToString(CultureInfo.InvariantCulture)))
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(type);
            Node.AddValue(name, SQuoteStyle.Quoted);
            Node.AddValue(type, SQuoteStyle.Bare);
            if (userName is not null)
            {
                Node.AddValue(userName, SQuoteStyle.Quoted);
            }
        }

        /// <summary>
        /// Gets or sets KiCad's layer number, which is the form's token. Every other reference to a
        /// layer in the file is by <see cref="Name"/>, so renumbering a layer that is in use is a
        /// board-wide edit, not a local one.
        /// </summary>
        public int Ordinal
        {
            get => int.TryParse(Node.Token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;
            set => Node.Token = value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Gets or sets the canonical layer name — what tracks, pads and zones name.</summary>
        public string Name
        {
            get => Node.GetValue(0) ?? string.Empty;
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the layer type: <c>signal</c>, <c>power</c>, <c>mixed</c>, <c>jumper</c> or <c>user</c>.</summary>
        public string Type
        {
            get => Node.GetValue(1) ?? KiCadLayerNames.TypeUser;
            set => WriteValue(1, value, SQuoteStyle.Bare);
        }

        /// <summary>
        /// Gets or sets the name shown in the editor, present only when it differs from
        /// <see cref="Name"/> — <c>(5 "F.SilkS" user "F.Silkscreen")</c>. <see langword="null"/>
        /// means the layer goes by its canonical name.
        /// </summary>
        public string? UserName
        {
            get => Node.GetValue(2);
            set
            {
                if (value is null)
                {
                    if (Node.Values.Count > 2)
                    {
                        Node.Values.RemoveAt(2);
                    }

                    return;
                }

                WriteValue(2, value, SQuoteStyle.Quoted);
            }
        }

        /// <summary>True when the layer carries copper, which is what <c>signal</c>, <c>power</c>, <c>mixed</c> and <c>jumper</c> all mean.</summary>
        public bool IsCopper => !string.Equals(Type, KiCadLayerNames.TypeUser, StringComparison.Ordinal);
    }

    /// <summary>
    /// The board's manufacturing settings: <c>(setup (stackup …) (pad_to_mask_clearance 0)
    /// (pcbplotparams …) …)</c>.
    /// </summary>
    /// <remarks>
    /// Only the handful of settings that are read often are named here. The rest — the plot
    /// parameters, the via and teardrop defaults, the solder-mask rules — are reachable through
    /// <see cref="KiCadNode.Node"/> and survive a save whether or not anyone reaches for them.
    /// </remarks>
    public sealed class KiCadBoardSetup : KiCadNode
    {
        /// <summary>Creates a view over a <c>(setup …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadBoardSetup(SExpression node)
            : base(node)
        {
        }

        /// <summary>
        /// Gets the physical stackup, or <see langword="null"/>. A board whose stackup has never
        /// been opened in the board setup dialog has none, and KiCad falls back to a default one
        /// derived from the layer table.
        /// </summary>
        public KiCadStackup? Stackup =>
            Node.GetChild(KiCadTokens.Board.Stackup) is { } stackup ? new KiCadStackup(stackup) : null;

        /// <summary>Gets or sets how far the solder mask is pulled back from a pad, in millimetres.</summary>
        public double PadToMaskClearance
        {
            get => ReadChildDouble(KiCadTokens.Board.PadToMaskClearance);
            set => WriteChildDouble(KiCadTokens.Board.PadToMaskClearance, value);
        }

        /// <summary>Gets or sets how far the paste stencil aperture is inset from a pad, in millimetres.</summary>
        public double PadToPasteClearance
        {
            get => ReadChildDouble(KiCadTokens.Board.PadToPasteClearance);
            set => WriteChildDouble(KiCadTokens.Board.PadToPasteClearance, value);
        }

        /// <summary>Gets or sets whether mask slivers between a footprint's own pads are allowed.</summary>
        public bool AllowSoldermaskBridgesInFootprints
        {
            get => ReadFlag(KiCadTokens.Board.AllowSoldermaskBridgesInFootprints);
            set => WriteFlag(KiCadTokens.Board.AllowSoldermaskBridgesInFootprints, value);
        }

        /// <summary>Gets the stackup, adding an empty one when the setup has none.</summary>
        /// <returns>The view.</returns>
        public KiCadStackup RequireStackup() => new(Require(KiCadTokens.Board.Stackup));
    }

    /// <summary>
    /// The physical stackup: <c>(stackup (layer "F.Cu" (type "copper") (thickness 0.035)) …
    /// (copper_finish "HASL") (dielectric_constraints no))</c>.
    /// </summary>
    /// <remarks>
    /// The stackup lists more layers than the layer table does, because it also names the dielectric
    /// between the copper — <c>dielectric 1</c>, <c>dielectric 2</c> — which has no ordinal and
    /// appears nowhere else in the file.
    /// </remarks>
    public sealed class KiCadStackup : KiCadNode
    {
        /// <summary>Creates a view over a <c>(stackup …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadStackup(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets the stackup layers, top to bottom, as a live view over the <c>layer</c> children.</summary>
        public KiCadNodeList<KiCadStackupLayer> Layers => new(Node, KiCadTokens.Common.Layer, n => new KiCadStackupLayer(n));

        /// <summary>Gets or sets the surface finish, e.g. <c>HASL</c>, <c>ENIG</c>.</summary>
        public string? CopperFinish
        {
            get => ReadChild(KiCadTokens.Board.CopperFinish);
            set => WriteChild(KiCadTokens.Board.CopperFinish, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets whether the fabricator must hold the declared dielectric constants.</summary>
        public bool DielectricConstraints
        {
            get => ReadFlag(KiCadTokens.Board.DielectricConstraints);
            set => WriteFlag(KiCadTokens.Board.DielectricConstraints, value);
        }

        /// <summary>Gets the total of every layer thickness the stackup declares, in millimetres.</summary>
        /// <remarks>
        /// This is a sum, not a stored value: the silk, mask and paste layers carry no thickness and
        /// contribute nothing, so it comes out close to but not identical to
        /// <see cref="KiCadBoardGeneral.Thickness"/>, which is the finished board.
        /// </remarks>
        public double TotalThickness => Layers.Sum(l => l.Thickness);

        /// <summary>Gets the stackup layer with the given name, e.g. <c>In1.Cu</c> or <c>dielectric 1</c>.</summary>
        /// <param name="name">The name as the stackup spells it.</param>
        /// <returns>The layer, or <see langword="null"/>.</returns>
        public KiCadStackupLayer? GetLayer(string name) =>
            Layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// One layer of the stackup: <c>(layer "dielectric 1" (type "prepreg") (thickness 0.2104)
    /// (material "FR4"))</c>.
    /// </summary>
    public sealed class KiCadStackupLayer : KiCadNode
    {
        /// <summary>Creates a view over a stackup <c>(layer …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadStackupLayer(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the layer name: a board layer's name, or <c>dielectric n</c>.</summary>
        public string Name
        {
            get => Node.GetValue(0) ?? string.Empty;
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets or sets what the layer is made of: <c>copper</c>, <c>core</c>, <c>prepreg</c>, or one
        /// of the descriptive names KiCad writes for the outer layers, e.g. <c>Top Solder Mask</c>.
        /// </summary>
        public string? Type
        {
            get => ReadChild(KiCadTokens.Common.Type);
            set => WriteChild(KiCadTokens.Common.Type, value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets or sets the layer thickness in millimetres, 0 for the silk, mask and paste layers,
        /// which KiCad writes without one.
        /// </summary>
        public double Thickness
        {
            get => ReadChildDouble(KiCadTokens.Common.Thickness);
            set => WriteChildDouble(KiCadTokens.Common.Thickness, value);
        }

        /// <summary>Gets or sets the material name, e.g. <c>FR4</c>.</summary>
        public string? Material
        {
            get => ReadChild(KiCadTokens.Board.Material);
            set => WriteChild(KiCadTokens.Board.Material, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the relative permittivity of a dielectric layer, 0 when it declares none.</summary>
        public double EpsilonR
        {
            get => ReadChildDouble(KiCadTokens.Board.EpsilonR);
            set => WriteChildDouble(KiCadTokens.Board.EpsilonR, value);
        }

        /// <summary>Gets or sets the dielectric loss tangent, 0 when the layer declares none.</summary>
        public double LossTangent
        {
            get => ReadChildDouble(KiCadTokens.Board.LossTangent);
            set => WriteChildDouble(KiCadTokens.Board.LossTangent, value);
        }

        /// <summary>Gets or sets the colour the fabricator should use, e.g. the mask's <c>green</c>.</summary>
        public string? Color
        {
            get => ReadChild(KiCadTokens.Common.Color);
            set => WriteChild(KiCadTokens.Common.Color, value, SQuoteStyle.Quoted);
        }

        /// <summary>True when the layer is copper rather than dielectric or a surface finish.</summary>
        public bool IsCopper => string.Equals(Type, KiCadLayerNames.TypeCopper, StringComparison.Ordinal);
    }

    /// <summary>A net: <c>(net 1 "GND")</c>.</summary>
    /// <remarks>
    /// <para>
    /// Up to KiCad 9 the code is the only thing copper points at — a <c>(segment … (net 1))</c> names
    /// the number, never the name — so renaming a net is a one-place edit and renumbering one is not.
    /// </para>
    /// <para>
    /// KiCad 10 turned that around: there is no table, and copper carries the name. A board in that
    /// format has no <c>KiCadNet</c> in it at all; see <see cref="KiCadBoard.Nets"/>.
    /// </para>
    /// </remarks>
    public sealed class KiCadNet : KiCadNode
    {
        /// <summary>Creates a view over a <c>(net …)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadNet(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates a net.</summary>
        /// <param name="code">The net code.</param>
        /// <param name="name">The net name.</param>
        public KiCadNet(int code, string name)
            : base(new SExpression(KiCadTokens.Common.Net))
        {
            ArgumentNullException.ThrowIfNull(name);
            Node.AddValue(code.ToString(CultureInfo.InvariantCulture), SQuoteStyle.Bare);
            Node.AddValue(name, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the net code. Net 0 is the no-net net and every board has it.</summary>
        public int Code
        {
            get => Node.TryGetValue<int>(0, out var value) ? value : 0;
            set => WriteValue(0, value.ToString(CultureInfo.InvariantCulture), SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets the net name. Net 0's name is the empty string.</summary>
        public string Name
        {
            get => Node.GetValue(1) ?? string.Empty;
            set => WriteValue(1, value, SQuoteStyle.Quoted);
        }
    }
}
