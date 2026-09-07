using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

using SExpressions;

namespace KiCadSharp.Documents
{
    /// <summary>
    /// A typed view over one s-expression node.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing derived from this holds state of its own. Every property reads the node it was given
    /// and every setter writes back into that same node, so a token this library has never heard of
    /// is still there after a save: it was never copied out and it is never rebuilt.
    /// </para>
    /// <para>
    /// That is the whole point. The previous document layer was a parallel object model — load
    /// copied the tokens it recognised into fields, save re-serialised the fields — and everything
    /// it did not recognise was dropped on the way through. A view has nothing to drop.
    /// </para>
    /// <para>
    /// Views are cheap and disposable: asking a collection for the same element twice returns two
    /// wrappers over one node. They compare equal, and writing through either is writing to the
    /// same place.
    /// </para>
    /// </remarks>
    public abstract class KiCadNode : IEquatable<KiCadNode>
    {
        /// <summary>Creates a view over <paramref name="node"/>.</summary>
        /// <param name="node">The s-expression to read and write through.</param>
        protected KiCadNode(SExpression node)
        {
            ArgumentNullException.ThrowIfNull(node);
            Node = node;
        }

        /// <summary>
        /// The s-expression this view reads and writes through. Everything this type does not model
        /// is reachable here, and stays in the file whether or not anyone reaches for it.
        /// </summary>
        public SExpression Node { get; }

        /// <summary>Gets the underlying s-expression. Same object as <see cref="Node"/>.</summary>
        /// <returns>The live node — not a rebuilt copy.</returns>
        public SExpression ToSExpression() => Node;

        // ------------------------------------------------------------------------------- reading

        /// <summary>Reads a value from a child form.</summary>
        /// <param name="token">The child token.</param>
        /// <param name="index">The value index inside the child.</param>
        /// <returns>The value, or <see langword="null"/> when the child or the value is missing.</returns>
        protected string? ReadChild(string token, int index = 0) => Node.GetChild(token)?.GetValue(index);

        /// <summary>Reads a number from a child form.</summary>
        /// <param name="token">The child token.</param>
        /// <param name="index">The value index inside the child.</param>
        /// <param name="fallback">Returned when the child or the value is missing.</param>
        /// <returns>The number.</returns>
        protected double ReadChildDouble(string token, int index = 0, double fallback = 0)
        {
            var child = Node.GetChild(token);
            return child is not null && child.TryGetValue<double>(index, out var value) ? value : fallback;
        }

        /// <summary>
        /// Reads a KiCad boolean child, which is spelled three different ways across format versions:
        /// <c>(hide yes)</c> in KiCad 7+, a bare <c>(hide)</c> in KiCad 6, and absent for false.
        /// </summary>
        /// <param name="token">The child token.</param>
        /// <returns>True when the flag is present and not explicitly <c>no</c>.</returns>
        protected bool ReadFlag(string token)
        {
            var child = Node.GetChild(token);
            if (child is null)
            {
                return false;
            }

            var value = child.GetValue(0);

            // A bare (hide) is the KiCad 6 spelling of (hide yes).
            return value is null || child.GetValueAsBool();
        }

        // ------------------------------------------------------------------------------- writing

        /// <summary>
        /// Writes a value into a child form, creating the child when it is missing and removing it
        /// when <paramref name="value"/> is <see langword="null"/>.
        /// </summary>
        /// <param name="token">The child token.</param>
        /// <param name="value">The new value, or <see langword="null"/> to remove the child.</param>
        /// <param name="quote">How to write the atom.</param>
        protected void WriteChild(string token, string? value, SQuoteStyle quote = SQuoteStyle.Auto)
        {
            if (value is null)
            {
                Node.RemoveChild(token);
                return;
            }

            Node.SetChildValue(token, value, quote);
        }

        /// <summary>Writes a number into a child form, invariant culture, KiCad's own formatting.</summary>
        /// <param name="token">The child token.</param>
        /// <param name="value">The number.</param>
        protected void WriteChildDouble(string token, double value) =>
            Node.SetChildValue(token, Format(value), SQuoteStyle.Bare);

        /// <summary>Writes a KiCad boolean child as <c>(token yes)</c> / <c>(token no)</c>.</summary>
        /// <param name="token">The child token.</param>
        /// <param name="value">The flag.</param>
        protected void WriteFlag(string token, bool value) =>
            Node.SetChildValue(token, value ? "yes" : "no", SQuoteStyle.Bare);

        /// <summary>Replaces one of this node's own bare values, appending when it is not there yet.</summary>
        /// <param name="index">The value index.</param>
        /// <param name="value">The new text.</param>
        /// <param name="quote">How to write the atom.</param>
        protected void WriteValue(int index, string value, SQuoteStyle quote = SQuoteStyle.Auto) =>
            Node.SetValue(index, value, quote);

        /// <summary>
        /// Formats a number the way KiCad does: invariant, no exponent, no trailing zeroes beyond
        /// what the value needs.
        /// </summary>
        /// <param name="value">The number.</param>
        /// <returns>The text.</returns>
        protected static string Format(double value) => value.ToString("0.############", CultureInfo.InvariantCulture);

        /// <summary>Gets or creates the child form with the given token.</summary>
        /// <param name="token">The child token.</param>
        /// <returns>The child, existing or newly appended.</returns>
        protected SExpression Require(string token) => Node.GetChild(token) ?? Node.CreateChild(token);

        // ------------------------------------------------------------------------------ identity

        /// <summary>Two views are equal when they are views over the same node.</summary>
        /// <param name="other">The other view.</param>
        /// <returns>True when both wrap the same node.</returns>
        public bool Equals(KiCadNode? other) => other is not null && ReferenceEquals(Node, other.Node);

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as KiCadNode);

        /// <inheritdoc />
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(Node);

        /// <inheritdoc />
        public override string ToString() => Node.ToString();
    }

    /// <summary>
    /// A live, typed view over the child forms of one node that carry a given token.
    /// </summary>
    /// <typeparam name="T">The view type to wrap each child in.</typeparam>
    /// <remarks>
    /// Nothing is cached: the list is the node's children, read at the moment you ask. Adding here
    /// appends a child to the node and removing here removes one, so the file follows the list
    /// rather than being rebuilt from it.
    /// </remarks>
    public sealed class KiCadNodeList<T> : IReadOnlyList<T>
        where T : KiCadNode
    {
        private readonly SExpression _owner;
        private readonly string _token;
        private readonly Func<SExpression, T> _view;

        internal KiCadNodeList(SExpression owner, string token, Func<SExpression, T> view)
        {
            _owner = owner;
            _token = token;
            _view = view;
        }

        /// <summary>Gets how many children carry the token.</summary>
        public int Count
        {
            get
            {
                var n = 0;
                foreach (var _ in _owner.GetChildren(_token))
                {
                    n++;
                }

                return n;
            }
        }

        /// <summary>Gets a view over the child at <paramref name="index"/>.</summary>
        /// <param name="index">Zero-based position among the children carrying the token.</param>
        public T this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                var i = 0;
                foreach (var child in _owner.GetChildren(_token))
                {
                    if (i++ == index)
                    {
                        return _view(child);
                    }
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        /// <summary>Appends a new child with the token and returns a view over it.</summary>
        /// <returns>The new element.</returns>
        public T Add() => _view(_owner.CreateChild(_token));

        /// <summary>Appends an existing node as a child. It must carry this list's token.</summary>
        /// <param name="item">The view whose node to append.</param>
        /// <returns>The appended element.</returns>
        /// <exception cref="ArgumentException">The node's token does not match.</exception>
        public T Add(T item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!string.Equals(item.Node.Token, _token, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Expected a ({_token} ...) node but got ({item.Node.Token} ...).", nameof(item));
            }

            _owner.AddChild(item.Node);
            return item;
        }

        /// <summary>Removes the child this view wraps.</summary>
        /// <param name="item">The element to remove.</param>
        /// <returns>True when it was a child of this node and was removed.</returns>
        public bool Remove(T item)
        {
            ArgumentNullException.ThrowIfNull(item);
            return _owner.Children.Remove(item.Node);
        }

        /// <summary>Inserts a new child at <paramref name="index"/> among this node's children.</summary>
        /// <param name="index">Zero-based position among all child forms of the owner.</param>
        /// <param name="item">The view whose node to insert.</param>
        public void Insert(int index, T item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _owner.Children.Insert(index, item.Node);
        }

        /// <inheritdoc />
        public IEnumerator<T> GetEnumerator()
        {
            foreach (var child in _owner.GetChildren(_token))
            {
                yield return _view(child);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
