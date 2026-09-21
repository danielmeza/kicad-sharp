using System;
using System.Collections.Generic;
using System.Linq;

using SExpressions;

namespace KiCadSharp.Documents
{
    /// <summary>
    /// Thrown when a file parses as s-expressions but is not the kind of KiCad document it was
    /// loaded as: its root form has a different token, or the file holds no form at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every <c>Load</c>, <c>LoadAsync</c> and <c>Parse</c> in the document layer checks the root
    /// token before it returns, as KiCad does before it reads anything else. Without that check a
    /// <c>(kicad_pcb …)</c> file loads as a symbol library with no symbols, and adding a symbol to it
    /// and saving replaces the board.
    /// </para>
    /// <para>
    /// It derives from <see cref="InvalidOperationException"/> because that is what
    /// <see cref="KiCadBoard"/> and <c>KiCadSchematic</c> threw for a wrong root before this type
    /// existed, so a caller that catches that still catches this. Text that is not s-expressions at
    /// all, such as an unclosed parenthesis, is a different failure: the parser reports it as a
    /// <see cref="SExpressionFormatException"/>, with its line and column.
    /// </para>
    /// </remarks>
    public class KiCadDocumentTypeException : InvalidOperationException
    {
        /// <summary>Creates the exception with no message.</summary>
        public KiCadDocumentTypeException()
        {
        }

        /// <summary>Creates the exception with a message.</summary>
        /// <param name="message">What went wrong.</param>
        public KiCadDocumentTypeException(string? message)
            : base(message)
        {
        }

        /// <summary>Creates the exception with a message and a cause.</summary>
        /// <param name="message">What went wrong.</param>
        /// <param name="innerException">The underlying failure.</param>
        public KiCadDocumentTypeException(string? message, Exception? innerException)
            : base(message, innerException)
        {
        }

        /// <summary>Creates the exception with the root it found and the roots it would have accepted.</summary>
        /// <param name="message">What went wrong.</param>
        /// <param name="filePath">The file, or <see langword="null"/> when the document was parsed from text.</param>
        /// <param name="expectedRootTokens">The root tokens the loader accepts.</param>
        /// <param name="actualRootToken">The root token the file has, or <see langword="null"/> when it holds no form.</param>
        public KiCadDocumentTypeException(string? message, string? filePath, IReadOnlyList<string> expectedRootTokens, string? actualRootToken)
            : base(message)
        {
            ArgumentNullException.ThrowIfNull(expectedRootTokens);
            FilePath = filePath;
            ExpectedRootTokens = [.. expectedRootTokens];
            ActualRootToken = actualRootToken;
        }

        /// <summary>Gets the file that was loaded, or <see langword="null"/> when the document was parsed from text.</summary>
        public string? FilePath { get; }

        /// <summary>Gets the root tokens the loader accepts, e.g. <c>kicad_symbol_lib</c>.</summary>
        public IReadOnlyList<string> ExpectedRootTokens { get; } = Array.Empty<string>();

        /// <summary>
        /// Gets the token of the file's root form, e.g. <c>kicad_pcb</c>. It is <see langword="null"/>
        /// when the file holds no form, as an empty file does, and empty when the root form has no
        /// token, as <c>()</c> does.
        /// </summary>
        public string? ActualRootToken { get; }
    }

    /// <summary>The root-token check every document loader runs.</summary>
    internal static class KiCadDocumentRoot
    {
        /// <summary>Returns the document's root form, or throws when it is not one of the expected tokens.</summary>
        /// <param name="document">The parsed file.</param>
        /// <param name="filePath">Where it was read from, or <see langword="null"/> for text.</param>
        /// <param name="documentKind">What the caller asked for, for the message, e.g. <c>symbol library</c>.</param>
        /// <param name="expectedTokens">The root tokens that kind of document may have.</param>
        /// <returns>The root form.</returns>
        /// <exception cref="KiCadDocumentTypeException">The root is missing or has another token.</exception>
        internal static SExpression Require(SDocument document, string? filePath, string documentKind, params string[] expectedTokens)
        {
            var root = document.Root;
            if (root is not null && expectedTokens.Contains(root.Token, StringComparer.Ordinal))
            {
                return root;
            }

            var subject = filePath is null ? "The text" : $"'{filePath}'";
            var found = root is null
                ? "it holds no s-expression form"
                : root.Token.Length == 0
                    ? "its root form has no token"
                    : $"its root form is ({root.Token} ...)";

            throw new KiCadDocumentTypeException(
                $"{subject} is not a KiCad {documentKind}: expected {Describe(expectedTokens)} at the root, but {found}.",
                filePath,
                expectedTokens,
                root?.Token);
        }

        /// <summary>Throws <see cref="ArgumentException"/> when a form handed to a constructor has an unexpected token.</summary>
        /// <param name="expression">The form.</param>
        /// <param name="parameterName">The constructor parameter it was passed as.</param>
        /// <param name="expectedTokens">The tokens the constructor accepts.</param>
        /// <exception cref="ArgumentException">The form's token is not one of <paramref name="expectedTokens"/>.</exception>
        internal static void RequireArgument(SExpression expression, string parameterName, params string[] expectedTokens)
        {
            if (expectedTokens.Contains(expression.Token, StringComparer.Ordinal))
            {
                return;
            }

            throw new ArgumentException($"Expected {Describe(expectedTokens)} but got ({expression.Token} ...).", parameterName);
        }

        private static string Describe(IReadOnlyList<string> tokens)
        {
            var forms = tokens.Select(t => $"({t} ...)").ToArray();
            return forms.Length switch
            {
                1 => $"a {forms[0]} form",
                _ => $"a {string.Join(", ", forms[..^1])} or {forms[^1]} form",
            };
        }
    }
}
