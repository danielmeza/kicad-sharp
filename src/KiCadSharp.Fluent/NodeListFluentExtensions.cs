using System;

using KiCadSharp.Documents;

namespace KiCadSharp.Fluent
{
    /// <summary>
    /// The fluent mirror of <see cref="KiCadNodeList{T}"/>'s two <c>Add</c> overloads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most of a board and a schematic have no <c>Add*</c> of their own — a zone, a track, a wire, a
    /// label is appended through the live list that holds it, <c>board.Zones.Add()</c>. These make
    /// that list chainable: each calls the <c>Add</c> it mirrors, hands the element that call
    /// returned to the optional callback, and returns the list.
    /// </para>
    /// <para>
    /// A list read from a form the file does not have is live and empty, and adding to it throws;
    /// <c>With</c> throws the same <see cref="InvalidOperationException"/>, from the same place.
    /// </para>
    /// </remarks>
    public static class NodeListFluentExtensions
    {
        /// <summary>
        /// Appends a new, empty element, as <see cref="KiCadNodeList{T}.Add()"/> does, and returns the list.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="list">The list to append to.</param>
        /// <param name="configure">Called with the new element — the place to give it its content.</param>
        /// <returns><paramref name="list"/>.</returns>
        public static KiCadNodeList<T> With<T>(this KiCadNodeList<T> list, Action<T>? configure = null)
            where T : KiCadNode
        {
            ArgumentNullException.ThrowIfNull(list);
            return Chain.Then(list, list.Add(), configure);
        }

        /// <summary>
        /// Appends an existing element, moving it out of wherever it was, as
        /// <see cref="KiCadNodeList{T}.Add(T)"/> does, and returns the list.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="list">The list to append to.</param>
        /// <param name="item">The element whose node to append. Its token must be the list's.</param>
        /// <param name="configure">Called with the appended element.</param>
        /// <returns><paramref name="list"/>.</returns>
        /// <exception cref="ArgumentException">The node's token does not match.</exception>
        /// <remarks>
        /// The node leaves its previous parent, in this file or another; see <see cref="KiCadNode"/>.
        /// Pass <c>item.Node.Clone()</c> wrapped in a view to keep the original.
        /// </remarks>
        public static KiCadNodeList<T> With<T>(this KiCadNodeList<T> list, T item, Action<T>? configure = null)
            where T : KiCadNode
        {
            ArgumentNullException.ThrowIfNull(list);
            return Chain.Then(list, list.Add(item), configure);
        }
    }
}
