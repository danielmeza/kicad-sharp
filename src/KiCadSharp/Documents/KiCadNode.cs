using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    /// <para>
    /// <b>Adding a view moves its node.</b> A node has one parent, so every <c>Add…</c> that takes an
    /// existing view — <see cref="KiCadNodeList{T}.Add(T)"/>, <see cref="KiCadNodeList{T}.Insert"/>,
    /// <see cref="KiCadSymbolLibrary.AddSymbol(KiCadSymbol)"/>,
    /// <see cref="KiCadFootprintLibrary.AddFootprint"/>, <c>AddPin</c>,
    /// <see cref="KiCadSymbol.AddGraphicalItem"/> — takes the node out of wherever it was, including
    /// another file, and puts that same node here. That is what keeps the view you hold and the
    /// element in the destination one thing, and the node keeps the bytes it was parsed from. To leave
    /// the original where it is, add a copy: <c>new KiCadSymbol(symbol.Node.Clone())</c>, or
    /// <c>CloneAs</c> to rename it on the way. Moving out of a list while a <c>foreach</c> walks it is
    /// safe (see <see cref="KiCadNodeList{T}"/>); walking it forward by index is not.
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
        /// <remarks>
        /// A missing child is created where KiCad reads it, which is the end of the form except in the
        /// few forms KiCad reads by position; see <see cref="Require"/>.
        /// </remarks>
        protected void WriteChild(string token, string? value, SQuoteStyle quote = SQuoteStyle.Auto)
        {
            if (value is null)
            {
                Node.RemoveChild(token);
                return;
            }

            KiCadChildOrder.Place(Node, token);
            Node.SetChildValue(token, value, quote);
        }

        /// <summary>Writes a number into a child form, invariant culture, KiCad's own formatting.</summary>
        /// <param name="token">The child token.</param>
        /// <param name="value">The number.</param>
        /// <remarks>A missing child is created where KiCad reads it; see <see cref="Require"/>.</remarks>
        protected void WriteChildDouble(string token, double value)
        {
            KiCadChildOrder.Place(Node, token);
            Node.SetChildValue(token, Format(value), SQuoteStyle.Bare);
        }

        /// <summary>Writes a KiCad boolean child as <c>(token yes)</c> / <c>(token no)</c>.</summary>
        /// <param name="token">The child token.</param>
        /// <param name="value">The flag.</param>
        /// <remarks>A missing child is created where KiCad reads it; see <see cref="Require"/>.</remarks>
        protected void WriteFlag(string token, bool value)
        {
            KiCadChildOrder.Place(Node, token);
            Node.SetChildValue(token, value ? KiCadTokens.Common.Yes : KiCadTokens.Common.No, SQuoteStyle.Bare);
        }

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
        /// <returns>The child, existing or new.</returns>
        /// <remarks>
        /// <para>
        /// A new child goes at the end of the form, except in the forms KiCad reads partly by
        /// position. There it goes where KiCad's parser looks for it, whatever order the properties
        /// are set in: the <c>pts</c> of an <c>fp_poly</c>, <c>gr_poly</c> or curve first; an arc's
        /// <c>start</c>, <c>mid</c> and <c>end</c> first and in that order; a line's or rectangle's
        /// <c>start</c> then <c>end</c>; a circle's <c>center</c> then <c>end</c>; a dimension's
        /// <c>type</c>; a filled polygon's <c>layer</c> before its points; a file's <c>version</c>.
        /// KiCad 10 refuses the whole file when one of these is anywhere else (#59).
        /// </para>
        /// <para>
        /// Children that are already there never move, so a loaded file keeps its bytes.
        /// </para>
        /// </remarks>
        protected SExpression Require(string token) => KiCadChildOrder.Require(Node, token);

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
    /// <para>
    /// The owner may be absent, and then the list is live AND empty. That is the shape a read-only
    /// property needs when the form it reads has not been written yet: it can report "nothing here"
    /// without creating a <c>(lib_symbols)</c> nobody asked for, and without degrading into a
    /// snapshot that stops tracking the file the moment someone adds to it. Mutating such a list
    /// throws, because there is nothing to mutate — take the <c>Require…()</c> accessor instead.
    /// </para>
    /// <para>
    /// <b>Enumerating walks the children that were there when it started.</b> <see cref="Count"/>,
    /// the indexer, <see cref="Add(T)"/>, <see cref="Remove"/> and <see cref="Insert"/> stay live;
    /// only a walk is fixed at its start. So a <c>foreach</c> may move, remove or append elements of
    /// the list it is walking — the natural copy loop,
    /// <c>foreach (var s in source.Symbols) destination.AddSymbol(s)</c>, moves every symbol, where
    /// a live walk skipped every other one (#50), and a loop that appends to its own list ends. An
    /// element removed during the walk is still visited; one appended is not; the next walk sees the
    /// list as it is then. The indexer does not snapshot: a forward index loop over a list that a
    /// move is shrinking still steps over every other element, as it would over a
    /// <see cref="List{T}"/>. Walk it with <c>foreach</c>, backwards, or take <c>[0]</c> until
    /// <see cref="Count"/> is 0.
    /// </para>
    /// </remarks>
    public sealed class KiCadNodeList<T> : IReadOnlyList<T>
        where T : KiCadNode
    {
        private static readonly IEnumerable<SExpression> None = Array.Empty<SExpression>();

        private readonly SExpression? _owner;
        private readonly string _token;
        private readonly Func<SExpression, T> _view;

        internal KiCadNodeList(SExpression? owner, string token, Func<SExpression, T> view)
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
                foreach (var _ in _owner?.GetChildren(_token) ?? None)
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
                foreach (var child in _owner?.GetChildren(_token) ?? None)
                {
                    if (i++ == index)
                    {
                        return _view(child);
                    }
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        /// <summary>The owner, or a clear failure when this list is the empty stand-in for a missing form.</summary>
        private SExpression Owner => _owner ?? throw new InvalidOperationException(
            $"This list is a live view over a ({_token} ...) form that does not exist in the file. "
            + "Reading it is fine and reports nothing; to add to it, take the Require... accessor, "
            + "which creates the form deliberately.");

        /// <summary>Appends a new child with the token and returns a view over it.</summary>
        /// <returns>The new element.</returns>
        public T Add() => _view(Owner.CreateChild(_token));

        /// <summary>
        /// Appends an existing node as a child, moving it out of wherever it was. It must carry this
        /// list's token.
        /// </summary>
        /// <param name="item">The view whose node to append.</param>
        /// <returns>The appended element: <paramref name="item"/> itself, now in this list.</returns>
        /// <exception cref="ArgumentException">The node's token does not match.</exception>
        /// <remarks>
        /// The node leaves its previous parent, in this file or another; see <see cref="KiCadNode"/>.
        /// Add <c>item.Node.Clone()</c> wrapped in a view to keep the original.
        /// </remarks>
        public T Add(T item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!string.Equals(item.Node.Token, _token, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Expected a ({_token} ...) node but got ({item.Node.Token} ...).", nameof(item));
            }

            Owner.AddChild(item.Node);
            return item;
        }

        /// <summary>Removes the child this view wraps.</summary>
        /// <param name="item">The element to remove.</param>
        /// <returns>True when it was a child of this node and was removed.</returns>
        public bool Remove(T item)
        {
            ArgumentNullException.ThrowIfNull(item);
            return Owner.Children.Remove(item.Node);
        }

        /// <summary>
        /// Inserts an existing node at <paramref name="index"/> among this node's children, moving it
        /// out of wherever it was.
        /// </summary>
        /// <param name="index">Zero-based position among all child forms of the owner.</param>
        /// <param name="item">The view whose node to insert.</param>
        /// <remarks>The node leaves its previous parent, as with <see cref="Add(T)"/>.</remarks>
        public void Insert(int index, T item)
        {
            ArgumentNullException.ThrowIfNull(item);
            Owner.Children.Insert(index, item.Node);
        }

        /// <summary>
        /// Walks the children that carry the token at the moment this is called, as views.
        /// </summary>
        /// <returns>An enumerator over that set, unaffected by later changes to the list.</returns>
        /// <remarks>
        /// The children are listed here, eagerly, and not inside an iterator: an iterator body only
        /// runs at the first <c>MoveNext</c>, so the set would depend on what happened in between.
        /// Only the node references are copied; each view is made as it is reached.
        /// </remarks>
        public IEnumerator<T> GetEnumerator() => Walk(_owner?.GetChildren(_token).ToArray() ?? [], _view);

        private static IEnumerator<T> Walk(SExpression[] children, Func<SExpression, T> view)
        {
            foreach (var child in children)
            {
                yield return view(child);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
