using Kiapi.Common.Commands;
using Kiapi.Common.Types;
using Kiapi.Schematic.Commands;
using Kiapi.Schematic.Types;

namespace KiCadSharp
{
    /// <summary>
    /// A KiCad schematic open in eeschema, over IPC.
    /// </summary>
    /// <remarks>
    /// KiCad 10.99 and later: on 10.0.x eeschema answers nothing at all (see docs/ipc.md). The
    /// editor-level commands, saving, commits, items, selection, page settings, variants and jobs,
    /// are on <see cref="KiCadDocument"/>. What is here is specific to a schematic.
    /// </remarks>
    public class Schematic : KiCadDocument
    {
        /// <summary>Creates a new Schematic proxy.</summary>
        /// <param name="client">KiCad IPC client.</param>
        /// <param name="document">Document specifier for the schematic. Its sheet path names the sheet commands apply to.</param>
        public Schematic(KiCadIPCClient client, DocumentSpecifier document) : base(client, document)
        {
        }

        /// <summary>The human-readable sheet path, or an empty string when the specifier carries none.</summary>
        public string Name => Document.SheetPath?.PathHumanReadable ?? string.Empty;

        /// <summary>The project this schematic belongs to.</summary>
        /// <returns>Project object.</returns>
        public Project GetProject()
        {
            return new Project(Client, Document);
        }

        /// <summary>The sheet hierarchy of the whole schematic, from its top-level sheets down.</summary>
        /// <returns>The top-level sheet instances, each with its sub-sheets.</returns>
        public async ValueTask<SheetInstance[]> GetHierarchy(CancellationToken cancellationToken = default)
        {
            var response = await Send<SchematicHierarchyResponse>(new GetSchematicHierarchy { Document = Document }, cancellationToken);
            return response.TopLevelSheets.ToArray();
        }

        /// <summary>The schematic's nets and what is on each of them, per sheet.</summary>
        /// <param name="types">Optional filter on the connectable item types listed per net: pins, labels, wires. Empty means all.</param>
        /// <returns>The nets.</returns>
        public ValueTask<SchematicNet[]> GetNetlist(params KiCadObjectType[] types) => GetNetlist(default, types);

        /// <summary>Same, with a cancellation token. The token comes first because a <c>params</c> array must be last.</summary>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <param name="types">As above.</param>
        /// <returns>As above.</returns>
        public async ValueTask<SchematicNet[]> GetNetlist(CancellationToken cancellationToken, params KiCadObjectType[] types)
        {
            var command = new GetSchematicNetlist { Document = Document };
            command.Types_.AddRange(types);

            var response = await Send<SchematicNetlistResponse>(command, cancellationToken);
            return response.Nets.ToArray();
        }

        /// <summary>Places a symbol from a library on this sheet.</summary>
        /// <param name="libraryId">The symbol, as library nickname and entry name.</param>
        /// <param name="position">Where to place it.</param>
        /// <param name="orientation">Its rotation and mirroring, or null for the default.</param>
        /// <param name="unit">Which unit of a multi-unit symbol, or null for the first.</param>
        /// <param name="reference">Its reference designator, or null to leave annotation to KiCad's current preference.</param>
        /// <returns>The placed symbol.</returns>
        public async ValueTask<SchematicSymbol> PlaceSymbolFromLibrary(
            LibraryIdentifier libraryId,
            Vector2 position,
            SchematicSymbolOrientation? orientation = null,
            SchematicSymbolUnit? unit = null,
            string? reference = null,
            CancellationToken cancellationToken = default)
        {
            var command = new PlaceSymbolFromLibrary
            {
                Header = Header(),
                LibId = libraryId,
                Position = position,
            };
            if (orientation is { } value)
            {
                command.Orientation = value;
            }

            if (unit is not null)
            {
                command.Unit = unit;
            }

            if (reference is not null)
            {
                command.Reference = reference;
            }

            var response = await Send<PlaceFromLibraryResponse>(command, cancellationToken);
            if (!response.Item.TryUnpack<SchematicSymbol>(out var symbol))
            {
                throw new ApiException($"KiCad placed the symbol but returned a {response.Item.TypeUrl}, not a SchematicSymbol.");
            }

            return symbol;
        }
    }
}
