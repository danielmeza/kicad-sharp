using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Schematics
{
    /// <summary>
    /// The sheet's drawing-frame text:
    /// <c>(title_block (title "..") (date "..") (rev "..") (company "..") (comment N ".."))</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every field is optional and KiCad writes only the ones that are set — a fresh sheet with a
    /// title and nothing else really does hold one child. So the four named fields read
    /// <see langword="null"/> rather than an empty string when they are absent, and setting one to
    /// <see langword="null"/> removes the line instead of writing an empty one.
    /// </para>
    /// <para>
    /// The comments are numbered, not ordered: KiCad's dialog offers nine slots and writes only the
    /// filled ones, so the file may hold comment 1 and comment 4 and nothing between them. That is
    /// why they are addressed by number rather than by index.
    /// </para>
    /// </remarks>
    public sealed class KiCadTitleBlock : KiCadNode
    {
        /// <summary>Creates a view over a <c>(title_block ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadTitleBlock(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the sheet title, or <see langword="null"/> when there is none.</summary>
        public string? Title
        {
            get => ReadChild("title");
            set => WriteChild("title", value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the date, as written — KiCad stores whatever the dialog was given.</summary>
        public string? Date
        {
            get => ReadChild("date");
            set => WriteChild("date", value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the revision. The file spells it <c>rev</c>.</summary>
        public string? Revision
        {
            get => ReadChild("rev");
            set => WriteChild("rev", value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the company name.</summary>
        public string? Company
        {
            get => ReadChild("company");
            set => WriteChild("company", value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets the numbers of the comment lines the file actually carries, in document order.</summary>
        public IReadOnlyList<int> CommentNumbers =>
            Node.GetChildren("comment")
                .Select(c => c.TryGetValue<int>(0, out var number) ? number : 0)
                .ToArray();

        /// <summary>Reads one numbered comment line.</summary>
        /// <param name="number">The slot number, 1-9 in KiCad's dialog.</param>
        /// <returns>The text, or <see langword="null"/> when that slot is empty.</returns>
        public string? GetComment(int number) => FindComment(number)?.GetValue(1);

        /// <summary>Writes one numbered comment line, adding the slot if the file has none.</summary>
        /// <param name="number">The slot number.</param>
        /// <param name="text">The text, or <see langword="null"/> to drop the line entirely.</param>
        public void SetComment(int number, string? text)
        {
            var comment = FindComment(number);
            if (text is null)
            {
                if (comment is not null)
                {
                    Node.Children.Remove(comment);
                }

                return;
            }

            if (comment is null)
            {
                comment = Node.CreateChild("comment");
                comment.AddValue(number.ToString(CultureInfo.InvariantCulture), SQuoteStyle.Bare);
                comment.AddValue(text, SQuoteStyle.Quoted);
                return;
            }

            comment.SetValue(1, text, SQuoteStyle.Quoted);
        }

        private SExpression? FindComment(int number) =>
            Node.GetChildren("comment")
                .FirstOrDefault(c => c.TryGetValue<int>(0, out var value) && value == number);
    }

    /// <summary>
    /// One entry of the root sheet's page map: <c>(path "/" (page "1"))</c>.
    /// </summary>
    /// <remarks>
    /// The page number is a string because KiCad allows anything the printer should show — "1",
    /// "2a", "iv" — and it is per <em>instance</em>, so one child sheet drawn twice has two entries
    /// here with two different pages.
    /// </remarks>
    public sealed class KiCadSheetInstance : KiCadNode
    {
        /// <summary>Creates a view over a <c>(path ...)</c> form inside <c>(sheet_instances ...)</c>.</summary>
        /// <param name="node">The form.</param>
        public KiCadSheetInstance(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the hierarchical path this page number belongs to; <c>"/"</c> is the root sheet.</summary>
        public string Path
        {
            get => Node.GetValue(0) ?? string.Empty;
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the page number printed on that sheet.</summary>
        public string Page
        {
            get => ReadChild("page") ?? string.Empty;
            set => WriteChild("page", value, SQuoteStyle.Quoted);
        }
    }

    /// <summary>
    /// A named bundle of nets: <c>(bus_alias "I2C0" (members "I2C0_SCL" "I2C0_SDA"))</c>.
    /// </summary>
    /// <remarks>
    /// The alias is file-scoped and exists only so a bus label can be written <c>I2C0</c> instead of
    /// spelling out every member; nothing references it by UUID, which is why it has none.
    /// </remarks>
    public sealed class KiCadBusAlias : KiCadNode
    {
        /// <summary>Creates a view over a <c>(bus_alias ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadBusAlias(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the alias name, which is what a bus label spells.</summary>
        public string Name
        {
            get => Node.GetValue(0) ?? string.Empty;
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets the net names the alias stands for, in the order the file lists them.</summary>
        public IReadOnlyList<string> Members =>
            Node.GetChild("members") is { } members ? members.Values.ToArray() : Array.Empty<string>();

        /// <summary>Appends a net to the bundle, creating the <c>(members ...)</c> form if needed.</summary>
        /// <param name="net">The net name.</param>
        public void AddMember(string net)
        {
            ArgumentNullException.ThrowIfNull(net);
            (Node.GetChild("members") ?? Node.CreateChild("members")).AddValue(net, SQuoteStyle.Quoted);
        }
    }

    /// <summary>
    /// A connection point on a sub-sheet's box:
    /// <c>(pin "NAME" input (at x y r) (effects ...) (uuid ...))</c>.
    /// </summary>
    /// <remarks>
    /// This is the parent's half of a hierarchical connection; the child file carries a
    /// <see cref="KiCadHierarchicalLabel"/> with the same name. The name is what joins them — the
    /// electrical type is drawn, checked by the rules checker, and otherwise ignored.
    /// </remarks>
    public sealed class KiCadSheetPin : KiCadNode
    {
        /// <summary>Creates a view over a <c>(pin ...)</c> form inside a <c>(sheet ...)</c>.</summary>
        /// <param name="node">The form.</param>
        public KiCadSheetPin(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the pin's name, which must match a hierarchical label in the child sheet.</summary>
        public string Name
        {
            get => Node.GetValue(0) ?? string.Empty;
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets or sets the electrical type — <c>input</c>, <c>output</c>, <c>bidirectional</c>,
        /// <c>tri_state</c> or <c>passive</c>. It is a bare value on the form, not a child, so it is
        /// read positionally.
        /// </summary>
        public string Shape
        {
            get => Node.GetValue(1) ?? "passive";
            set => WriteValue(1, value, SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets where the pin sits on the sheet box and which way it points.</summary>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild("at"));
            set => value.Write(Require("at"), includeRotation: true);
        }

        /// <summary>Gets the text rendering, creating an <c>(effects ...)</c> if there is none.</summary>
        public KiCadFontEffects FontEffects => new(Require("effects"));

        /// <summary>Gets or sets the pin's UUID.</summary>
        public string Uuid
        {
            get => ReadChild("uuid") ?? string.Empty;
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }
    }
}
