using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SExpressions;

namespace KiCadSharp.Documents
{
    /// <summary>
    /// A KiCad symbol library (<c>.kicad_sym</c>), as a view over the file's s-expression tree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Loading parses the file and keeps it. Saving writes the same document back, so a library this
    /// library has only read comes out byte for byte, and one where a single property changed comes
    /// out with exactly that property's bytes different. Nothing is re-serialised from a model,
    /// which is why tokens KiCad added after this code was written survive the round trip.
    /// </para>
    /// </remarks>
    public class KiCadSymbolLibrary
    {
        private readonly SDocument _document;
        private readonly SExpression _root;

        /// <summary>
        /// Creates a new, empty library.
        /// </summary>
        /// <param name="generator">The value of the <c>generator</c> token.</param>
        /// <param name="version">The value of the <c>version</c> token.</param>
        public KiCadSymbolLibrary(string generator = "KiCad Library Importer", string version = "20211014")
        {
            _root = new SExpression("kicad_symbol_lib");
            _root.CreateChild("version", version);
            _root.CreateChild("generator").AddValue(generator, SQuoteStyle.Quoted);
            _document = new SDocument();
            _document.Add(_root);
        }

        /// <summary>
        /// Creates a library over an existing s-expression.
        /// </summary>
        /// <param name="expression">The <c>kicad_symbol_lib</c> form.</param>
        public KiCadSymbolLibrary(SExpression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            _root = expression;
            _document = new SDocument();
            _document.Add(expression);
        }

        private KiCadSymbolLibrary(SDocument document)
        {
            _document = document;
            _root = document.Root ?? throw new InvalidOperationException("The file holds no s-expression.");
        }

        /// <summary>Gets the whole parsed file, including anything outside the root form.</summary>
        public SDocument Document => _document;

        /// <summary>Gets the root <c>kicad_symbol_lib</c> form.</summary>
        public SExpression Node => _root;

        /// <summary>Gets or sets the library format version.</summary>
        public string Version
        {
            get => _root.GetChildValue("version") ?? "20211014";
            set => _root.SetChildValue("version", value, SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets the name of the program that wrote the library.</summary>
        public string Generator
        {
            get => _root.GetChildValue("generator") ?? "KiCad Library Importer";
            set => _root.SetChildValue("generator", value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets the symbols in the library, as a live view over the root form's <c>symbol</c> children.
        /// </summary>
        public KiCadNodeList<KiCadSymbol> Symbols => new(_root, "symbol", n => new KiCadSymbol(n));

        /// <summary>Loads a library from a file.</summary>
        /// <param name="filePath">Path to the <c>.kicad_sym</c> file.</param>
        /// <returns>The library.</returns>
        public static KiCadSymbolLibrary Load(string filePath) => new(SDocument.Load(filePath));

        /// <summary>Loads a library from a file, reading it asynchronously.</summary>
        /// <param name="filePath">Path to the <c>.kicad_sym</c> file.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The library.</returns>
        public static async Task<KiCadSymbolLibrary> LoadAsync(string filePath, CancellationToken cancellationToken = default) =>
            new(await SDocument.LoadAsync(filePath, cancellationToken).ConfigureAwait(false));

        /// <summary>Parses a library from text.</summary>
        /// <param name="text">The file contents.</param>
        /// <returns>The library.</returns>
        public static KiCadSymbolLibrary Parse(string text) => new(SDocument.Parse(text));

        /// <summary>Appends an existing symbol to the library.</summary>
        /// <param name="symbol">The symbol to append.</param>
        /// <returns>The symbol, now a child of this library.</returns>
        public KiCadSymbol AddSymbol(KiCadSymbol symbol)
        {
            ArgumentNullException.ThrowIfNull(symbol);
            _root.AddChild(symbol.Node);
            return symbol;
        }

        /// <summary>Creates and appends a new symbol.</summary>
        /// <param name="id">The symbol's name.</param>
        /// <returns>The new symbol.</returns>
        public KiCadSymbol AddSymbol(string id) => AddSymbol(new KiCadSymbol(id));

        /// <summary>Removes the first symbol with the given name.</summary>
        /// <param name="id">The symbol's name.</param>
        /// <returns>True when a symbol was removed.</returns>
        public bool RemoveSymbol(string id)
        {
            var symbol = GetSymbol(id);
            return symbol is not null && _root.Children.Remove(symbol.Node);
        }

        /// <summary>Gets the first symbol with the given name.</summary>
        /// <param name="id">The symbol's name.</param>
        /// <returns>The symbol, or <see langword="null"/>.</returns>
        public KiCadSymbol? GetSymbol(string id) => Symbols.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

        /// <summary>
        /// Writes the library back out. An untouched library that was loaded from a file is written
        /// byte for byte; a changed one differs only where it was changed.
        /// </summary>
        /// <param name="filePath">Destination path.</param>
        public void Save(string filePath) => _document.Save(filePath);

        /// <summary>Writes the library back out asynchronously.</summary>
        /// <param name="filePath">Destination path.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the file is written.</returns>
        public Task SaveAsync(string filePath, CancellationToken cancellationToken = default) =>
            _document.SaveAsync(filePath, cancellationToken);

        /// <summary>Renders the library to text.</summary>
        /// <returns>The file contents.</returns>
        public string ToText() => _document.ToText();
    }

    /// <summary>
    /// One symbol in a library, as a view over its <c>(symbol "Name" ...)</c> form.
    /// </summary>
    /// <remarks>
    /// KiCad 6 and later split a symbol's drawing into sub-units — nested <c>(symbol "Name_1_1" ...)</c>
    /// forms holding the pins and graphics — and it is entirely normal for the outer form to have no
    /// pins of its own. <see cref="Pins"/> and <see cref="GraphicalItems"/> therefore look through
    /// the sub-units as well; <see cref="Units"/> exposes them directly.
    /// </remarks>
    public class KiCadSymbol : KiCadNode
    {
        /// <summary>Creates a view over an existing <c>(symbol ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadSymbol(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates a new symbol with the four properties KiCad requires.</summary>
        /// <param name="id">The symbol's name.</param>
        public KiCadSymbol(string id)
            : base(new SExpression("symbol"))
        {
            ArgumentNullException.ThrowIfNull(id);
            Node.AddValue(id, SQuoteStyle.Quoted);
            AddProperty("Reference", "U");
            AddProperty("Value", id);
            AddProperty("Footprint", string.Empty);
            AddProperty("Datasheet", string.Empty);
        }

        /// <summary>Gets or sets the symbol's name, the first value of the form.</summary>
        public string Id
        {
            get => Node.GetValue(0) ?? "Unknown";
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets the symbol's properties, as a live view.</summary>
        public KiCadNodeList<KiCadProperty> Properties => new(Node, "property", n => new KiCadProperty(n));

        /// <summary>
        /// Gets the sub-units — the nested <c>(symbol "Name_u_b" ...)</c> forms that hold the drawing.
        /// </summary>
        public KiCadNodeList<KiCadSymbolUnit> Units => new(Node, "symbol", n => new KiCadSymbolUnit(n));

        /// <summary>
        /// Gets every pin of the symbol: those declared on the form itself and those inside its
        /// sub-units, in document order.
        /// </summary>
        public IReadOnlyList<KiCadPin> Pins => Collect(n => n.GetChildren("pin").Select(p => new KiCadPin(p)));

        /// <summary>
        /// Gets every graphical item of the symbol, from the form itself and from its sub-units.
        /// </summary>
        public IReadOnlyList<KiCadGraphicalItem> GraphicalItems => Collect(KiCadGraphicalItem.In);

        /// <summary>Gets or sets whether pin numbers are hidden.</summary>
        public bool HidePinNumbers
        {
            get => IsHidden("pin_numbers");
            set => SetHidden("pin_numbers", value);
        }

        /// <summary>Gets or sets whether pin names are hidden.</summary>
        public bool HidePinNames
        {
            get => IsHidden("pin_names");
            set => SetHidden("pin_names", value);
        }

        /// <summary>Gets or sets whether the symbol appears in the bill of materials.</summary>
        public bool InBom
        {
            get => Node.GetChild("in_bom") is null || ReadFlag("in_bom");
            set => WriteFlag("in_bom", value);
        }

        /// <summary>Gets or sets whether the symbol is transferred to the board.</summary>
        public bool OnBoard
        {
            get => Node.GetChild("on_board") is null || ReadFlag("on_board");
            set => WriteFlag("on_board", value);
        }

        /// <summary>Gets the value of a named property.</summary>
        /// <param name="key">The property key, e.g. <c>Reference</c>.</param>
        /// <returns>The value, or <see langword="null"/> when the property is absent.</returns>
        public string? GetPropertyValue(string key) =>
            Properties.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal))?.Value;

        /// <summary>Appends a property, or updates it when one with the same key already exists.</summary>
        /// <param name="key">The property key.</param>
        /// <param name="value">The property value.</param>
        /// <returns>The property.</returns>
        public KiCadProperty AddProperty(string key, string value)
        {
            var existing = Properties.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));
            if (existing is not null)
            {
                existing.Value = value;
                return existing;
            }

            var property = new KiCadProperty(key, value);
            Node.AddChild(property.Node);
            return property;
        }

        /// <summary>Appends a pin to the symbol form itself.</summary>
        /// <param name="pin">The pin.</param>
        /// <returns>The pin.</returns>
        /// <remarks>
        /// KiCad puts pins in a sub-unit, not on the outer form. Use <c>Units[i].AddPin(pin)</c> to
        /// match what the editor writes; this overload is for symbols built entirely in memory.
        /// </remarks>
        public KiCadPin AddPin(KiCadPin pin)
        {
            ArgumentNullException.ThrowIfNull(pin);
            Node.AddChild(pin.Node);
            return pin;
        }

        /// <summary>Appends a graphical item to the symbol form itself.</summary>
        /// <param name="item">The item.</param>
        /// <returns>The item.</returns>
        public KiCadGraphicalItem AddGraphicalItem(KiCadGraphicalItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            Node.AddChild(item.Node);
            return item;
        }

        /// <summary>Creates and appends a sub-unit.</summary>
        /// <param name="name">The sub-unit name; KiCad's convention is <c>&lt;symbol&gt;_&lt;unit&gt;_&lt;style&gt;</c>.</param>
        /// <returns>The new sub-unit.</returns>
        public KiCadSymbolUnit AddUnit(string name)
        {
            var unit = new SExpression("symbol");
            unit.AddValue(name, SQuoteStyle.Quoted);
            Node.AddChild(unit);
            return new KiCadSymbolUnit(unit);
        }

        /// <summary>
        /// Deep-copies the symbol under a new name, leaving the original untouched.
        /// </summary>
        /// <param name="newId">The copy's name.</param>
        /// <returns>The copy, with no parent.</returns>
        /// <remarks>
        /// The copy keeps its link to the source text, so writing it out unchanged still reproduces
        /// the original bytes. Sub-unit names carrying the old symbol name are renamed with it.
        /// </remarks>
        public KiCadSymbol CloneAs(string newId)
        {
            ArgumentNullException.ThrowIfNull(newId);
            var oldId = Id;
            var copy = new KiCadSymbol(Node.Clone()) { Id = newId };

            foreach (var unit in copy.Units)
            {
                if (unit.Id.StartsWith(oldId + "_", StringComparison.Ordinal))
                {
                    unit.Id = newId + unit.Id[oldId.Length..];
                }
            }

            var value = copy.Properties.FirstOrDefault(p => string.Equals(p.Key, "Value", StringComparison.Ordinal));
            if (value is not null)
            {
                value.Value = newId;
            }

            return copy;
        }

        private IReadOnlyList<T> Collect<T>(Func<SExpression, IEnumerable<T>> select)
        {
            var all = new List<T>();
            all.AddRange(select(Node));
            foreach (var item in Node.GetChildren("symbol"))
            {
                all.AddRange(select(item));
            }

            return all;
        }

        private bool IsHidden(string token)
        {
            var group = Node.GetChild(token);
            if (group is null)
            {
                return false;
            }

            var hide = group.GetChild("hide");
            return hide is not null && (hide.GetValue(0) is null || hide.GetValueAsBool());
        }

        private void SetHidden(string token, bool value)
        {
            var group = Node.GetChild(token) ?? Node.CreateChild(token);
            group.SetChildValue("hide", value ? "yes" : "no", SQuoteStyle.Bare);
        }
    }

    /// <summary>
    /// A sub-unit of a symbol: the nested <c>(symbol "Name_1_1" ...)</c> that carries the pins and
    /// the drawing for one unit and body style.
    /// </summary>
    public class KiCadSymbolUnit : KiCadNode
    {
        /// <summary>Creates a view over a sub-unit form.</summary>
        /// <param name="node">The form.</param>
        public KiCadSymbolUnit(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets or sets the sub-unit's name.</summary>
        public string Id
        {
            get => Node.GetValue(0) ?? string.Empty;
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets the unit number encoded in the name (<c>R_1_1</c> is unit 1), or 0 when the name does
        /// not follow KiCad's convention.
        /// </summary>
        public int Unit => NumberAt(1);

        /// <summary>
        /// Gets the body style encoded in the name (<c>R_1_2</c> is body style 2 — "De Morgan"), or 0.
        /// </summary>
        public int BodyStyle => NumberAt(0);

        /// <summary>Gets the pins of this sub-unit, as a live view.</summary>
        public KiCadNodeList<KiCadPin> Pins => new(Node, "pin", n => new KiCadPin(n));

        /// <summary>Gets the graphical items of this sub-unit.</summary>
        public IReadOnlyList<KiCadGraphicalItem> GraphicalItems => KiCadGraphicalItem.In(Node).ToArray();

        /// <summary>Appends a pin.</summary>
        /// <param name="pin">The pin.</param>
        /// <returns>The pin.</returns>
        public KiCadPin AddPin(KiCadPin pin)
        {
            ArgumentNullException.ThrowIfNull(pin);
            Node.AddChild(pin.Node);
            return pin;
        }

        private int NumberAt(int fromEnd)
        {
            var parts = Id.Split('_');
            var index = parts.Length - 1 - fromEnd;
            return index > 0 && int.TryParse(parts[index], out var value) ? value : 0;
        }
    }

    /// <summary>
    /// A property: <c>(property "Key" "Value" (at ...) (effects ...))</c>.
    /// </summary>
    public class KiCadProperty : KiCadNode
    {
        /// <summary>Creates a view over a <c>(property ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadProperty(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates a new property.</summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        public KiCadProperty(string key, string value)
            : base(new SExpression("property"))
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);
            Node.AddValue(key, SQuoteStyle.Quoted);
            Node.AddValue(value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the property key.</summary>
        public string Key
        {
            get => Node.GetValue(0) ?? "Unknown";
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the property value.</summary>
        public string Value
        {
            get => Node.GetValue(1) ?? string.Empty;
            set => WriteValue(1, value, SQuoteStyle.Quoted);
        }

        /// <summary>
        /// Gets or sets the property's ordinal, from the <c>(id n)</c> child KiCad 6 wrote. KiCad 7
        /// dropped it; on a file that has no <c>id</c> this reads 0 and setting it adds one back.
        /// </summary>
        public int Id
        {
            get => Node.GetChild("id") is { } id && id.TryGetValue<int>(0, out var value) ? value : 0;
            set => Node.SetChildValue("id", value.ToString(System.Globalization.CultureInfo.InvariantCulture), SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets where the property text sits.</summary>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild("at"));
            set => value.Write(Require("at"), includeRotation: true);
        }

        /// <summary>Gets the text rendering of the property, creating an <c>(effects ...)</c> if needed.</summary>
        public KiCadFontEffects FontEffects => new(Require("effects"));
    }

    /// <summary>
    /// A pin: <c>(pin passive line (at x y r) (length l) (name "N" ...) (number "1" ...))</c>.
    /// </summary>
    /// <remarks>
    /// The electrical type and the graphic style are bare values on the form, not children, which is
    /// why they are read positionally.
    /// </remarks>
    public class KiCadPin : KiCadNode
    {
        /// <summary>Creates a view over a <c>(pin ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadPin(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates a new pin.</summary>
        /// <param name="type">Electrical type, e.g. <c>passive</c>, <c>power_in</c>.</param>
        /// <param name="style">Graphic style, e.g. <c>line</c>, <c>inverted</c>.</param>
        /// <param name="position">Where the pin's connection point sits.</param>
        /// <param name="length">Pin length in millimetres.</param>
        /// <param name="name">Pin name.</param>
        /// <param name="number">Pin number.</param>
        public KiCadPin(string type, string style, KiCadPosition position, double length, string name, string number)
            : base(new SExpression("pin"))
        {
            Node.AddValue(type, SQuoteStyle.Bare);
            Node.AddValue(style, SQuoteStyle.Bare);
            Position = position;
            Length = length;
            Name = name;
            Number = number;
        }

        /// <summary>Gets or sets the electrical type.</summary>
        public string Type
        {
            get => Node.GetValue(0) ?? "passive";
            set => WriteValue(0, value, SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets the graphic style.</summary>
        public string Style
        {
            get => Node.GetValue(1) ?? "line";
            set => WriteValue(1, value, SQuoteStyle.Bare);
        }

        /// <summary>Gets or sets the pin's connection point.</summary>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild("at"));
            set => value.Write(Require("at"), includeRotation: true);
        }

        /// <summary>Gets or sets the pin length in millimetres.</summary>
        public double Length
        {
            get => ReadChildDouble("length", 0, 2.54);
            set => WriteChildDouble("length", value);
        }

        /// <summary>Gets or sets the pin name.</summary>
        public string Name
        {
            get => ReadChild("name") ?? string.Empty;
            set => WriteChild("name", value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets the pin number.</summary>
        public string Number
        {
            get => ReadChild("number") ?? string.Empty;
            set => WriteChild("number", value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets whether the pin is hidden.</summary>
        public bool Hide
        {
            get => ReadFlag("hide");
            set => WriteFlag("hide", value);
        }
    }

    /// <summary>
    /// A drawing element of a symbol: a polyline, rectangle, circle, arc, Bézier or text.
    /// </summary>
    /// <remarks>
    /// The concrete type is chosen by the form's token. A token this library does not recognise
    /// still appears here, as a <see cref="KiCadGraphicalItem"/> with no more specific type — it is
    /// not skipped, because skipping is how a shape gets lost.
    /// </remarks>
    public class KiCadGraphicalItem : KiCadNode
    {
        /// <summary>The tokens that draw something inside a symbol.</summary>
        private static readonly HashSet<string> Tokens = new(StringComparer.Ordinal)
        {
            "polyline", "rectangle", "circle", "arc", "bezier", "text", "text_box",
        };

        /// <summary>Creates a view over a drawing form.</summary>
        /// <param name="node">The form.</param>
        public KiCadGraphicalItem(SExpression node)
            : base(node)
        {
        }

        /// <summary>Gets the stroke, creating a <c>(stroke ...)</c> child if there is none.</summary>
        public KiCadStroke Stroke => new(Require("stroke"));

        /// <summary>Gets the fill, creating a <c>(fill ...)</c> child if there is none.</summary>
        public KiCadFill Fill => new(Require("fill"));

        /// <summary>Enumerates the drawing elements directly inside <paramref name="owner"/>.</summary>
        /// <param name="owner">The form to look in.</param>
        /// <returns>One view per drawing child, in document order.</returns>
        internal static IEnumerable<KiCadGraphicalItem> In(SExpression owner)
        {
            foreach (var child in owner.Children)
            {
                if (Tokens.Contains(child.Token))
                {
                    yield return Wrap(child);
                }
            }
        }

        /// <summary>Wraps a drawing form in the most specific view available for its token.</summary>
        /// <param name="node">The form.</param>
        /// <returns>The view.</returns>
        public static KiCadGraphicalItem Wrap(SExpression node)
        {
            ArgumentNullException.ThrowIfNull(node);
            return node.Token switch
            {
                "polyline" => new KiCadPolyline(node),
                "rectangle" => new KiCadRectangle(node),
                "circle" => new KiCadCircle(node),
                "arc" => new KiCadArc(node),
                "text" => new KiCadText(node),
                _ => new KiCadGraphicalItem(node),
            };
        }
    }

    /// <summary>A polyline: <c>(polyline (pts (xy x y) ...) (stroke ...) (fill ...))</c>.</summary>
    public class KiCadPolyline : KiCadGraphicalItem
    {
        /// <summary>Creates a view over a <c>(polyline ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadPolyline(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an empty polyline.</summary>
        public KiCadPolyline()
            : base(new SExpression("polyline"))
        {
        }

        /// <summary>Gets the vertices, in order.</summary>
        public IReadOnlyList<KiCadPosition> Points =>
            (Node.GetChild("pts")?.GetChildren("xy") ?? Enumerable.Empty<SExpression>())
                .Select(xy => new KiCadPosition(xy.GetValueAsDouble(0), xy.GetValueAsDouble(1)))
                .ToArray();

        /// <summary>Appends a vertex.</summary>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        public void AddPoint(double x, double y)
        {
            var points = Node.GetChild("pts") ?? Node.CreateChild("pts");
            points.CreateChild("xy", Numbers.Format(x), Numbers.Format(y));
        }
    }

    /// <summary>A rectangle: <c>(rectangle (start x y) (end x y) (stroke ...) (fill ...))</c>.</summary>
    public class KiCadRectangle : KiCadGraphicalItem
    {
        /// <summary>Creates a view over a <c>(rectangle ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadRectangle(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates a rectangle from two corners.</summary>
        /// <param name="startX">First corner X.</param>
        /// <param name="startY">First corner Y.</param>
        /// <param name="endX">Opposite corner X.</param>
        /// <param name="endY">Opposite corner Y.</param>
        public KiCadRectangle(double startX, double startY, double endX, double endY)
            : base(new SExpression("rectangle"))
        {
            Start = new KiCadPosition(startX, startY);
            End = new KiCadPosition(endX, endY);
        }

        /// <summary>Gets or sets the first corner.</summary>
        public KiCadPosition Start
        {
            get => KiCadPosition.Read(Node.GetChild("start"));
            set => value.Write(Require("start"), includeRotation: false);
        }

        /// <summary>Gets or sets the opposite corner.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild("end"));
            set => value.Write(Require("end"), includeRotation: false);
        }
    }

    /// <summary>A circle: <c>(circle (center x y) (radius r) (stroke ...) (fill ...))</c>.</summary>
    public class KiCadCircle : KiCadGraphicalItem
    {
        /// <summary>Creates a view over a <c>(circle ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadCircle(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates a circle.</summary>
        /// <param name="centerX">Centre X.</param>
        /// <param name="centerY">Centre Y.</param>
        /// <param name="radius">Radius, millimetres.</param>
        public KiCadCircle(double centerX, double centerY, double radius)
            : base(new SExpression("circle"))
        {
            Center = new KiCadPosition(centerX, centerY);
            Radius = radius;
        }

        /// <summary>Gets or sets the centre.</summary>
        public KiCadPosition Center
        {
            get => KiCadPosition.Read(Node.GetChild("center"));
            set => value.Write(Require("center"), includeRotation: false);
        }

        /// <summary>Gets or sets the radius in millimetres.</summary>
        public double Radius
        {
            get => ReadChildDouble("radius");
            set => WriteChildDouble("radius", value);
        }
    }

    /// <summary>An arc: <c>(arc (start x y) (mid x y) (end x y) (stroke ...) (fill ...))</c>.</summary>
    public class KiCadArc : KiCadGraphicalItem
    {
        /// <summary>Creates a view over an <c>(arc ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadArc(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates an arc from three points.</summary>
        /// <param name="startX">Start X.</param>
        /// <param name="startY">Start Y.</param>
        /// <param name="midX">Mid X.</param>
        /// <param name="midY">Mid Y.</param>
        /// <param name="endX">End X.</param>
        /// <param name="endY">End Y.</param>
        public KiCadArc(double startX, double startY, double midX, double midY, double endX, double endY)
            : base(new SExpression("arc"))
        {
            Start = new KiCadPosition(startX, startY);
            Mid = new KiCadPosition(midX, midY);
            End = new KiCadPosition(endX, endY);
        }

        /// <summary>Gets or sets the start point.</summary>
        public KiCadPosition Start
        {
            get => KiCadPosition.Read(Node.GetChild("start"));
            set => value.Write(Require("start"), includeRotation: false);
        }

        /// <summary>Gets or sets the mid point the arc passes through.</summary>
        public KiCadPosition Mid
        {
            get => KiCadPosition.Read(Node.GetChild("mid"));
            set => value.Write(Require("mid"), includeRotation: false);
        }

        /// <summary>Gets or sets the end point.</summary>
        public KiCadPosition End
        {
            get => KiCadPosition.Read(Node.GetChild("end"));
            set => value.Write(Require("end"), includeRotation: false);
        }
    }

    /// <summary>Free text inside a symbol: <c>(text "..." (at x y r) (effects ...))</c>.</summary>
    public class KiCadText : KiCadGraphicalItem
    {
        /// <summary>Creates a view over a <c>(text ...)</c> form.</summary>
        /// <param name="node">The form.</param>
        public KiCadText(SExpression node)
            : base(node)
        {
        }

        /// <summary>Creates a text item.</summary>
        /// <param name="text">The text.</param>
        /// <param name="x">X, millimetres.</param>
        /// <param name="y">Y, millimetres.</param>
        /// <param name="rotation">Rotation, degrees.</param>
        public KiCadText(string text, double x, double y, double rotation = 0)
            : base(new SExpression("text"))
        {
            Node.AddValue(text, SQuoteStyle.Quoted);
            Position = new KiCadPosition(x, y, rotation);
        }

        /// <summary>Gets or sets the text.</summary>
        public string Text
        {
            get => Node.GetValue(0) ?? string.Empty;
            set => WriteValue(0, value, SQuoteStyle.Quoted);
        }

        /// <summary>Gets or sets where the text sits.</summary>
        public KiCadPosition Position
        {
            get => KiCadPosition.Read(Node.GetChild("at"));
            set => value.Write(Require("at"), includeRotation: true);
        }

        /// <summary>Gets the text rendering, creating an <c>(effects ...)</c> if there is none.</summary>
        public KiCadFontEffects FontEffects => new(Require("effects"));
    }
}
