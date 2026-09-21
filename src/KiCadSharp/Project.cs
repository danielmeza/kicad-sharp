using Kiapi.Common.Commands;
using Kiapi.Common.Project;
using Kiapi.Common.Types;

namespace KiCadSharp
{
    /// <summary>
    /// Represents a KiCad project
    /// </summary>
    public class Project : KiCadIPCProxy
    {
        private readonly DocumentSpecifier _document;
        
        /// <summary>
        /// Creates a new Project proxy
        /// </summary>
        /// <param name="client">KiCad IPC client</param>
        /// <param name="document">A document in the project, or a project specifier. Only its project is kept.</param>
        /// <remarks>
        /// Builds its own specifier: type <see cref="DocumentType.DoctypeProject"/> and the
        /// project, nothing else. It used to flip <c>Type</c> on the specifier it was given, which
        /// is the one the <see cref="Board"/> that created it still sends, so every board command
        /// after <c>GetProject()</c> went out typed as a project. And it kept the board file name
        /// in the message: MEASURED against KiCad master at 6e93fd64, a project-typed specifier that still
        /// names a board file is answered "the requested document ... is not open" by pcbnew's
        /// handler, while KiCad's own project handler wants a bare <c>DOCTYPE_PROJECT</c>.
        /// </remarks>
        public Project(KiCadIPCClient client, DocumentSpecifier document) : base(client)
        {
            ArgumentNullException.ThrowIfNull(document);

            _document = new DocumentSpecifier { Type = DocumentType.DoctypeProject };
            if (document.Project is not null)
            {
                _document.Project = document.Project.Clone();
                _document.Project.Path = WithTrailingSeparator(_document.Project.Path);
            }
        }

        /// <summary>
        /// KiCad's project path, the way KiCad itself compares it.
        /// </summary>
        /// <remarks>
        /// MEASURED against KiCad master at 6e93fd64: <c>validateProject</c> in
        /// <c>api_handler_common.cpp</c> compares the request's path with
        /// <c>PROJECT::GetProjectPath()</c>, which by its own contract ends with a separator, and
        /// eeschema's <c>PackProject</c> reports that same form. pcbnew's <c>GetOpenDocuments</c>
        /// reports <c>GetProjectDirectory()</c> instead, without the separator, so sending back
        /// exactly what pcbnew said gets "the requested project ... is not open at path /project".
        /// Adding the separator is what the other two agree on, and stays right if pcbnew is
        /// changed to match them.
        /// </remarks>
        private static string WithTrailingSeparator(string path)
        {
            if (string.IsNullOrEmpty(path) || path.EndsWith('/') || path.EndsWith('\\'))
            {
                return path;
            }

            return path + (path.Contains('\\') && !path.Contains('/') ? '\\' : '/');
        }
        
        /// <summary>
        /// Gets the document specifier for this project
        /// </summary>
        public DocumentSpecifier Document => _document;
        
        /// <summary>
        /// Gets the name of the project
        /// </summary>
        public string Name => _document.Project.Name;
        
        /// <summary>
        /// Gets the path of the project
        /// </summary>
        public string Path => _document.Project.Path;

        /// <summary>
        /// Gets the net classes defined in the project
        /// </summary>
        /// <returns>Array of net classes</returns>
        /// <remarks>
        /// Names the project in the request. KiCad 11.0 added that field and says a request
        /// without it will be deprecated; a 10.0.x KiCad ignores it.
        /// </remarks>
        public async ValueTask<NetClass[]> GetNetClasses(CancellationToken cancellationToken = default)
        {
            var command = new GetNetClasses { Project = _document.Project };
            var response = await Send<NetClassesResponse>(command, cancellationToken);
            return [.. response.NetClasses];
        }
        
        /// <summary>
        /// Sets the net classes in the project
        /// </summary>
        /// <param name="netClasses">Net classes to set</param>
        /// <param name="mergeMode">How to merge with existing net classes</param>
        public async ValueTask SetNetClasses(NetClass[] netClasses, MapMergeMode mergeMode = MapMergeMode.MmmMerge, CancellationToken cancellationToken = default)
        {
            var command = new SetNetClasses
            {
                MergeMode = mergeMode,
                Project = _document.Project,
            };
            command.NetClasses.AddRange(netClasses);
            await Send(command, cancellationToken);
        }

        /// <summary>Which net classes each net is assigned to, directly and by pattern.</summary>
        /// <returns>The per-net assignments and the wildcard patterns.</returns>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public async ValueTask<NetClassAssignmentsResponse> GetNetClassAssignments(CancellationToken cancellationToken = default)
        {
            return await Send<NetClassAssignmentsResponse>(new GetNetClassAssignments { Project = _document.Project }, cancellationToken);
        }

        /// <summary>Assigns nets to net classes, directly and by pattern.</summary>
        /// <param name="assignments">Per-net assignments. In merge mode, a net with an empty list loses all its assignments.</param>
        /// <param name="patternAssignments">Wildcard patterns. In merge mode, a pattern with an empty net class is removed.</param>
        /// <param name="mergeMode">Merge into, or replace, what the project has.</param>
        /// <remarks>KiCad 10.99 and later.</remarks>
        public async ValueTask SetNetClassAssignments(
            IEnumerable<NetClassAssignment> assignments,
            IEnumerable<NetClassPatternAssignment>? patternAssignments = null,
            MapMergeMode mergeMode = MapMergeMode.MmmMerge,
            CancellationToken cancellationToken = default)
        {
            var command = new SetNetClassAssignments
            {
                Project = _document.Project,
                MergeMode = mergeMode,
            };
            command.Assignments.AddRange(assignments);
            if (patternAssignments is not null)
            {
                command.PatternAssignments.AddRange(patternAssignments);
            }

            await Send(command, cancellationToken);
        }

        /// <summary>
        /// Expands text variables in a string
        /// </summary>
        /// <param name="text">Text containing variables to expand</param>
        /// <param name="expandEnvironmentVariables">Also expand environment variables such as <c>${KIPRJMOD}</c>, after the text variables. KiCad 10.0.7 and later; ignored before.</param>
        /// <returns>Text with variables expanded</returns>
        public async ValueTask<string> ExpandTextVariables(string text, bool expandEnvironmentVariables = false, CancellationToken cancellationToken = default)
        {
            var expanded = await ExpandTextVariables([text], expandEnvironmentVariables, cancellationToken);
            return expanded.Length > 0 ? expanded[0] : string.Empty;
        }
        
        /// <summary>
        /// Expands text variables in multiple strings
        /// </summary>
        /// <param name="texts">Array of texts containing variables to expand</param>
        /// <param name="expandEnvironmentVariables">Also expand environment variables such as <c>${KIPRJMOD}</c>, after the text variables. KiCad 10.0.7 and later; ignored before.</param>
        /// <returns>Array of texts with variables expanded</returns>
        public async ValueTask<string[]> ExpandTextVariables(string[] texts, bool expandEnvironmentVariables = false, CancellationToken cancellationToken = default)
        {
            var command = new ExpandTextVariables
            {
                Document = _document,
                ExpandEnvVars = expandEnvironmentVariables,
            };
            command.Text.AddRange(texts);
            
            var response = await Send<ExpandTextVariablesResponse>(command, cancellationToken);
            return response.Text.ToArray();
        }
        
        /// <summary>
        /// Gets the text variables defined in the project
        /// </summary>
        /// <returns>Text variables object</returns>
        public async ValueTask<TextVariables> GetTextVariables(CancellationToken cancellationToken = default)
        {
            var command = new GetTextVariables
            {
                Document = _document
            };
            return await Send<TextVariables>(command, cancellationToken);
        }
        
        /// <summary>
        /// Sets the text variables in the project
        /// </summary>
        /// <param name="variables">Text variables to set</param>
        /// <param name="mergeMode">How to merge with existing variables</param>
        public async ValueTask SetTextVariables(TextVariables variables, MapMergeMode mergeMode = MapMergeMode.MmmMerge, CancellationToken cancellationToken = default)
        {
            var command = new SetTextVariables
            {
                Document = _document,
                Variables = variables,
                MergeMode = mergeMode
            };
            await Send(command, cancellationToken);
        }
    }
}
