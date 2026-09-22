using Google.Protobuf;

using Kiapi.Common.Commands;
using Kiapi.Common.Project;
using Kiapi.Common.Types;

namespace KiCadSharp
{
    /// <summary>
    /// Represents a connection to a running KiCad instance
    /// </summary>
    public class KiCad : KiCadIPCProxy
    {
        /// <summary>
        /// Creates a new KiCad connection with the specified client
        /// </summary>
        /// <param name="client">KiCad IPC client for communication with KiCad</param>
        public KiCad(KiCadIPCClient client) : base(client)
        {

        }

        /// <summary>
        /// Pings the KiCad instance to check if the connection is alive
        /// </summary>
        public async ValueTask Ping(CancellationToken cancellationToken = default)
        {
            await Send(new Ping(), cancellationToken);
        }

        /// <summary>
        /// Gets the version of the connected KiCad instance
        /// </summary>
        /// <returns>KiCad version information</returns>
        public async ValueTask<KiCadVersion> GetVersion(CancellationToken cancellationToken = default)
        {
            var response = await Send<GetVersionResponse>(new GetVersion(), cancellationToken);
            return new KiCadVersion(response.Version);
        }

        /// <summary>
        /// Gets the path to a KiCad binary
        /// </summary>
        /// <param name="binaryName">Name of the binary (e.g., "kicad-cli")</param>
        /// <returns>Full path to the binary</returns>
        public async ValueTask<string> GetKiCadBinaryPath(string binaryName, CancellationToken cancellationToken = default)
        {
            var command = new GetKiCadBinaryPath
            {
                BinaryName = binaryName
            };
            var response = await Send<PathResponse>(command, cancellationToken);
            return response.Path;
        }

        /// <summary>
        /// Gets a writable path for plugin settings
        /// </summary>
        /// <param name="identifier">Plugin identifier</param>
        /// <returns>Path where plugin settings can be stored</returns>
        public async ValueTask<string> GetPluginSettingsPath(string identifier, CancellationToken cancellationToken = default)
        {
            var command = new GetPluginSettingsPath
            {
                Identifier = identifier
            };
            var response = await Send<StringResponse>(command, cancellationToken);
            return response.Response;
        }

        /// <summary>The well-known paths KiCad uses: user plugins, templates and settings, and the stock libraries.</summary>
        /// <returns>Each path KiCad reported, by kind. A path may not exist on disk.</returns>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public async ValueTask<IReadOnlyDictionary<PathType, string>> GetPaths(CancellationToken cancellationToken = default)
        {
            var response = await Send<GetPathsResponse>(new GetPaths(), cancellationToken);
            return response.Paths.ToDictionary(entry => entry.Type, entry => entry.Path);
        }

        /// <summary>
        /// Gets all open documents of the specified type
        /// </summary>
        /// <param name="documentType">Type of documents to retrieve</param>
        /// <returns>List of document specifiers</returns>
        public async ValueTask<DocumentSpecifier[]> GetOpenDocuments(DocumentType documentType, CancellationToken cancellationToken = default)
        {
            var command = new GetOpenDocuments
            {

                Type = documentType
            };
            var response = await Send<GetOpenDocumentsResponse>(command, cancellationToken);
            return response.Documents.ToArray();
        }

        /// <summary>
        /// Gets a board object for the first open board
        /// </summary>
        /// <returns>Board object</returns>
        /// <exception cref="ApiException">Thrown if no board is open</exception>
        public async ValueTask<Board> GetBoard(CancellationToken cancellationToken = default)
        {
            var docs = await GetOpenDocuments(DocumentType.DoctypePcb, cancellationToken);
            if (docs.Length == 0)
            {
                throw new ApiException("Expected to be able to retrieve at least one board");
            }
            return new Board(Client, docs[0]);
        }

        /// <summary>A schematic proxy for the first open schematic.</summary>
        /// <returns>Schematic object</returns>
        /// <exception cref="ApiException">No schematic is open.</exception>
        /// <remarks>KiCad 10.99 and later: on 10.0.x eeschema does not answer the API at all.</remarks>
        public async ValueTask<Schematic> GetSchematic(CancellationToken cancellationToken = default)
        {
            var docs = await GetOpenDocuments(DocumentType.DoctypeSchematic, cancellationToken);
            if (docs.Length == 0)
            {
                throw new ApiException("Expected to be able to retrieve at least one schematic");
            }
            return new Schematic(Client, docs[0]);
        }

        /// <summary>A schematic proxy for a document KiCad already reported.</summary>
        /// <param name="document">A <see cref="DocumentType.DoctypeSchematic"/> specifier, with the sheet path commands should apply to.</param>
        /// <returns>Schematic object</returns>
        /// <remarks>Synchronous: it binds a handle and sends nothing.</remarks>
        public Schematic GetSchematic(DocumentSpecifier document)
        {
            return new Schematic(Client, document);
        }

        /// <summary>
        /// Gets a project object for the specified document
        /// </summary>
        /// <param name="document">Document specifier</param>
        /// <returns>Project object</returns>
        /// <remarks>
        /// Synchronous on purpose, and not an oversight: the caller already has the document, so
        /// this only binds a handle and sends nothing. The parameterless overload has to ask KiCad
        /// which board is open first, which is why that one is a <see cref="ValueTask{TResult}"/>.
        /// </remarks>
        public Project GetProject(DocumentSpecifier document)
        {
            return new Project(Client, document);
        }

        /// <summary>
        /// Gets a project object for the board currently open in KiCad.
        /// </summary>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>Project object</returns>
        public async ValueTask<Project> GetProject(CancellationToken cancellationToken = default)
        {
            var board = await GetBoard(cancellationToken);
            return board.GetProject();
        }

        // -------------------------------------------------------------------- opening documents

        /// <summary>Opens a document in KiCad.</summary>
        /// <param name="type">Board or schematic.</param>
        /// <param name="path">Path to the file.</param>
        /// <returns>The specifier of the opened document, to hand to <see cref="Board"/>, <see cref="Schematic"/> or <see cref="GetProject(DocumentSpecifier)"/>.</returns>
        /// <remarks>
        /// KiCad 10.99 and later, and only in <c>kicad-cli api-server</c> mode. The file has to be
        /// in the loaded project, or no project may be loaded; close documents to switch projects.
        /// </remarks>
        public async ValueTask<DocumentSpecifier> OpenDocument(DocumentType type, string path, CancellationToken cancellationToken = default)
        {
            var response = await Send<OpenDocumentResponse>(new OpenDocument { Type = type, Path = path }, cancellationToken);
            return response.Document;
        }

        /// <summary>Creates a new, unsaved document in memory, and a project for it if none exists.</summary>
        /// <param name="type">Board or schematic.</param>
        /// <param name="path">Where the document will be saved.</param>
        /// <returns>The specifier of the new document.</returns>
        /// <remarks>KiCad 10.99 and later. Fails while the current document has unsaved changes.</remarks>
        public async ValueTask<DocumentSpecifier> CreateDocument(DocumentType type, string path, CancellationToken cancellationToken = default)
        {
            var response = await Send<OpenDocumentResponse>(new CreateDocument { Type = type, Path = path }, cancellationToken);
            return response.Document;
        }

        /// <summary>Closes a document.</summary>
        /// <param name="document">The document to close.</param>
        /// <remarks>KiCad 10.99 and later, and only in <c>kicad-cli api-server</c> mode.</remarks>
        public async ValueTask CloseDocument(DocumentSpecifier document, CancellationToken cancellationToken = default)
        {
            await Send(new CloseDocument { Document = document }, cancellationToken);
        }

        /// <summary>Closes every open document.</summary>
        /// <param name="force">Close documents with unsaved changes too.</param>
        /// <remarks>KiCad 10.99 and later, and only in <c>kicad-cli api-server</c> mode.</remarks>
        public async ValueTask CloseAllDocuments(bool force = false, CancellationToken cancellationToken = default)
        {
            await Send(new CloseAllDocuments { Force = force }, cancellationToken);
        }

        /// <summary>Opens a library item in its editor: a footprint in the footprint editor, a symbol in the symbol editor.</summary>
        /// <param name="type"><see cref="DocumentType.DoctypeFootprint"/> or <see cref="DocumentType.DoctypeSymbol"/>.</param>
        /// <param name="identifier">The item, as library nickname and entry name.</param>
        /// <remarks>KiCad 10.99 and later. On master only the footprint editor handles it.</remarks>
        public async ValueTask OpenLibraryItem(DocumentType type, LibraryIdentifier identifier, CancellationToken cancellationToken = default)
        {
            await Send(new OpenLibraryItem { Type = type, Identifier = identifier }, cancellationToken);
        }

        // ---------------------------------------------------------------------------- libraries

        /// <summary>Reads a library table.</summary>
        /// <param name="type">Symbol, footprint or design-block libraries.</param>
        /// <param name="scope">The global table, the project table, or both.</param>
        /// <param name="substituted">Return URIs with environment variables expanded.</param>
        /// <returns>The table file and its rows.</returns>
        /// <remarks>
        /// Declared for KiCad 11.0. As of master at the commit this library is pinned to, KiCad
        /// registers no handler for it and answers <c>AS_UNHANDLED</c>; see docs/ipc.md.
        /// </remarks>
        public async ValueTask<LibraryTableResponse> GetLibraryTable(LibraryType type, LibraryTableScope scope = LibraryTableScope.LtsBoth, bool substituted = false, CancellationToken cancellationToken = default)
        {
            var command = new GetLibraryTable { Type = type, Scope = scope, Substituted = substituted };
            return await Send<LibraryTableResponse>(command, cancellationToken);
        }

        /// <summary>Adds a row to a library table.</summary>
        /// <param name="entry">The row. Its nickname must be unique in the table.</param>
        /// <param name="scope">The global or the project table; not both.</param>
        /// <returns>KiCad's status: OK, already exists, read-only, or an error with a message.</returns>
        /// <remarks>Declared for KiCad 11.0; unhandled on master at the pinned commit (see docs/ipc.md).</remarks>
        public async ValueTask<LibraryCommandStatus> AddLibraryTableEntry(LibraryTableEntry entry, LibraryTableScope scope, CancellationToken cancellationToken = default)
        {
            return await Send<LibraryCommandStatus>(new AddLibraryTableEntry { Entry = entry, Scope = scope }, cancellationToken);
        }

        /// <summary>Adds a library to a library table, given its location.</summary>
        /// <param name="path">The library URI, as it goes in the table. Environment variables such as <c>${KIPRJMOD}</c> are kept, not expanded.</param>
        /// <param name="nickname">The nickname the library is referred to by.</param>
        /// <param name="type">Symbol, footprint or design-block library.</param>
        /// <param name="scope">The global table (default) or the project table.</param>
        /// <param name="description">A description for the table, if any.</param>
        /// <returns>KiCad's status.</returns>
        /// <remarks>
        /// A shorthand for <see cref="AddLibraryTableEntry"/>. Declared for KiCad 11.0; unhandled on
        /// master at the pinned commit (see docs/ipc.md).
        /// </remarks>
        public ValueTask<LibraryCommandStatus> ImportLibrary(string path, string nickname, LibraryType type, LibraryTableScope scope = LibraryTableScope.LtsGlobal, string description = "", CancellationToken cancellationToken = default)
        {
            var entry = new LibraryTableEntry
            {
                Nickname = nickname,
                Uri = path,
                Type = type,
                Scope = scope,
                Description = description,
            };
            return AddLibraryTableEntry(entry, scope, cancellationToken);
        }

        /// <summary>Changes a row in a library table, matched by nickname.</summary>
        /// <param name="entry">The row's new content.</param>
        /// <param name="scope">The global or the project table; not both.</param>
        /// <returns>KiCad's status.</returns>
        /// <remarks>Declared for KiCad 11.0; unhandled on master at the pinned commit (see docs/ipc.md).</remarks>
        public async ValueTask<LibraryCommandStatus> UpdateLibraryTableEntry(LibraryTableEntry entry, LibraryTableScope scope, CancellationToken cancellationToken = default)
        {
            return await Send<LibraryCommandStatus>(new UpdateLibraryTableEntry { Entry = entry, Scope = scope }, cancellationToken);
        }

        /// <summary>Removes a row from a library table.</summary>
        /// <param name="type">Which table type the row is in.</param>
        /// <param name="nickname">The row's nickname.</param>
        /// <param name="scope">The global or the project table; not both.</param>
        /// <returns>KiCad's status.</returns>
        /// <remarks>Declared for KiCad 11.0; unhandled on master at the pinned commit (see docs/ipc.md).</remarks>
        public async ValueTask<LibraryCommandStatus> DeleteLibraryTableEntry(LibraryType type, string nickname, LibraryTableScope scope, CancellationToken cancellationToken = default)
        {
            return await Send<LibraryCommandStatus>(new DeleteLibraryTableEntry { Type = type, Nickname = nickname, Scope = scope }, cancellationToken);
        }

        /// <summary>The load status of each library in the tables.</summary>
        /// <param name="scope">Which tables to look at.</param>
        /// <param name="types">Which library types; none means all.</param>
        /// <returns>Each row with whether it is loading, loaded, invalid, or failed with a message.</returns>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public async ValueTask<LibraryStatusEntry[]> GetLibraryStatuses(LibraryTableScope scope = LibraryTableScope.LtsBoth, CancellationToken cancellationToken = default, params LibraryType[] types)
        {
            var command = new GetLibraryStatuses { Scope = scope };
            command.Types_.AddRange(types);

            var response = await Send<LibraryStatusResponse>(command, cancellationToken);
            return response.Libraries.ToArray();
        }

        /// <summary>Reloads libraries from disk. Returns at once; the reload runs in the background.</summary>
        /// <param name="type">Which library type.</param>
        /// <param name="scope">The global or the project table; not both.</param>
        /// <param name="nicknames">Which libraries; none means every library of that type in that table.</param>
        /// <returns>KiCad's status for starting the reload.</returns>
        /// <remarks>KiCad 10.99 and later. Poll <see cref="GetLibraryStatuses"/> to see it finish.</remarks>
        public async ValueTask<LibraryCommandStatus> ReloadLibrary(LibraryType type, LibraryTableScope scope, CancellationToken cancellationToken = default, params string[] nicknames)
        {
            var command = new ReloadLibrary { Type = type, Scope = scope };
            command.Nickname.AddRange(nicknames);
            return await Send<LibraryCommandStatus>(command, cancellationToken);
        }

        /// <summary>Starts loading every library in the tables. Returns at once; the load runs in the background.</summary>
        /// <param name="types">Which library types; none means all.</param>
        /// <returns>KiCad's status for starting the load.</returns>
        /// <remarks>KiCad 10.99 and later. A no-op against the GUI, which preloads its libraries; meant for <c>kicad-cli api-server</c>.</remarks>
        public ValueTask<LibraryCommandStatus> LoadAllLibraries(params LibraryType[] types) => LoadAllLibraries(default, types);

        /// <summary>Same, with a cancellation token. The token comes first because a <c>params</c> array must be last.</summary>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <param name="types">As above.</param>
        /// <returns>As above.</returns>
        public async ValueTask<LibraryCommandStatus> LoadAllLibraries(CancellationToken cancellationToken, params LibraryType[] types)
        {
            var command = new LoadAllLibraries();
            command.Type.AddRange(types);
            return await Send<LibraryCommandStatus>(command, cancellationToken);
        }

        /// <summary>Lists what a library contains.</summary>
        /// <param name="type">Which library type.</param>
        /// <param name="nicknames">The libraries to list.</param>
        /// <returns>The identifier of every item in them.</returns>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public ValueTask<LibraryIdentifier[]> GetLibraryItems(LibraryType type, params string[] nicknames) => GetLibraryItems(type, default, nicknames);

        /// <summary>Same, with a cancellation token. The token comes before the <c>params</c> array because that must be last.</summary>
        /// <param name="type">As above.</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <param name="nicknames">As above.</param>
        /// <returns>As above.</returns>
        public async ValueTask<LibraryIdentifier[]> GetLibraryItems(LibraryType type, CancellationToken cancellationToken, params string[] nicknames)
        {
            var command = new GetLibraryItems { Type = type };
            command.Nickname.AddRange(nicknames);

            var response = await Send<LibraryItemsResponse>(command, cancellationToken);
            return response.Items.ToArray();
        }

        /// <summary>Reads the full content of library items.</summary>
        /// <param name="type">Which library type.</param>
        /// <param name="document">A document whose project's libraries should be visible too, or null for the global tables only.</param>
        /// <param name="itemIds">The items to read.</param>
        /// <returns>The items, unpacked: a <c>Footprint</c> or a <c>SchematicSymbol</c> each.</returns>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public async ValueTask<IMessage[]> GetItemsFromLibrary(LibraryType type, DocumentSpecifier? document, CancellationToken cancellationToken, params LibraryIdentifier[] itemIds)
        {
            var command = new GetItemsFromLibrary { Type = type };
            if (document is not null)
            {
                command.Document = document;
            }

            command.ItemIds.AddRange(itemIds);

            var response = await Send<GetItemsResponse>(command, cancellationToken);
            return response.Items.ToArray();
        }

        /// <summary>Same, without a cancellation token or document context.</summary>
        /// <param name="type">As above.</param>
        /// <param name="itemIds">As above.</param>
        /// <returns>As above.</returns>
        public ValueTask<IMessage[]> GetItemsFromLibrary(LibraryType type, params LibraryIdentifier[] itemIds) => GetItemsFromLibrary(type, null, default, itemIds);

        /// <summary>Searches the libraries by item name.</summary>
        /// <param name="type">Which library type.</param>
        /// <param name="query">A substring or glob, matched the way KiCad's library chooser filter matches.</param>
        /// <returns>The identifiers of the matching items.</returns>
        /// <remarks>Declared for KiCad 11.0; unhandled on master at the pinned commit (see docs/ipc.md).</remarks>
        public async ValueTask<LibraryIdentifier[]> SearchLibraries(LibraryType type, string query, CancellationToken cancellationToken = default)
        {
            var response = await Send<SearchLibrariesResponse>(new SearchLibraries { Type = type, Query = query }, cancellationToken);
            return response.Items.ToArray();
        }

        // -------------------------------------------------------------------------- cross-probe

        /// <summary>Selects, in the editor this client is talking to, the items another editor selected.</summary>
        /// <param name="context">Whether the user clicked (implicit) or asked to select in the other window (explicit).</param>
        /// <param name="mode">Items only, or items and their nets.</param>
        /// <param name="focusItem">The item to centre on, if any.</param>
        /// <param name="items">The items to select, named by reference, pad or sheet path.</param>
        /// <returns>KiCad's status, and a message when the selection was refused or something was not found.</returns>
        /// <remarks>KiCad 10.99 and later. This is what pcbnew and eeschema send each other on a click.</remarks>
        public async ValueTask<SyncSelectionResponse> SyncSelection(SyncSelectionContext context, SyncSelectionMode mode, SelectionSpec? focusItem, CancellationToken cancellationToken, params SelectionSpec[] items)
        {
            var command = new SyncSelection { Context = context, Mode = mode };
            if (focusItem is not null)
            {
                command.FocusItem = focusItem;
            }

            command.Items.AddRange(items);
            return await Send<SyncSelectionResponse>(command, cancellationToken);
        }

        /// <summary>Same, without a cancellation token.</summary>
        /// <param name="context">As above.</param>
        /// <param name="mode">As above.</param>
        /// <param name="focusItem">As above.</param>
        /// <param name="items">As above.</param>
        /// <returns>As above.</returns>
        public ValueTask<SyncSelectionResponse> SyncSelection(SyncSelectionContext context, SyncSelectionMode mode, SelectionSpec? focusItem, params SelectionSpec[] items) =>
            SyncSelection(context, mode, focusItem, default, items);

        /// <summary>Highlights nets in the editor.</summary>
        /// <param name="netNames">The nets. None clears the highlight.</param>
        /// <returns>KiCad's status.</returns>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public ValueTask<HighlightNetsResponse> HighlightNets(params string[] netNames) => HighlightNets(default, netNames);

        /// <summary>Same, with a cancellation token. The token comes first because a <c>params</c> array must be last.</summary>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <param name="netNames">As above.</param>
        /// <returns>As above.</returns>
        public async ValueTask<HighlightNetsResponse> HighlightNets(CancellationToken cancellationToken, params string[] netNames)
        {
            var command = new HighlightNets();
            command.NetName.AddRange(netNames);
            return await Send<HighlightNetsResponse>(command, cancellationToken);
        }

        /// <summary>Centres the editor on one item named by reference, pad or sheet path.</summary>
        /// <param name="focusItem">The item.</param>
        /// <returns>KiCad's status.</returns>
        /// <remarks>Declared on master; no editor registers a handler for it at the pinned commit. <see cref="KiCadDocument.FocusOnItems"/>, by id, is handled.</remarks>
        public async ValueTask<FocusOnItemResponse> FocusOnItem(SelectionSpec focusItem, CancellationToken cancellationToken = default)
        {
            return await Send<FocusOnItemResponse>(new FocusOnItem { FocusItem = focusItem }, cancellationToken);
        }

        /// <summary>Tells a standalone editor where another editor's API socket is, so the two can cross-probe.</summary>
        /// <param name="frameType">Which editor is announcing itself.</param>
        /// <param name="socketPath">Its API socket.</param>
        /// <param name="apiToken">Its API token.</param>
        /// <returns>KiCad's status.</returns>
        /// <remarks>
        /// KiCad marks this internal and says it may go without notice: it exists so two standalone
        /// editors can find each other, and KiCad plans to drop standalone mode. Wrapped for
        /// completeness; a plugin has no reason to send it.
        /// </remarks>
        public async ValueTask<CrossProbeAnnounceResponse> CrossProbeAnnounce(FrameType frameType, string socketPath, string apiToken, CancellationToken cancellationToken = default)
        {
            var command = new CrossProbeAnnounce { FrameType = frameType, SocketPath = socketPath, ApiToken = apiToken };
            return await Send<CrossProbeAnnounceResponse>(command, cancellationToken);
        }

        // ------------------------------------------------------------------------------ editor

        /// <summary>
        /// Runs a KiCad tool action
        /// </summary>
        /// <param name="actionName">Name of the action to run</param>
        /// <returns>Status of the action</returns>
        public async ValueTask<RunActionResponse> RunAction(string actionName, CancellationToken cancellationToken = default)
        {
            var command = new RunAction
            {
                Action = actionName
            };
            return await Send<RunActionResponse>(command, cancellationToken);
        }

        /// <summary>
        /// Refreshes the specified KiCad frame
        /// </summary>
        /// <param name="frameType">Type of frame to refresh</param>
        public async ValueTask RefreshEditor(FrameType frameType, CancellationToken cancellationToken = default)
        {
            var command = new RefreshEditor
            {
                Frame = frameType
            };
            await Send(command, cancellationToken);
        }

        /// <summary>The text variables of the project the open board belongs to.</summary>
        /// <returns>Name to value.</returns>
        /// <remarks>
        /// Asks KiCad which board is open and reads its project's variables, because KiCad master
        /// insists on a named project for this command ("a project name and path must be
        /// specified"; MEASURED) and a connection has no project of its own. With a
        /// <see cref="Project"/> in hand, its <see cref="Project.GetTextVariables"/> is one round
        /// trip fewer.
        /// </remarks>
        public async ValueTask<IDictionary<string, string>> GetTextVariables(CancellationToken cancellationToken = default)
        {
            var project = await GetProject(cancellationToken);
            var variables = await project.GetTextVariables(cancellationToken);
            return variables.Variables.ToDictionary();
        }
    }

    /// <summary>
    /// Represents KiCad version information
    /// </summary>
    public class KiCadVersion
    {
        /// <summary>
        /// Major version number
        /// </summary>
        public uint Major { get; }

        /// <summary>
        /// Minor version number
        /// </summary>
        public uint Minor { get; }

        /// <summary>
        /// Patch version number
        /// </summary>
        public uint Patch { get; }

        /// <summary>
        /// Full version string
        /// </summary>
        public string FullVersion { get; }

        /// <summary>
        /// Creates a new KiCad version from the proto version
        /// </summary>
        /// <param name="version">Proto version object</param>
        public KiCadVersion(Kiapi.Common.Types.KiCadVersion version)
        {
            Major = version.Major;
            Minor = version.Minor;
            Patch = version.Patch;
            FullVersion = version.FullVersion;
        }

        /// <summary>Creates a version from its numbers, for comparisons and tests.</summary>
        /// <param name="major">Major version.</param>
        /// <param name="minor">Minor version.</param>
        /// <param name="patch">Patch version.</param>
        /// <param name="fullVersion">The full version string, if known.</param>
        public KiCadVersion(uint major, uint minor, uint patch, string fullVersion = "")
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            FullVersion = fullVersion;
        }

        /// <summary>Whether this KiCad is the given version or newer.</summary>
        /// <param name="major">Major version.</param>
        /// <param name="minor">Minor version.</param>
        /// <param name="patch">Patch version.</param>
        /// <returns>True when this version is at least <paramref name="major"/>.<paramref name="minor"/>.<paramref name="patch"/>.</returns>
        public bool IsAtLeast(uint major, uint minor, uint patch = 0)
        {
            if (Major != major)
            {
                return Major > major;
            }

            if (Minor != minor)
            {
                return Minor > minor;
            }

            return Patch >= patch;
        }

        /// <summary>
        /// Whether this KiCad is a development build of the next major version: KiCad master
        /// reports itself as <c>X.99.0</c> (10.99.0 while 11.0 is in development).
        /// </summary>
        public bool IsDevelopmentBuild => Minor == 99;

        /// <summary>Embedded files, design variants and the document header on commits: KiCad 10.0.7 and later.</summary>
        public bool SupportsEmbeddedFiles => IsAtLeast(10, 0, 7);

        /// <summary>Design variants over IPC: KiCad 10.0.7 and later.</summary>
        public bool SupportsVariants => IsAtLeast(10, 0, 7);

        /// <summary>The schematic API: KiCad 10.99 (the 11.0 line) and later. On 10.0.x eeschema answers nothing.</summary>
        public bool SupportsSchematic => IsAtLeast(10, 99);

        /// <summary>Library statuses, reloads and item queries: KiCad 10.99 and later. The table-editing commands are declared but unhandled there.</summary>
        public bool SupportsLibraryCommands => IsAtLeast(10, 99);

        /// <summary>Export jobs, design rules, plot settings, netlist import, page settings, cross-probe and document open/close: KiCad 10.99 and later.</summary>
        public bool SupportsJobs => IsAtLeast(10, 99);

        /// <summary>
        /// Returns a string representation of the version
        /// </summary>
        public override string ToString()
        {
            return $"{Major}.{Minor}.{Patch} ({FullVersion})";
        }
    }
}
