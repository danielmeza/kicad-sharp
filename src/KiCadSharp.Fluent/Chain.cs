using System;

namespace KiCadSharp.Fluent
{
    /// <summary>
    /// The one rule every <c>With*</c> here follows, written once.
    /// </summary>
    /// <remarks>
    /// <c>parent.WithX(args, configure)</c> is exactly <c>configure?.Invoke(parent.AddX(args))</c>
    /// followed by <c>return parent</c>. The child is appended before the callback runs, so a
    /// callback that throws leaves it appended — the same state the equivalent <c>Add*</c> statement
    /// followed by a throwing statement would leave.
    /// </remarks>
    internal static class Chain
    {
        /// <summary>Hands <paramref name="child"/> to <paramref name="configure"/>, then returns <paramref name="parent"/>.</summary>
        /// <typeparam name="TParent">The type the chain continues on.</typeparam>
        /// <typeparam name="TChild">The type of the child the <c>Add*</c> call produced.</typeparam>
        /// <param name="parent">The object the <c>Add*</c> was called on.</param>
        /// <param name="child">What the <c>Add*</c> returned.</param>
        /// <param name="configure">The caller's callback, or <see langword="null"/>.</param>
        /// <returns><paramref name="parent"/>.</returns>
        internal static TParent Then<TParent, TChild>(TParent parent, TChild child, Action<TChild>? configure)
        {
            configure?.Invoke(child);
            return parent;
        }
    }
}
