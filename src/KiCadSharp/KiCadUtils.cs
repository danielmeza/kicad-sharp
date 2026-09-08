using System;
using System.IO;

using KiCadSharp.Documents;

using SExpressions;

namespace KiCadSharp
{
    /// <summary>
    /// Provides utility functions for working with KiCad files and handling format conversions
    /// </summary>
    public static class KiCadUtils
    {
        /// <summary>
        /// Parse a KiCad symbol library file
        /// </summary>
        /// <param name="filePath">Path to the KiCad symbol library file (.kicad_sym)</param>
        /// <returns>The parsed symbol library</returns>
        public static KiCadSymbolLibrary ParseSymbolLibrary(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"KiCad symbol library file not found: {filePath}");
            }

            // Load, not Parse: SExpressionParser.ParseFile returns only the first top-level form
            // and drops the rest of the file with it. KiCadSymbolLibrary.Load keeps the whole
            // document, which is what makes an untouched save byte-identical.
            return KiCadSymbolLibrary.Load(filePath);
        }

        /// <summary>
        /// Export a symbol to a new KiCad symbol library file
        /// </summary>
        /// <param name="symbol">Symbol to export</param>
        /// <param name="filePath">Path to the output file</param>
        public static void ExportSymbolToLibrary(KiCadSymbol symbol, string filePath)
        {
            var library = new KiCadSymbolLibrary();
            library.AddSymbol(symbol);
            library.Save(filePath);
        }

        /// <summary>
        /// Validate a KiCad symbol library file
        /// </summary>
        /// <param name="filePath">Path to the KiCad symbol library file</param>
        /// <returns>True if the file is valid, false otherwise</returns>
        public static bool ValidateSymbolLibrary(string filePath)
        {
            try
            {
                var parser = new SExpressionParser();
                var rootExpression = parser.ParseFile(filePath);
                
                // Verify it's a symbol library
                if (rootExpression.Token != KiCadTokens.Symbol.LibraryRoot)
                {
                    return false;
                }
                
                // Verify it has a version
                if (rootExpression.GetChild(KiCadTokens.Common.Version) == null)
                {
                    return false;
                }
                
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Create a copy of a symbol with a new name
        /// </summary>
        /// <param name="originalSymbol">Original symbol to copy</param>
        /// <param name="newId">New ID for the symbol</param>
        /// <returns>A copy of the symbol with the new ID</returns>
        /// <remarks>
        /// The copy is a deep copy of the original's node. Since <see cref="KiCadSymbol"/> is a view,
        /// wrapping the same node twice would give two handles on one symbol, and renaming through
        /// either would rename the original.
        /// </remarks>
        public static KiCadSymbol CloneSymbol(KiCadSymbol originalSymbol, string newId)
        {
            ArgumentNullException.ThrowIfNull(originalSymbol);
            return originalSymbol.CloneAs(newId);
        }

        // --------------------------------------------------------------------------- footprints

        /// <summary>
        /// Parse a KiCad footprint file: a single footprint (<c>.kicad_mod</c>) or a board
        /// (<c>.kicad_pcb</c>).
        /// </summary>
        /// <param name="filePath">Path to the file.</param>
        /// <returns>The parsed library.</returns>
        /// <exception cref="FileNotFoundException">The file is not there.</exception>
        public static KiCadFootprintLibrary ParseFootprintLibrary(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"KiCad footprint file not found: {filePath}");
            }

            return KiCadFootprintLibrary.Load(filePath);
        }

        /// <summary>
        /// Export a footprint to its own <c>.kicad_mod</c> file.
        /// </summary>
        /// <param name="footprint">Footprint to export.</param>
        /// <param name="filePath">Path to the output file.</param>
        /// <remarks>
        /// The footprint's own bytes are reproduced, so exporting one out of a board and reading it
        /// back gives the same form the board held — including everything this library does not
        /// model.
        /// </remarks>
        public static void ExportFootprintToFile(KiCadFootprint footprint, string filePath) =>
            KiCadFootprintLibrary.SaveFootprint(footprint, filePath);

        /// <summary>
        /// Validate a KiCad footprint file.
        /// </summary>
        /// <param name="filePath">Path to the file.</param>
        /// <returns>True when the file parses and its root form is one KiCad would recognise.</returns>
        /// <remarks>
        /// Accepts all three spellings: <c>footprint</c> (KiCad 6+ <c>.kicad_mod</c>), <c>module</c>
        /// (KiCad 5), and <c>kicad_pcb</c> (a board, which holds footprints as children). A KiCad 5
        /// <c>module</c> has no <c>version</c> token, so that is only required of the other two.
        /// </remarks>
        public static bool ValidateFootprintLibrary(string filePath)
        {
            try
            {
                var root = SDocument.Load(filePath).Root;
                if (root is null)
                {
                    return false;
                }

                if (string.Equals(root.Token, KiCadTokens.Footprint.LegacyRoot, StringComparison.Ordinal))
                {
                    return true;
                }

                var isFootprintFile = string.Equals(root.Token, KiCadTokens.Footprint.Root, StringComparison.Ordinal)
                    || string.Equals(root.Token, KiCadTokens.Board.Root, StringComparison.Ordinal);

                return isFootprintFile && root.GetChild(KiCadTokens.Common.Version) is not null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Create a copy of a footprint under a new name.
        /// </summary>
        /// <param name="originalFootprint">Footprint to copy.</param>
        /// <param name="newId">Name for the copy.</param>
        /// <returns>The copy, with no parent.</returns>
        /// <remarks>
        /// A deep copy, for the same reason <see cref="CloneSymbol"/> is: a <see cref="KiCadFootprint"/>
        /// is a view, so wrapping one node twice would give two handles on one footprint and renaming
        /// through either would rename the original.
        /// </remarks>
        public static KiCadFootprint CloneFootprint(KiCadFootprint originalFootprint, string newId)
        {
            ArgumentNullException.ThrowIfNull(originalFootprint);
            return originalFootprint.CloneAs(newId);
        }

        /// <summary>
        /// Extract library name from a KiCad library file path
        /// </summary>
        /// <param name="libraryPath">Path to a KiCad library file</param>
        /// <returns>The library name without path or extension</returns>
        public static string GetLibraryName(string libraryPath)
        {
            return Path.GetFileNameWithoutExtension(libraryPath);
        }
    }
}