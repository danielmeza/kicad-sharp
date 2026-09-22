using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Kiapi.Common.Commands;
using Kiapi.Common.Types;

namespace KiCadSharp
{
    /// <summary>
    /// A document open in a KiCad editor, over IPC: what <see cref="Board"/> and
    /// <see cref="Schematic"/> have in common.
    /// </summary>
    /// <remarks>
    /// Everything here is a command from <c>editor_commands.proto</c>, <c>project_commands.proto</c>,
    /// <c>variant_commands.proto</c> or the job protos, which KiCad handles the same way for a board
    /// and for a schematic. Each one carries <see cref="Document"/>, so a caller never has to build
    /// a <see cref="DocumentSpecifier"/> or an <see cref="ItemHeader"/> by hand.
    /// </remarks>
    public abstract class KiCadDocument : KiCadIPCProxy
    {
        /// <param name="client">KiCad IPC client.</param>
        /// <param name="document">Document specifier every command is sent for.</param>
        protected KiCadDocument(KiCadIPCClient client, DocumentSpecifier document) : base(client)
        {
            Document = document;
        }

        /// <summary>The document specifier every command on this proxy is sent for.</summary>
        public DocumentSpecifier Document { get; }

        /// <summary>An <see cref="ItemHeader"/> naming <see cref="Document"/>, for the commands that take one.</summary>
        protected ItemHeader Header() => new() { Document = Document };

        // ------------------------------------------------------------------------------ files

        /// <summary>Saves the document.</summary>
        public async ValueTask Save(CancellationToken cancellationToken = default)
        {
            await Send(new SaveDocument { Document = Document }, cancellationToken);
        }

        /// <summary>Saves a copy of the document to a new file.</summary>
        /// <param name="filename">Path to save to.</param>
        /// <param name="overwrite">Whether to overwrite an existing file.</param>
        /// <param name="includeProject">Whether to save the project files alongside.</param>
        public async ValueTask SaveAs(string filename, bool overwrite = false, bool includeProject = true, CancellationToken cancellationToken = default)
        {
            var command = new SaveCopyOfDocument
            {
                Document = Document,
                Path = filename,
                Options = new SaveOptions
                {
                    Overwrite = overwrite,
                    IncludeProject = includeProject,
                },
            };
            await Send(command, cancellationToken);
        }

        /// <summary>Reverts the document to its last saved state.</summary>
        public async ValueTask Revert(CancellationToken cancellationToken = default)
        {
            await Send(new RevertDocument { Document = Document }, cancellationToken);
        }

        /// <summary>The document in its file format, as KiCad would save it.</summary>
        /// <returns>The file content.</returns>
        public async ValueTask<string> GetAsString(CancellationToken cancellationToken = default)
        {
            var response = await Send<SavedDocumentResponse>(new SaveDocumentToString { Document = Document }, cancellationToken);
            return response.Contents;
        }

        /// <summary>Whether the document has unsaved changes.</summary>
        /// <returns>KiCad's answer; <see cref="DocumentModifiedState.DmsUnknown"/> when it could not tell.</returns>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public async ValueTask<DocumentModifiedState> GetModifiedState(CancellationToken cancellationToken = default)
        {
            var response = await Send<GetDocumentModifiedStateResponse>(new GetDocumentModifiedState { Document = Document }, cancellationToken);
            return response.State;
        }

        // ---------------------------------------------------------------------------- commits

        /// <summary>Begins a commit transaction on the document.</summary>
        /// <returns>The commit, to pass to <see cref="PushCommit"/> or <see cref="DropCommit"/>.</returns>
        /// <remarks>
        /// The request names the document. KiCad 10.0.7 introduced that field and says a
        /// <c>BeginCommit</c> without it will be deprecated; a 10.0.6 KiCad ignores it.
        /// </remarks>
        public async ValueTask<Commit> BeginCommit(CancellationToken cancellationToken = default)
        {
            var response = await Send<BeginCommitResponse>(new BeginCommit { Header = Header() }, cancellationToken);
            return new Commit(response.Id);
        }

        /// <summary>Pushes the changes made during a commit transaction.</summary>
        /// <param name="commit">The commit from <see cref="BeginCommit"/>.</param>
        /// <param name="message">Optional undo-history message.</param>
        public async ValueTask PushCommit(Commit commit, string message = "", CancellationToken cancellationToken = default)
        {
            var command = new EndCommit
            {
                Id = commit.Id,
                Action = CommitAction.CmaCommit,
                Message = message,
                Header = Header(),
            };
            await Send<EndCommitResponse>(command, cancellationToken);
        }

        /// <summary>Drops the changes made during a commit transaction.</summary>
        /// <param name="commit">The commit from <see cref="BeginCommit"/>.</param>
        public async ValueTask DropCommit(Commit commit, CancellationToken cancellationToken = default)
        {
            var command = new EndCommit
            {
                Id = commit.Id,
                Action = CommitAction.CmaDrop,
                Header = Header(),
            };
            await Send<EndCommitResponse>(command, cancellationToken);
        }

        // ------------------------------------------------------------------------------ items

        /// <summary>Gets the items of the given types.</summary>
        /// <param name="types">Which item types to return.</param>
        /// <returns>The items, each unpacked to its own message type.</returns>
        public ValueTask<IMessage[]> GetItems(params KiCadObjectType[] types) => GetItems(default, types);

        /// <summary>Same, with a cancellation token. The token comes first because a <c>params</c> array must be last.</summary>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <param name="types">As above.</param>
        /// <returns>As above.</returns>
        public async ValueTask<IMessage[]> GetItems(CancellationToken cancellationToken, params KiCadObjectType[] types)
        {
            var command = new GetItems { Header = Header() };
            command.Types_.AddRange(types);

            var response = await Send<GetItemsResponse>(command, cancellationToken);
            return response.Items.ToArray();
        }

        /// <summary>Gets the current selection.</summary>
        /// <param name="types">Optional filter on item types.</param>
        /// <returns>The selected items.</returns>
        public ValueTask<IMessage[]> GetSelection(params KiCadObjectType[] types) => GetSelection(default, types);

        /// <summary>Same, with a cancellation token. The token comes first because a <c>params</c> array must be last.</summary>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <param name="types">As above.</param>
        /// <returns>As above.</returns>
        public async ValueTask<IMessage[]> GetSelection(CancellationToken cancellationToken, params KiCadObjectType[] types)
        {
            var command = new GetSelection { Header = Header() };
            if (types is { Length: > 0 })
            {
                command.Types_.AddRange(types);
            }

            var response = await Send<SelectionResponse>(command, cancellationToken);
            return response.Items.ToArray();
        }

        /// <summary>Clears the current selection.</summary>
        public async ValueTask ClearSelection(CancellationToken cancellationToken = default)
        {
            await Send(new ClearSelection { Header = Header() }, cancellationToken);
        }

        /// <summary>Creates items in the document.</summary>
        /// <param name="items">The items to create.</param>
        /// <returns>KiCad's response, with the created items and per-item status.</returns>
        public ValueTask<CreateItemsResponse> CreateItems(params IMessage[] items) => CreateItems(default, items);

        /// <summary>Same, with a cancellation token. The token comes first because a <c>params</c> array must be last.</summary>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <param name="items">As above.</param>
        /// <returns>As above.</returns>
        public async ValueTask<CreateItemsResponse> CreateItems(CancellationToken cancellationToken, params IMessage[] items)
        {
            var command = new CreateItems { Header = Header() };
            foreach (var item in items)
            {
                command.Items.Add(Any.Pack(item));
            }

            return await Send<CreateItemsResponse>(command, cancellationToken);
        }

        /// <summary>Updates items in the document.</summary>
        /// <param name="items">The items to update, matched by id.</param>
        /// <returns>KiCad's response, with the updated items and per-item status.</returns>
        public ValueTask<UpdateItemsResponse> UpdateItems(params IMessage[] items) => UpdateItems(default, items);

        /// <summary>Same, with a cancellation token. The token comes first because a <c>params</c> array must be last.</summary>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <param name="items">As above.</param>
        /// <returns>As above.</returns>
        public async ValueTask<UpdateItemsResponse> UpdateItems(CancellationToken cancellationToken, params IMessage[] items)
        {
            var command = new UpdateItems { Header = Header() };
            foreach (var item in items)
            {
                command.Items.Add(Any.Pack(item));
            }

            return await Send<UpdateItemsResponse>(command, cancellationToken);
        }

        /// <summary>Deletes items from the document.</summary>
        /// <param name="itemIds">The ids of the items to delete.</param>
        /// <returns>KiCad's response, with per-item status.</returns>
        public ValueTask<DeleteItemsResponse> DeleteItems(params KIID[] itemIds) => DeleteItems(default, itemIds);

        /// <summary>Same, with a cancellation token. The token comes first because a <c>params</c> array must be last.</summary>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <param name="itemIds">As above.</param>
        /// <returns>As above.</returns>
        public async ValueTask<DeleteItemsResponse> DeleteItems(CancellationToken cancellationToken, params KIID[] itemIds)
        {
            var command = new DeleteItems { Header = Header() };
            command.ItemIds.AddRange(itemIds);

            return await Send<DeleteItemsResponse>(command, cancellationToken);
        }

        /// <summary>Scrolls and zooms the editor so the given items are in view.</summary>
        /// <param name="items">The items to fit. Schematic items must all be on one sheet.</param>
        /// <param name="margin">Extra space around the items' bounding box, if any.</param>
        /// <remarks>
        /// KiCad 10.99 and later, and only in interactive mode while the editor is idle. Fails if any
        /// item is not in this document.
        /// </remarks>
        public async ValueTask FocusOnItems(IEnumerable<KIID> items, Distance? margin = null, CancellationToken cancellationToken = default)
        {
            var command = new FocusOnItems { Document = Document };
            command.Items.AddRange(items);
            if (margin is not null)
            {
                command.Margin = margin;
            }

            await Send(command, cancellationToken);
        }

        // --------------------------------------------------------------------- text variables

        /// <summary>Expands text variables in strings, resolved against this document.</summary>
        /// <param name="texts">Texts with <c>${VAR}</c> references.</param>
        /// <param name="expandEnvironmentVariables">Also expand environment variables such as <c>${KIPRJMOD}</c>, after the text variables. KiCad 10.0.7 and later; ignored before.</param>
        /// <returns>The texts, expanded, in the same order.</returns>
        /// <remarks>
        /// The editor's own resolver answers, so a board's variables and the project's are both
        /// in scope. This is the expansion that works on every KiCad: MEASURED, a pcbnew on
        /// 10.0.6 answers <see cref="Project.ExpandTextVariables(string, bool, CancellationToken)"/>
        /// first, with its own handler, and rejects the project document without letting KiCad's
        /// project handler see it; master lets it through. See docs/ipc.md.
        /// </remarks>
        public async ValueTask<string[]> ExpandTextVariables(IEnumerable<string> texts, bool expandEnvironmentVariables = false, CancellationToken cancellationToken = default)
        {
            var command = new ExpandTextVariables { Document = Document, ExpandEnvVars = expandEnvironmentVariables };
            command.Text.AddRange(texts);

            var response = await Send<ExpandTextVariablesResponse>(command, cancellationToken);
            return response.Text.ToArray();
        }

        /// <summary>Expands text variables in one string, resolved against this document.</summary>
        /// <param name="text">Text with <c>${VAR}</c> references.</param>
        /// <param name="expandEnvironmentVariables">As above.</param>
        /// <returns>The text, expanded.</returns>
        public async ValueTask<string> ExpandTextVariables(string text, bool expandEnvironmentVariables = false, CancellationToken cancellationToken = default)
        {
            var expanded = await ExpandTextVariables([text], expandEnvironmentVariables, cancellationToken);
            return expanded.Length > 0 ? expanded[0] : string.Empty;
        }

        // ------------------------------------------------------------------------------- page

        /// <summary>The document's page size, orientation and drawing sheet.</summary>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public async ValueTask<PageSettings> GetPageSettings(CancellationToken cancellationToken = default)
        {
            return await Send<PageSettings>(new GetPageSettings { Document = Document }, cancellationToken);
        }

        /// <summary>Sets the document's page size, orientation and drawing sheet.</summary>
        /// <param name="pageSettings">The settings to apply.</param>
        /// <returns>The settings as KiCad holds them after the change.</returns>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public async ValueTask<PageSettings> SetPageSettings(PageSettings pageSettings, CancellationToken cancellationToken = default)
        {
            var command = new SetPageSettings
            {
                Document = Document,
                PageSettings = pageSettings,
            };
            return await Send<PageSettings>(command, cancellationToken);
        }

        // --------------------------------------------------------------------------- variants

        /// <summary>The design variants defined on this document, not counting the default one.</summary>
        /// <remarks>
        /// KiCad 10.0.7 and later. A project's board and schematic each keep their own variant
        /// list, and KiCad does not force them to agree.
        /// </remarks>
        public async ValueTask<DesignVariant[]> GetVariants(CancellationToken cancellationToken = default)
        {
            var response = await Send<VariantsResponse>(new GetVariants { Document = Document }, cancellationToken);
            return response.Variants.ToArray();
        }

        /// <summary>Adds a design variant.</summary>
        /// <param name="name">Its name; KiCad rejects a name that already exists, ignoring case.</param>
        /// <param name="description">Its description, if any.</param>
        /// <remarks>KiCad 10.0.7 and later.</remarks>
        public async ValueTask AddVariant(string name, string? description = null, CancellationToken cancellationToken = default)
        {
            var command = new AddVariant { Document = Document, Name = name };
            if (description is not null)
            {
                command.Description = description;
            }

            await Send(command, cancellationToken);
        }

        /// <summary>Deletes a design variant and every per-item override recorded under it.</summary>
        /// <param name="name">The variant to delete. Deleting the current one makes the default variant current.</param>
        /// <remarks>KiCad 10.0.7 and later.</remarks>
        public async ValueTask DeleteVariant(string name, CancellationToken cancellationToken = default)
        {
            await Send(new DeleteVariant { Document = Document, Name = name }, cancellationToken);
        }

        /// <summary>Renames a design variant.</summary>
        /// <param name="oldName">The variant to rename.</param>
        /// <param name="newName">Its new name.</param>
        /// <remarks>KiCad 10.0.7 and later.</remarks>
        public async ValueTask RenameVariant(string oldName, string newName, CancellationToken cancellationToken = default)
        {
            await Send(new RenameVariant { Document = Document, OldName = oldName, NewName = newName }, cancellationToken);
        }

        /// <summary>Sets a design variant's description.</summary>
        /// <param name="name">The variant.</param>
        /// <param name="description">Its new description.</param>
        /// <remarks>KiCad 10.0.7 and later.</remarks>
        public async ValueTask SetVariantDescription(string name, string description, CancellationToken cancellationToken = default)
        {
            await Send(new SetVariantDescription { Document = Document, Name = name, Description = description }, cancellationToken);
        }

        /// <summary>Copies a design variant, overrides included.</summary>
        /// <param name="oldName">The variant to copy.</param>
        /// <param name="newName">The name of the copy.</param>
        /// <param name="newDescription">The description of the copy, if any.</param>
        /// <remarks>KiCad 10.0.7 and later.</remarks>
        public async ValueTask CopyVariant(string oldName, string newName, string? newDescription = null, CancellationToken cancellationToken = default)
        {
            var command = new CopyVariant { Document = Document, OldName = oldName, NewName = newName };
            if (newDescription is not null)
            {
                command.NewDescription = newDescription;
            }

            await Send(command, cancellationToken);
        }

        /// <summary>Makes a design variant the one the editor shows.</summary>
        /// <param name="name">The variant, or null for the default variant.</param>
        /// <remarks>KiCad 10.0.7 and later.</remarks>
        public async ValueTask SetCurrentVariant(string? name, CancellationToken cancellationToken = default)
        {
            var command = new SetCurrentVariant { Document = Document };
            if (!string.IsNullOrEmpty(name))
            {
                command.Name = name;
            }

            await Send(command, cancellationToken);
        }

        /// <summary>The design variant the editor currently shows.</summary>
        /// <returns>Its name, or null for the default variant.</returns>
        /// <remarks>KiCad 10.0.7 and later.</remarks>
        public async ValueTask<string?> GetCurrentVariant(CancellationToken cancellationToken = default)
        {
            var response = await Send<CurrentVariantResponse>(new GetCurrentVariant { Document = Document }, cancellationToken);
            return response.HasName ? response.Name : null;
        }

        // ------------------------------------------------------------------------------- jobs

        /// <summary>Runs an export job on this document.</summary>
        /// <param name="job">
        /// One of the <c>Run*Job*</c> messages from <c>board_jobs.proto</c> or
        /// <c>schematic_jobs.proto</c>, with its options filled in. Its <c>job_settings</c> field is
        /// set here: the document is this one, and the output path is <paramref name="outputPath"/>.
        /// </param>
        /// <param name="outputPath">Where the job writes. A file or a directory, depending on the job.</param>
        /// <returns>The job's status, the paths it wrote, and its message when there was a warning or error.</returns>
        /// <exception cref="ArgumentException"><paramref name="job"/> has no <c>job_settings</c> field, so it is not a job.</exception>
        /// <remarks>
        /// One method rather than one per job because the 22 job messages differ only in their
        /// options, and those options are the message. KiCad 10.99 and later.
        /// </remarks>
        public async ValueTask<RunJobResponse> RunJob(IMessage job, string outputPath, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(job);
            ArgumentException.ThrowIfNullOrEmpty(outputPath);

            var field = job.Descriptor.FindFieldByName("job_settings")
                ?? throw new ArgumentException($"{job.Descriptor.FullName} is not a job: it has no job_settings field.", nameof(job));

            field.Accessor.SetValue(job, new RunJobSettings { Document = Document, OutputPath = outputPath });
            return await Send<RunJobResponse>(job, cancellationToken);
        }
    }

    /// <summary>
    /// A commit transaction on a document.
    /// </summary>
    public class Commit
    {
        /// <summary>Wraps the id KiCad gave in <c>BeginCommitResponse</c>.</summary>
        /// <param name="id">The commit id.</param>
        public Commit(KIID id)
        {
            Id = id;
        }

        /// <summary>The id KiCad knows this commit by.</summary>
        public KIID Id { get; }
    }
}
