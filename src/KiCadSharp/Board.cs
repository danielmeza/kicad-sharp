using Kiapi.Board;
using Kiapi.Board.Commands;
using Kiapi.Board.Jobs;
using Kiapi.Board.Types;
using Kiapi.Common.Commands;
using Kiapi.Common.Types;

namespace KiCadSharp
{
    /// <summary>
    /// A KiCad PCB open in pcbnew, over IPC.
    /// </summary>
    /// <remarks>
    /// The editor-level commands, saving, commits, items, selection, page settings, variants and
    /// jobs, are on <see cref="KiCadDocument"/>. What is here is specific to a board.
    /// </remarks>
    public class Board : KiCadDocument
    {
        /// <summary>Creates a new Board proxy.</summary>
        /// <param name="client">KiCad IPC client.</param>
        /// <param name="document">Document specifier for the board.</param>
        public Board(KiCadIPCClient client, DocumentSpecifier document) : base(client, document)
        {
        }

        /// <summary>The board file name.</summary>
        public string Name => Document.BoardFilename;

        /// <summary>The project this board belongs to.</summary>
        /// <returns>Project object.</returns>
        public Project GetProject()
        {
            return new Project(Client, Document);
        }

        // ----------------------------------------------------------------------------- layers

        /// <summary>The active layer in the editor.</summary>
        public async ValueTask<BoardLayer> GetActiveLayer(CancellationToken cancellationToken = default)
        {
            var response = await Send<BoardLayerResponse>(new GetActiveLayer { Board = Document }, cancellationToken);
            return response.Layer;
        }

        /// <summary>Sets the active layer in the editor.</summary>
        /// <param name="layer">Layer to make active.</param>
        public async ValueTask SetActiveLayer(BoardLayer layer, CancellationToken cancellationToken = default)
        {
            await Send(new SetActiveLayer { Board = Document, Layer = layer }, cancellationToken);
        }

        /// <summary>Refills every zone on the board.</summary>
        public async ValueTask RefillZones(CancellationToken cancellationToken = default)
        {
            await Send(new RefillZones { Board = Document }, cancellationToken);
        }

        // ---------------------------------------------------------------------- embedded files

        /// <summary>The files embedded in the board.</summary>
        /// <returns>Each file as KiCad holds it. Decode one with <see cref="EmbeddedFileCodec.Unpack"/>.</returns>
        /// <remarks>KiCad 10.0.7 and later.</remarks>
        public async ValueTask<EmbeddedFile[]> GetEmbeddedFiles(CancellationToken cancellationToken = default)
        {
            var response = await Send<EmbeddedFiles>(new GetEmbeddedFiles { Board = Document }, cancellationToken);
            return response.Files.ToArray();
        }

        /// <summary>Appends files to the board's embedded files.</summary>
        /// <param name="files">Files as <see cref="EmbeddedFileCodec.Pack"/> builds them.</param>
        /// <remarks>KiCad 10.0.7 and later. KiCad rejects the whole request if any file's hash does not match its content.</remarks>
        public ValueTask AddEmbeddedFiles(params EmbeddedFile[] files) => AddEmbeddedFiles(default, files);

        /// <summary>Same, with a cancellation token. The token comes first because a <c>params</c> array must be last.</summary>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <param name="files">As above.</param>
        public async ValueTask AddEmbeddedFiles(CancellationToken cancellationToken, params EmbeddedFile[] files)
        {
            var command = new AddEmbeddedFiles { Board = Document, Files = new EmbeddedFiles() };
            command.Files.Files.AddRange(files);
            await Send(command, cancellationToken);
        }

        /// <summary>Embeds one file, given its raw content.</summary>
        /// <param name="name">The name KiCad lists it under.</param>
        /// <param name="content">The raw bytes.</param>
        /// <param name="type">What KiCad treats it as; <see cref="EmbeddedFileType.EftDatasheet"/> for a datasheet.</param>
        /// <remarks>KiCad 10.0.7 and later. Compresses, encodes and hashes through <see cref="EmbeddedFileCodec.Pack"/>.</remarks>
        public ValueTask AddEmbeddedFile(string name, byte[] content, EmbeddedFileType type = EmbeddedFileType.EftOther, CancellationToken cancellationToken = default)
        {
            return AddEmbeddedFiles(cancellationToken, EmbeddedFileCodec.Pack(name, content, type));
        }

        /// <summary>Replaces the board's embedded files.</summary>
        /// <param name="files">The new set. Empty removes every embedded file.</param>
        /// <remarks>KiCad 10.0.7 and later.</remarks>
        public ValueTask SetEmbeddedFiles(params EmbeddedFile[] files) => SetEmbeddedFiles(default, files);

        /// <summary>Same, with a cancellation token. The token comes first because a <c>params</c> array must be last.</summary>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <param name="files">As above.</param>
        public async ValueTask SetEmbeddedFiles(CancellationToken cancellationToken, params EmbeddedFile[] files)
        {
            var command = new SetEmbeddedFiles { Board = Document, Files = new EmbeddedFiles() };
            command.Files.Files.AddRange(files);
            await Send(command, cancellationToken);
        }

        // ------------------------------------------------------------------------ design rules

        /// <summary>The board's design rules: constraints, predefined sizes, defaults, DRC severities and exclusions.</summary>
        /// <returns>The rules, plus whether the board's custom rules text is valid.</returns>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public async ValueTask<BoardDesignRulesResponse> GetDesignRules(CancellationToken cancellationToken = default)
        {
            return await Send<BoardDesignRulesResponse>(new GetBoardDesignRules { Board = Document }, cancellationToken);
        }

        /// <summary>Sets the board's design rules.</summary>
        /// <param name="rules">The rules to apply.</param>
        /// <returns>The rules as KiCad holds them after the change.</returns>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public async ValueTask<BoardDesignRulesResponse> SetDesignRules(BoardDesignRules rules, CancellationToken cancellationToken = default)
        {
            return await Send<BoardDesignRulesResponse>(new SetBoardDesignRules { Board = Document, Rules = rules }, cancellationToken);
        }

        /// <summary>The board's custom design rules, parsed.</summary>
        /// <returns>The rules, their validity, and the parser's error text when they are invalid.</returns>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public async ValueTask<CustomRulesResponse> GetCustomDesignRules(CancellationToken cancellationToken = default)
        {
            return await Send<CustomRulesResponse>(new GetCustomDesignRules { Board = Document }, cancellationToken);
        }

        /// <summary>Replaces the board's custom design rules.</summary>
        /// <param name="rules">The rules to write.</param>
        /// <returns>The rules as KiCad parsed them back, with their validity and any error text.</returns>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public ValueTask<CustomRulesResponse> SetCustomDesignRules(params CustomRule[] rules) => SetCustomDesignRules(default, rules);

        /// <summary>Same, with a cancellation token. The token comes first because a <c>params</c> array must be last.</summary>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <param name="rules">As above.</param>
        /// <returns>As above.</returns>
        public async ValueTask<CustomRulesResponse> SetCustomDesignRules(CancellationToken cancellationToken, params CustomRule[] rules)
        {
            var command = new SetCustomDesignRules { Board = Document };
            command.Rules.AddRange(rules);
            return await Send<CustomRulesResponse>(command, cancellationToken);
        }

        // ----------------------------------------------------------------------------- netlist

        /// <summary>Updates the board from a netlist exported by the schematic.</summary>
        /// <param name="netlistPath">Path to the netlist file.</param>
        /// <param name="dryRun">Report what would change without changing the board.</param>
        /// <param name="matchMode">How schematic symbols are matched to footprints: by unique id, or by reference designator.</param>
        /// <param name="deleteExtraFootprints">Remove footprints the netlist does not mention.</param>
        /// <param name="updateFootprints">Replace footprints whose library link changed.</param>
        /// <param name="transferGroups">Carry schematic groups over to the board.</param>
        /// <param name="overrideLocks">Change locked footprints too.</param>
        /// <returns>Error, warning and new-footprint counts, and KiCad's report text.</returns>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public async ValueTask<ImportNetlistResponse> ImportNetlist(
            string netlistPath,
            bool dryRun = false,
            NetlistMatchMode matchMode = NetlistMatchMode.NmmUuid,
            bool deleteExtraFootprints = false,
            bool updateFootprints = false,
            bool transferGroups = false,
            bool overrideLocks = false,
            CancellationToken cancellationToken = default)
        {
            var command = new ImportNetlist
            {
                Board = Document,
                NetlistPath = netlistPath,
                DryRun = dryRun,
                MatchMode = matchMode,
                DeleteExtraFootprints = deleteExtraFootprints,
                UpdateFootprints = updateFootprints,
                TransferGroups = transferGroups,
                OverrideLocks = overrideLocks,
            };
            return await Send<ImportNetlistResponse>(command, cancellationToken);
        }

        // ---------------------------------------------------------------------------- plotting

        /// <summary>The board's saved plot settings, the ones the Plot dialog and the plot jobs start from.</summary>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public async ValueTask<BoardPlotSettings> GetPlotSettings(CancellationToken cancellationToken = default)
        {
            var response = await Send<BoardPlotSettingsResponse>(new GetBoardPlotSettings { Board = Document }, cancellationToken);
            return response.PlotSettings;
        }

        /// <summary>Sets the board's saved plot settings.</summary>
        /// <param name="plotSettings">The settings to store.</param>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public async ValueTask SetPlotSettings(BoardPlotSettings plotSettings, CancellationToken cancellationToken = default)
        {
            await Send(new SetBoardPlotSettings { Board = Document, PlotSettings = plotSettings }, cancellationToken);
        }

        // --------------------------------------------------------------------------- libraries

        /// <summary>Places a footprint from a library on the board.</summary>
        /// <param name="libraryId">The footprint, as library nickname and entry name.</param>
        /// <param name="position">Where to place it.</param>
        /// <param name="orientation">Its rotation, or null for the library default.</param>
        /// <param name="layer">Front or back copper, or null for the front. Placing on the back flips it.</param>
        /// <returns>The placed footprint.</returns>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public async ValueTask<FootprintInstance> PlaceFootprintFromLibrary(
            LibraryIdentifier libraryId,
            Vector2 position,
            Angle? orientation = null,
            BoardLayer? layer = null,
            CancellationToken cancellationToken = default)
        {
            var command = new PlaceFootprintFromLibrary
            {
                Header = Header(),
                LibId = libraryId,
                Position = position,
            };
            if (orientation is not null)
            {
                command.Orientation = orientation;
            }

            if (layer is { } value)
            {
                command.Layer = value;
            }

            var response = await Send<PlaceFromLibraryResponse>(command, cancellationToken);
            if (!response.Item.TryUnpack<FootprintInstance>(out var footprint))
            {
                throw new ApiException($"KiCad placed the footprint but returned a {response.Item.TypeUrl}, not a FootprintInstance.");
            }

            return footprint;
        }
    }
}
