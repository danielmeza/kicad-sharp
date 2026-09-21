using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KiCadSharp.Specctra
{
    /// <summary>
    /// One form of a Specctra <c>.dsn</c> or <c>.ses</c> file: a head word and its items, each an
    /// atom or a nested form, in the order they were written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Specctra looks like the s-expressions KiCad writes and is not quite them, which is why this is
    /// not <c>SExpressions</c>. The file names its own quote character — <c>(string_quote ")</c>, a
    /// bare quote the KiCad grammar would read as an unterminated string — and a word such as
    /// <c>RoundRect[T]Pad_540.000000x640.000000_um</c> or <c>Net-(U1-CH4_OUT)</c> is one token only
    /// because the writer quoted it.
    /// </para>
    /// <para>
    /// The quoting rule is KiCad's, because KiCad is the writer Freerouting has been built against:
    /// a word is quoted when it is empty, begins with <c>#</c>, contains a tab, space, parenthesis,
    /// <c>%</c>, <c>{</c> or <c>}</c>, or contains a <c>-</c> anywhere but first. So <c>USB_D+</c>
    /// stands bare and <c>USB_D-</c> does not. A <c>"</c> inside a word cannot be escaped in this
    /// format at all; it is written as <c>''</c>, as KiCad does.
    /// </para>
    /// </remarks>
    public sealed class SpecctraNode
    {
        private readonly List<object> _items = [];

        /// <summary>Creates a form.</summary>
        /// <param name="head">The head word.</param>
        /// <param name="items">Its items: strings, numbers or nested forms.</param>
        public SpecctraNode(string head, params object?[] items)
        {
            Head = head ?? throw new ArgumentNullException(nameof(head));
            foreach (var item in items)
            {
                Add(item);
            }
        }

        /// <summary>Gets the head word.</summary>
        public string Head { get; }

        /// <summary>Gets every item, atoms as strings and forms as <see cref="SpecctraNode"/>, in order.</summary>
        public IReadOnlyList<object> Items => _items;

        /// <summary>Gets the atoms, in order.</summary>
        public IEnumerable<string> Atoms => _items.OfType<string>();

        /// <summary>Gets the nested forms, in order.</summary>
        public IEnumerable<SpecctraNode> Children => _items.OfType<SpecctraNode>();

        /// <summary>Appends an item. A number is written as a number, <see langword="null"/> is skipped.</summary>
        /// <param name="item">A string, a number, a form, or <see langword="null"/>.</param>
        /// <returns>This form, to chain.</returns>
        public SpecctraNode Add(object? item)
        {
            switch (item)
            {
                case null:
                    break;
                case SpecctraNode node:
                    _items.Add(node);
                    break;
                case string text:
                    _items.Add(text);
                    break;
                case double d:
                    _items.Add(Number(d));
                    break;
                case int i:
                    _items.Add(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case IEnumerable<object?> many:
                    foreach (var each in many)
                    {
                        Add(each);
                    }

                    break;
                default:
                    _items.Add(Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty);
                    break;
            }

            return this;
        }

        /// <summary>The first nested form with this head, or <see langword="null"/>.</summary>
        /// <param name="head">The head word.</param>
        /// <returns>The form.</returns>
        public SpecctraNode? Child(string head) =>
            Children.FirstOrDefault(c => string.Equals(c.Head, head, StringComparison.Ordinal));

        /// <summary>Every nested form with this head.</summary>
        /// <param name="head">The head word.</param>
        /// <returns>The forms.</returns>
        public IEnumerable<SpecctraNode> ChildrenNamed(string head) =>
            Children.Where(c => string.Equals(c.Head, head, StringComparison.Ordinal));

        /// <summary>The atom at <paramref name="index"/> among the atoms, or <see langword="null"/>.</summary>
        /// <param name="index">Zero-based, counting atoms only.</param>
        /// <returns>The atom.</returns>
        public string? Atom(int index) => Atoms.Skip(index).FirstOrDefault();

        /// <summary>The atom at <paramref name="index"/> read as a number.</summary>
        /// <param name="index">Zero-based, counting atoms only.</param>
        /// <returns>The number.</returns>
        /// <exception cref="FormatException">It is missing or not a number.</exception>
        public double Number(int index) =>
            double.Parse(
                Atom(index) ?? throw new FormatException($"({Head} …) has no atom {index}"),
                NumberStyles.Float,
                CultureInfo.InvariantCulture);

        /// <summary>The form as Specctra text, indented, ending in a newline.</summary>
        /// <returns>The text.</returns>
        public override string ToString()
        {
            var text = new StringBuilder();
            Write(text, 0);
            return text.ToString();
        }

        /// <summary>Whether a word must be quoted to stay one token.</summary>
        /// <param name="word">The word.</param>
        /// <returns>True when it must.</returns>
        public static bool NeedsQuotes(string word)
        {
            ArgumentNullException.ThrowIfNull(word);
            if (word.Length == 0 || word[0] == '#')
            {
                return true;
            }

            for (var i = 0; i < word.Length; i++)
            {
                var c = word[i];
                if (c is '\t' or ' ' or '(' or ')' or '%' or '{' or '}' or '"' || (c == '-' && i > 0))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A length in the units this library writes — micrometres, to 0.1 nm.</summary>
        internal static string Number(double value)
        {
            var text = Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
            return text == "-0" ? "0" : text;
        }

        private void Write(StringBuilder text, int depth)
        {
            text.Append(' ', depth * 2).Append('(').Append(Head);
            var flat = _items.All(i => i is string || (i is SpecctraNode n && !n.Children.Any() && n._items.Count <= 8))
                && _items.Count <= 32;
            if (flat)
            {
                WriteFlat(text);
                text.Append(")\n");
                return;
            }

            var column = text.Length;
            foreach (var item in _items)
            {
                if (item is string atom)
                {
                    if (text.Length - column > 90)
                    {
                        text.Append('\n').Append(' ', (depth * 2) + 4);
                        column = text.Length;
                    }

                    text.Append(' ').Append(Quote(atom));
                }
                else
                {
                    text.Append('\n');
                    ((SpecctraNode)item).Write(text, depth + 1);
                    text.Length--;
                    column = text.Length;
                }
            }

            text.Append('\n').Append(' ', depth * 2).Append(")\n");
        }

        private void WriteFlat(StringBuilder text)
        {
            foreach (var item in _items)
            {
                if (item is string atom)
                {
                    // `(string_quote ")` declares the quote; quoting it would declare something else.
                    text.Append(' ').Append(string.Equals(Head, "string_quote", StringComparison.Ordinal) ? atom : Quote(atom));
                }
                else
                {
                    var inner = (SpecctraNode)item;
                    text.Append(" (").Append(inner.Head);
                    inner.WriteFlat(text);
                    text.Append(')');
                }
            }
        }

        private static string Quote(string atom)
        {
            var clean = atom.Replace("\"", "''", StringComparison.Ordinal);
            return NeedsQuotes(atom) ? "\"" + clean + "\"" : clean;
        }
    }

    /// <summary>Reads Specctra text into <see cref="SpecctraNode"/>s.</summary>
    public static class SpecctraReader
    {
        /// <summary>Reads the first form in the text.</summary>
        /// <param name="text">A <c>.dsn</c> or <c>.ses</c> file's content.</param>
        /// <returns>The form.</returns>
        /// <exception cref="FormatException">The text is not one well-formed form.</exception>
        public static SpecctraNode Parse(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            var reader = new Reader(text);
            reader.SkipSpace();
            if (!reader.TryTake('('))
            {
                throw new FormatException("A Specctra file begins with '('.");
            }

            return reader.ReadForm();
        }

        private sealed class Reader(string text)
        {
            private int _at;
            private char _quote = '"';

            internal SpecctraNode ReadForm()
            {
                SkipSpace();
                var head = ReadWord();
                var node = new SpecctraNode(head);
                if (string.Equals(head, "string_quote", StringComparison.Ordinal))
                {
                    // `(string_quote ")` - the next character IS the quote, whatever it is.
                    SkipSpace();
                    _quote = text[_at++];
                    node.Add(_quote.ToString());
                }

                while (true)
                {
                    SkipSpace();
                    if (_at >= text.Length)
                    {
                        throw new FormatException($"({head} … is not closed.");
                    }

                    if (TryTake(')'))
                    {
                        return node;
                    }

                    if (TryTake('('))
                    {
                        node.Add(ReadForm());
                    }
                    else
                    {
                        node.Add(ReadWord());
                    }
                }
            }

            internal void SkipSpace()
            {
                while (_at < text.Length && char.IsWhiteSpace(text[_at]))
                {
                    _at++;
                }
            }

            internal bool TryTake(char c)
            {
                if (_at < text.Length && text[_at] == c)
                {
                    _at++;
                    return true;
                }

                return false;
            }

            private string ReadWord()
            {
                if (TryTake(_quote))
                {
                    var end = text.IndexOf(_quote, _at);
                    if (end < 0)
                    {
                        throw new FormatException("A quoted word is not closed.");
                    }

                    var word = text[_at..end];
                    _at = end + 1;
                    return word;
                }

                var start = _at;
                while (_at < text.Length && !char.IsWhiteSpace(text[_at]) && text[_at] != '(' && text[_at] != ')')
                {
                    _at++;
                }

                return text[start.._at];
            }
        }
    }
}
