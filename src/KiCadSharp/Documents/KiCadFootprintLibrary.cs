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
        /// <summary>
        /// The root tokens a file of footprints may have: KiCad 6+ <c>footprint</c>, KiCad 5
        /// <c>module</c>, and <c>kicad_pcb</c> for the footprints placed on a board.
        /// </summary>
        private static readonly string[] RootTokens =
            [KiCadTokens.Footprint.Root, KiCadTokens.Footprint.LegacyRoot, KiCadTokens.Board.Root];

        private readonly SDocument _document;
        private readonly SExpression _root;
        private readonly bool _rootIsFootprint;

        /// <summary>Creates a new, empty board-shaped library.</summary>
        /// <param name="generator">The value of the <c>generator</c> token.</param>
        /// <param name="version">The value of the <c>version</c> token.</param>
        /// <remarks>
        /// It starts with the layer table of a two-layer board, the one a new <see cref="KiCadBoard"/>
        /// starts with. KiCad writes a layer table on every board, and refuses one whose table holds no
        /// copper layers: "0 is not a valid layer count" (#74).
        /// </remarks>
        public KiCadFootprintLibrary(string? generator = null, string? version = null)
        {
            generator ??= KiCadDefaults.LibraryGenerator;
            version ??= KiCadDefaults.FootprintLibraryVersion;

            _root = new SExpression(KiCadTokens.Board.Root);
            _root.CreateChild(KiCadTokens.Common.Version, version);
            _root.CreateChild(KiCadTokens.Common.Generator).AddValue(generator, SQuoteStyle.Quoted);
            _root.CreateChild(KiCadTokens.Board.General);
            _root.CreateChild(KiCadTokens.Common.Paper).AddValue(KiCadDefaults.Paper, SQuoteStyle.Quoted);
            KiCadBoard.AddDefaultLayers(_root);
            _document = new SDocument();
            _document.Add(_root);
            _rootIsFootprint = false;
        }

        /// <summary>Creates a library over an existing s-expression.</summary>
        /// <param name="expression">A board form, or a single <c>footprint</c>/<c>module</c> form.</param>
        /// <exception cref="ArgumentException">The form is not a <c>footprint</c>, <c>module</c> or <c>kicad_pcb</c>.</exception>
        public KiCadFootprintLibrary(SExpression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            KiCadDocumentRoot.RequireArgument(expression, nameof(expression), RootTokens);
            _root = expression;
            _rootIsFootprint = IsFootprintToken(expression.Token);
            _document = new SDocument();
            _document.Add(expression);
        }

        private KiCadFootprintLibrary(SDocument document, string? filePath)
        {
            _document = document;
            _root = KiCadDocumentRoot.Require(document, filePath, "footprint or board", RootTokens);
            _rootIsFootprint = IsFootprintToken(_root.Token);
        }

        /// <summary>Gets the whole parsed file.</summary>
        public SDocument Document => _document;

        /// <summary>Gets the root form: a board, or the footprint itself for a <c>.kicad_mod</c>.</summary>
        public SExpression Node => _root;

        /// <summary>True when the file is a single footprint rather than a board.</summary>
        public bool IsSingleFootprint => _rootIsFootprint;

        /// <summary>
        /// Gets or sets the file format version the file declares, its root's <c>(version …)</c>, or
        /// <see langword="null"/> when it declares none. Setting <see langword="null"/> removes it.
        /// </summary>
        /// <remarks>
        /// <see langword="null"/> is not a format this library assumes for the file; KiCad has its own
        /// answer. It reads a <c>.kicad_mod</c> with no version as format 0, KiCad 5's
        /// (<c>pcb_io_kicad_sexpr_parser.cpp</c>, line 104), and a board with none as <c>20201115</c>
        /// (line 1679). For a <c>.kicad_mod</c> this is the footprint's own
        /// <see cref="KiCadFootprint.Version"/>, and the two always agree (#76). A version set on a
        /// file that has none goes first, where KiCad reads it.
        /// </remarks>
        public string? Version
        {
            get => _root.GetChildValue(KiCadTokens.Common.Version);
            set
            {
                if (value is null)
                {
                    _root.RemoveChild(KiCadTokens.Common.Version);
                    return;
                }

                KiCadChildOrder.Place(_root, KiCadTokens.Common.Version);
                _root.SetChildValue(KiCadTokens.Common.Version, value, SQuoteStyle.Bare);
            }
        }

        /// <summary>
        /// Gets or sets the name of the program that wrote the file, or <see langword="null"/> when
        /// the file names none, as a KiCad 5 <c>module</c> does not. Setting <see langword="null"/>
        /// removes it.
        /// </summary>
        /// <remarks>
        /// It used to report <see cref="KiCadDefaults.LibraryGenerator"/> for a file that names no
        /// generator (#95). KiCad reads nothing by it (<c>pcb_io_kicad_sexpr_parser.cpp</c>, lines 1161
        /// and 5053).
        /// </remarks>
        public string? Generator
        {
            get => _root.GetChildValue(KiCadTokens.Common.Generator);
            set
            {
                if (value is null)
                {
                    _root.RemoveChild(KiCadTokens.Common.Generator);
                    return;
                }

                _root.SetChildValue(KiCadTokens.Common.Generator, value, SQuoteStyle.Quoted);
            }
        }

        /// <summary>
        /// Gets the footprints in the file. For a <c>.kicad_mod</c> this is the root form itself,
        /// as one element; for a board it is its <c>footprint</c> children, with KiCad 5's
        /// <c>module</c> children included, as they are at the moment you ask.
        /// </summary>
        /// <remarks>
        /// A <c>.kicad_mod</c> whose footprint has been moved into another file through
        /// <see cref="AddFootprint"/> no longer holds it, and this is then empty: the form belongs to
        /// the other file now, and a view of it here would edit that file.
        /// </remarks>
        public IReadOnlyList<KiCadFootprint> Footprints
        {
            get
            {
                if (_rootIsFootprint)
                {
                    return ReferenceEquals(_document.Root, _root)
                        ? new[] { new KiCadFootprint(_root) }
                        : Array.Empty<KiCadFootprint>();
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
        /// <exception cref="KiCadDocumentTypeException">
        /// The file is not a footprint or a board: its root is not <c>(footprint …)</c>, <c>(module …)</c>
        /// or <c>(kicad_pcb …)</c>, or it holds no form at all.
        /// </exception>
        /// <exception cref="SExpressionFormatException">The file is not well-formed s-expression text.</exception>
        public static KiCadFootprintLibrary Load(string filePath) => new(SDocument.Load(filePath), filePath);

        /// <summary>Loads a file asynchronously.</summary>
        /// <param name="filePath">Path to a <c>.kicad_pcb</c> or <c>.kicad_mod</c>.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The library.</returns>
        /// <exception cref="KiCadDocumentTypeException">
        /// The file is not a footprint or a board: its root is not <c>(footprint …)</c>, <c>(module …)</c>
        /// or <c>(kicad_pcb …)</c>, or it holds no form at all.
        /// </exception>
        /// <exception cref="SExpressionFormatException">The file is not well-formed s-expression text.</exception>
        public static async Task<KiCadFootprintLibrary> LoadAsync(string filePath, CancellationToken cancellationToken = default) =>
            new(await SDocument.LoadAsync(filePath, cancellationToken).ConfigureAwait(false), filePath);

        /// <summary>Parses a file from text.</summary>
        /// <param name="text">The file contents.</param>
        /// <returns>The library.</returns>
        /// <exception cref="KiCadDocumentTypeException">
        /// The text is not a footprint or a board: its root is not <c>(footprint …)</c>, <c>(module …)</c>
        /// or <c>(kicad_pcb …)</c>, or it holds no form at all.
        /// </exception>
        /// <exception cref="SExpressionFormatException">The text is not well-formed s-expression text.</exception>
        public static KiCadFootprintLibrary Parse(string text) => new(SDocument.Parse(text), null);

        /// <summary>Appends a footprint, moving it out of wherever it was.</summary>
        /// <param name="footprint">The footprint.</param>
        /// <returns>The footprint, now a child of this file.</returns>
        /// <exception cref="InvalidOperationException">The file is a single footprint and cannot hold another.</exception>
        /// <remarks>
        /// <para>
        /// A footprint taken from another board leaves that board, and one taken from a
        /// <c>.kicad_mod</c> leaves that file empty: the node itself moves, bytes and all (see
        /// <see cref="KiCadNode"/>). To keep the source as it was, add
        /// <c>new KiCadFootprint(footprint.Node.Clone())</c>, or <see cref="KiCadFootprint.CloneAs"/>.
        /// </para>
        /// <para>
        /// The file this adds to is a <c>kicad_pcb</c>, and KiCad reads it as a board. On its way in,
        /// the footprint loses the <c>version</c>, <c>generator</c> and <c>generator_version</c> it
        /// carried as a file of its own, as pcbnew writes a footprint inside a board; its
        /// <see cref="KiCadFootprint.Version"/> then reads <see langword="null"/>, and the file's
        /// <see cref="Version"/> is the one KiCad reads it under (#73). A footprint with none, which
        /// is every footprint on a board KiCad wrote, moves untouched.
        /// </para>
        /// </remarks>
        public KiCadFootprint AddFootprint(KiCadFootprint footprint)
        {
            ArgumentNullException.ThrowIfNull(footprint);
            if (_rootIsFootprint)
            {
                throw new InvalidOperationException("A .kicad_mod holds exactly one footprint, at the root of the file.");
            }

            _root.AddChild(footprint.Node);
            KiCadFootprint.PlaceOnBoard(footprint.Node);
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
        /// <remarks>
        /// The footprint's own bytes are reproduced; it is not re-formatted. The file ends with the
        /// line ending the footprint was read with. A footprint built in memory, or one written on
        /// a single line, ends with <c>\n</c>, which is what KiCad writes on every platform.
        /// </remarks>
        public static void SaveFootprint(KiCadFootprint footprint, string filePath)
        {
            ArgumentNullException.ThrowIfNull(footprint);
            var options = new SExpressionWriterOptions { NewLine = NewLineOf(footprint.Node.SourceSpan) };
            new SExpressionWriter(options).WriteToFile(footprint.Node, filePath);
        }

        /// <summary>
        /// The line ending a form's source uses: the one at its first line break, or <c>\n</c> when
        /// it has none.
        /// </summary>
        /// <remarks>
        /// The trailing newline of a <c>.kicad_mod</c> is not part of the <c>footprint</c> form, so
        /// the writer adds one of its own, and until #107 that was always <c>\n</c>. MEASURED on
        /// Linux with a CRLF footprint: the form's bytes came back with CRLF and the file ended
        /// <c>)\n</c>, a file with mixed endings. On a Windows checkout with
        /// <c>core.autocrlf=true</c> that is every footprint in a test's string literal.
        /// </remarks>
        private static string NewLineOf(ReadOnlySpan<char> source)
        {
            var lineFeed = source.IndexOf('\n');
            return lineFeed > 0 && source[lineFeed - 1] == '\r' ? "\r\n" : "\n";
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

        /// <summary>Creates a new footprint with the fields KiCad gives every footprint.</summary>
        /// <param name="id">The footprint's name.</param>
        /// <remarks>
        /// <para>
        /// The footprint starts with the <c>(version …)</c> KiCad 10.0.6 writes,
        /// <see cref="KiCadDefaults.FootprintVersion"/>, and a <c>(generator …)</c>, in the place
        /// KiCad writes them; see <see cref="Version"/>.
        /// </para>
        /// <para>
        /// Its reference, value, datasheet and description are the four fields KiCad 10.0.6 gives
        /// every footprint, on the layers and with the visibility it gives them
        /// (<c>pcbnew/footprint.cpp</c>, lines 114–117), written as KiCad writes a field:
        /// <c>(property "Reference" "REF**" (at 0 0 0) (layer "F.SilkS"))</c>, then
        /// <c>(property "Value" "&lt;id&gt;" (at 0 1.27 0) (layer "F.Fab"))</c>, and the empty
        /// <c>Datasheet</c> and <c>Description</c> on <c>F.Fab</c> with <c>(hide yes)</c>
        /// (<c>pcb_io_kicad_sexpr.cpp</c>, lines 1243–1254 and 2303–2313). So
        /// <see cref="GetPropertyValue"/> finds them on a new footprint as it does on one KiCad wrote,
        /// and <see cref="TextItems"/> starts empty. The <c>(fp_text reference …)</c> and
        /// <c>(fp_text value …)</c> this constructor used to write date from before format
        /// <c>20230620</c>, when fields replaced them (<c>pcb_io_kicad_sexpr_parser.cpp</c>, line 5149;
        /// #75). A footprint read from a file keeps whichever form it has.
        /// </para>
        /// </remarks>
        public KiCadFootprint(string id)
            : base(new SExpression(KiCadTokens.Footprint.Root))
        {
            ArgumentNullException.ThrowIfNull(id);
            Node.AddValue(id, SQuoteStyle.Quoted);
            Version = KiCadDefaults.FootprintVersion;
            WriteChild(KiCadTokens.Common.Generator, KiCadDefaults.LibraryGenerator, SQuoteStyle.Quoted);
            Node.SetChildValue(KiCadTokens.Common.Layer, KiCadLayerNames.FCu, SQuoteStyle.Quoted);
            AddField(KiCadPropertyNames.Reference, "REF**", new KiCadPosition(0, 0), KiCadLayerNames.FSilkS, hide: false);
            AddField(KiCadPropertyNames.Value, id, new KiCadPosition(0, 1.27), KiCadLayerNames.FFab, hide: false);
            AddField(KiCadPropertyNames.Datasheet, string.Empty, new KiCadPosition(0, 0), KiCadLayerNames.FFab, hide: true);
            AddField(KiCadPropertyNames.Description, string.Empty, new KiCadPosition(0, 0), KiCadLayerNames.FFab, hide: true);
        }

        /// <summary>Gets or sets the footprint's name, the first value of the form.</summary>
        public string Id
        {
            get => Node.GetValue(0) ?? "Unknown";
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets or sets the format the footprint is written in, its own <c>(version …)</c>, or
        /// <see langword="null"/> when it has none. Setting <see langword="null"/> removes it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// KiCad reads a footprint by the format this names, so a stamp that does not match the
        /// content changes what KiCad reads. With none, a <c>.kicad_mod</c> is read as format 0,
        /// KiCad 5's, which refuses a modern arc. A footprint read from a file keeps what it has, and
        /// a footprint built with <see cref="KiCadFootprint(string)"/> starts with
        /// <see cref="KiCadDefaults.FootprintVersion"/>, KiCad 10.0.6's. A new version goes first in
        /// the form, before anything it changes the meaning of.
        /// </para>
        /// <para>
        /// <b>On a board.</b> KiCad writes a footprint inside a board without a version, so the board's
        /// applies (<c>CTL_FOR_BOARD</c> carries <c>CTL_OMIT_FOOTPRINT_VERSION</c>,
        /// <c>pcb_io_kicad_sexpr.h</c> line 223). A footprint that does carry one there makes KiCad
        /// 10.0.6 read the rest of the board under the greater of the two
        /// (<c>pcb_io_kicad_sexpr_parser.cpp</c>, line 5046), and some board content reads differently
        /// by version. MEASURED against kicad-cli 10.0.6: a via after a footprint stamped
        /// <c>20260206</c> on a <c>new KiCadBoard()</c>, stamped <c>20241229</c>, lost its explicit
        /// "no" covering, plugging, capping and filling (line 7422). So placing a footprint on a board,
        /// through <see cref="KiCadBoard.Footprints"/> or <see cref="KiCadFootprintLibrary.AddFootprint"/>,
        /// removes its version, generator and generator_version, as pcbnew does, and this then reads
        /// <see langword="null"/> (#73). The footprint is read under the board's version, and nothing
        /// converts its content to that version: a footprint newer than its board can read
        /// differently there, or be refused. A footprint saved as a <c>.kicad_mod</c> keeps its
        /// version.
        /// </para>
        /// </remarks>
        public string? Version
        {
            get => ReadChild(KiCadTokens.Common.Version);
            set => WriteChild(KiCadTokens.Common.Version, value, SQuoteStyle.Bare);
        }

        /// <summary>
        /// Strips the header a footprint carries as a file of its own — <c>version</c>,
        /// <c>generator</c> and <c>generator_version</c> — as it is placed on a board. Every path
        /// that puts a footprint node inside a <c>kicad_pcb</c> calls this:
        /// <see cref="KiCadBoard.Footprints"/> and <see cref="KiCadFootprintLibrary.AddFootprint"/>.
        /// </summary>
        /// <param name="footprint">The footprint node, already a child of the board.</param>
        /// <remarks>
        /// This is what pcbnew writes: <c>format( const FOOTPRINT* )</c> puts the three out only
        /// without <c>CTL_OMIT_FOOTPRINT_VERSION</c> (<c>pcb_io_kicad_sexpr.cpp</c>, line 1210),
        /// which <c>CTL_FOR_BOARD</c> carries. A footprint with none, which is every footprint on a
        /// board pcbnew wrote, is left as it is, bytes included. See <see cref="Version"/> for what a
        /// version inside a board does to the rest of it.
        /// </remarks>
        internal static void PlaceOnBoard(SExpression footprint)
        {
            footprint.RemoveChild(KiCadTokens.Common.Version);
            footprint.RemoveChild(KiCadTokens.Common.Generator);
            footprint.RemoveChild(KiCadTokens.Common.GeneratorVersion);
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
            set => value.Write(this, KiCadTokens.Common.At, includeRotation: false);
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

        /// <summary>Gets the footprint's Bézier curves.</summary>
        public KiCadNodeList<KiCadFpCurve> Curves => new(Node, KiCadTokens.Footprint.FpCurve, n => new KiCadFpCurve(n));

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
                Width = width,
                Layer = layer,
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
                Width = width,
                Layer = layer,
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

        /// <summary>Appends a field the way KiCad 10.0.6 writes one on a footprint.</summary>
        private void AddField(string key, string value, KiCadPosition position, string layer, bool hide)
        {
            var field = new KiCadProperty(key, value) { Position = position };
            field.Node.CreateChild(KiCadTokens.Common.Layer).AddValue(layer, SQuoteStyle.Quoted);
            if (hide)
            {
                field.Node.CreateChild(KiCadTokens.Common.Hide, KiCadTokens.Common.Yes);
            }

            Node.AddChild(field.Node);
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
        /// <remarks>
        /// Setting it writes into whichever of the two the element has. An element that has neither,
        /// such as one built here, gets the form KiCad 10.0.6 writes,
        /// <c>(stroke (width w) (type solid))</c> (<c>common/stroke_params.cpp</c>, line 362), rather
        /// than the bare <c>(width w)</c> of older files (#75).
        /// </remarks>
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

                if (Node.GetChild(KiCadTokens.Common.Width) is not null)
                {
                    WriteChildDouble(KiCadTokens.Common.Width, value);
                    return;
                }

                var created = RequireStroke();
                created.Width = value;
                created.Type = KiCadTokens.Common.Solid;
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
            set => value.Write(this, KiCadTokens.Common.Start, includeRotation: false);
        }

        /// <summary>Gets or sets the end point.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.End));
            set => value.Write(this, KiCadTokens.Common.End, includeRotation: false);
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
            set => value.Write(this, KiCadTokens.Common.Start, includeRotation: false);
        }

        /// <summary>Gets or sets the opposite corner.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.End));
            set => value.Write(this, KiCadTokens.Common.End, includeRotation: false);
        }

        /// <summary>
        /// Gets or sets the corner radius in millimetres: KiCad 10's <c>(radius r)</c>, 0 when the
        /// rectangle has none. KiCad writes the form only for a radius above 0, and clamps one it
        /// reads to half the rectangle's shorter side.
        /// </summary>
        public double CornerRadius
        {
            get => ReadChildDouble(KiCadTokens.Common.Radius);
            set => WriteChildDouble(KiCadTokens.Common.Radius, value);
        }

        /// <summary>Gets the fill, or <see langword="null"/> when the rectangle has no <c>(fill ...)</c>.</summary>
        public KiCadFill? Fill => Node.GetChild(KiCadTokens.Common.Fill) is { } node ? new KiCadFill(node, KiCadFillSpelling.Board) : null;

        /// <summary>
        /// Gets the <c>(fill ...)</c> form, adding an empty one when the node has none. Its
        /// <see cref="KiCadFill.Type"/> is written the board's way, <c>(fill no)</c>, which is how
        /// KiCad spells it in a footprint too; see <see cref="KiCadFillSpelling.Board"/>.
        /// </summary>
        /// <returns>The view.</returns>
        public KiCadFill RequireFill() => new(Require(KiCadTokens.Common.Fill), KiCadFillSpelling.Board);
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
            set => value.Write(this, KiCadTokens.Common.Center, includeRotation: false);
        }

        /// <summary>Gets or sets a point on the circumference.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.End));
            set => value.Write(this, KiCadTokens.Common.End, includeRotation: false);
        }

        /// <summary>Gets the fill, or <see langword="null"/> when the circle has no <c>(fill ...)</c>.</summary>
        public KiCadFill? Fill => Node.GetChild(KiCadTokens.Common.Fill) is { } node ? new KiCadFill(node, KiCadFillSpelling.Board) : null;

        /// <summary>
        /// Gets the <c>(fill ...)</c> form, adding an empty one when the node has none. Its
        /// <see cref="KiCadFill.Type"/> is written the board's way, <c>(fill no)</c>, which is how
        /// KiCad spells it in a footprint too; see <see cref="KiCadFillSpelling.Board"/>.
        /// </summary>
        /// <returns>The view.</returns>
        public KiCadFill RequireFill() => new(Require(KiCadTokens.Common.Fill), KiCadFillSpelling.Board);
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
            set => value.Write(this, KiCadTokens.Common.Start, includeRotation: false);
        }

        /// <summary>Gets or sets the mid point the arc passes through, KiCad 6 and later.</summary>
        public KiCadPosition Mid
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.Mid));
            set => value.Write(this, KiCadTokens.Common.Mid, includeRotation: false);
        }

        /// <summary>Gets or sets the end point.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild(KiCadTokens.Common.End));
            set => value.Write(this, KiCadTokens.Common.End, includeRotation: false);
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
        /// <exception cref="ArgumentOutOfRangeException">A coordinate is not a number KiCad reads (#101).</exception>
        public void AddPoint(double x, double y)
        {
            var xText = Numbers.Format(x);
            var yText = Numbers.Format(y);
            Require(KiCadTokens.Common.Pts).CreateChild(KiCadTokens.Common.Xy, xText, yText);
        }

        /// <summary>Gets the fill, or <see langword="null"/> when the polygon has no <c>(fill ...)</c>.</summary>
        public KiCadFill? Fill => Node.GetChild(KiCadTokens.Common.Fill) is { } node ? new KiCadFill(node, KiCadFillSpelling.Board) : null;

        /// <summary>
        /// Gets the <c>(fill ...)</c> form, adding an empty one when the node has none. Its
        /// <see cref="KiCadFill.Type"/> is written the board's way, <c>(fill no)</c>, which is how
        /// KiCad spells it in a footprint too; see <see cref="KiCadFillSpelling.Board"/>.
        /// </summary>
        /// <returns>The view.</returns>
        public KiCadFill RequireFill() => new(Require(KiCadTokens.Common.Fill), KiCadFillSpelling.Board);
    }

    /// <summary>A Bézier curve: <c>(fp_curve (pts (xy ...) (xy ...) (xy ...) (xy ...)) (stroke ...) (layer "..."))</c>.</summary>
    /// <remarks>The four points are a cubic Bézier: start, two control points, end — not a polyline.</remarks>
    public class KiCadFpCurve : KiCadFpItem
    {
        /// <summary>Creates a view over an <c>(fp_curve ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadFpCurve(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty curve.</summary>
        public KiCadFpCurve()
            : base(new SExpression(KiCadTokens.Footprint.FpCurve))
        {
        }

        /// <summary>Gets the four control points, in order.</summary>
        public IReadOnlyList<KiCadPosition> Points =>
            (Node.GetChild(KiCadTokens.Common.Pts)?.GetChildren(KiCadTokens.Common.Xy) ?? Enumerable.Empty<SExpression>())
                .Select(xy => new KiCadPosition(xy.GetValueAsDouble(0), xy.GetValueAsDouble(1)))
                .ToArray();

        /// <summary>Appends a control point.</summary>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        public void AddPoint(double x, double y)
        {
            Require(KiCadTokens.Common.Pts).CreateChild(KiCadTokens.Common.Xy, Numbers.Format(x), Numbers.Format(y));
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
            set => value.Write(this, KiCadTokens.Common.At, includeRotation: true);
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
            set => value.Write(this, KiCadTokens.Common.At, includeRotation: false);
        }

        /// <summary>Gets or sets the pad size.</summary>
        public KiCadSize Size
        {
            get => KiCadSize.Read(Node.GetChild(KiCadTokens.Common.Size), 1);
            set => value.Write(this, KiCadTokens.Common.Size);
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
            set => value.Write(this, KiCadTokens.Footprint.Offset);
        }

        /// <summary>Gets or sets the model scale.</summary>
        public KiCadXyz Scale
        {
            get => KiCadXyz.Read(Node.GetChild(KiCadTokens.Common.Scale), 1);
            set => value.Write(this, KiCadTokens.Common.Scale);
        }

        /// <summary>Gets or sets the model rotation, in degrees.</summary>
        public KiCadXyz Rotation
        {
            get => KiCadXyz.Read(Node.GetChild(KiCadTokens.Footprint.Rotate), 0);
            set => value.Write(this, KiCadTokens.Footprint.Rotate);
        }
    }
}
