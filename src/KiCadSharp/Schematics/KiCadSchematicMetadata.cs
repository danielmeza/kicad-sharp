using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp.Schematics
{

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

        /// <summary>
        /// Gets the text rendering, or <see langword="null"/> when the form carries no
        /// <c>(effects ...)</c>. Reading it never adds one; use <see cref="RequireFontEffects"/>.
        /// </summary>
        public KiCadFontEffects? FontEffects => Node.GetChild("effects") is { } node ? new KiCadFontEffects(node) : null;

        /// <summary>Gets the <c>(effects ...)</c> form, adding an empty one when the node has none.</summary>
        /// <returns>The view.</returns>
        public KiCadFontEffects RequireFontEffects() => new(Require("effects"));

        /// <summary>Gets or sets the pin's UUID.</summary>
        public string Uuid
        {
            get => ReadChild("uuid") ?? string.Empty;
            set => WriteChild("uuid", value, SQuoteStyle.Quoted);
        }
    }
}
