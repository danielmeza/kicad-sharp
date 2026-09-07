using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KiCadSharp.Settings
{
    /// <summary>
    /// An <see cref="IOptions{T}"/> that can also write its section back to the JSON file it was
    /// bound from.
    /// </summary>
    /// <typeparam name="T">The options type.</typeparam>
    public interface IWritableOptions<out T> : IOptions<T>
        where T : class, new()
    {
        /// <summary>
        /// Applies a change to the options and writes the whole file back, then reloads configuration.
        /// </summary>
        /// <param name="applyChanges">Mutates the section before it is written.</param>
        void Update(Action<T> applyChanges);
    }

    /// <summary>
    /// Writes one section of a JSON configuration file back to disk.
    /// </summary>
    /// <typeparam name="T">The options type.</typeparam>
    /// <remarks>
    /// Only the named section is replaced; every other property of the file is carried across as it
    /// was parsed, so unrelated settings survive an update.
    /// </remarks>
    public class WritableOptions<T> : IWritableOptions<T>
        where T : class, new()
    {
        /// <summary>
        /// How the section is read and written.
        /// </summary>
        /// <remarks>
        /// Both settings here restore behaviour that <c>Newtonsoft.Json</c> had by default and
        /// <c>System.Text.Json</c> does not. Case-insensitive matching, because
        /// <c>Microsoft.Extensions.Configuration</c> binds case-insensitively: reading a section
        /// back more strictly than the binder that produced it returns an empty object, and the
        /// update then writes away every value it did not set. The relaxed encoder, because the
        /// default one escapes <c>+</c>, <c>&amp;</c>, <c>&lt;</c> and every non-ASCII character,
        /// and a settings file is read by people. It is never served as HTML.
        /// </remarks>
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };

        private static readonly JsonDocumentOptions DocumentOptions = new()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        };

        private readonly IHostEnvironment _environment;
        private readonly IOptionsMonitor<T> _options;
        private readonly IConfigurationRoot _configuration;
        private readonly string _section;
        private readonly string _file;

        /// <summary>Creates a writable options accessor.</summary>
        /// <param name="environment">Supplies the content root the file is resolved against.</param>
        /// <param name="options">The bound options to read the current value from.</param>
        /// <param name="configuration">Reloaded after a successful write.</param>
        /// <param name="section">The top-level property of the file to replace.</param>
        /// <param name="file">The file, relative to the content root.</param>
        public WritableOptions(
            IHostEnvironment environment,
            IOptionsMonitor<T> options,
            IConfigurationRoot configuration,
            string section,
            string file)
        {
            _environment = environment;
            _options = options;
            _configuration = configuration;
            _section = section;
            _file = file;
        }

        /// <inheritdoc />
        public T Value => _options.CurrentValue;

        /// <summary>Gets a named instance of the options.</summary>
        /// <param name="name">The instance name.</param>
        /// <returns>The options.</returns>
        public T Get(string name) => _options.Get(name);

        /// <inheritdoc />
        public void Update(Action<T> applyChanges)
        {
            ArgumentNullException.ThrowIfNull(applyChanges);

            var fileProvider = _environment.ContentRootFileProvider;
            var fileInfo = fileProvider.GetFileInfo(_file);
            var physicalPath = fileInfo.PhysicalPath
                ?? throw new InvalidOperationException($"Settings file '{_file}' has no physical path; a non-physical file provider cannot be written back to.");

            var root = JsonNode.Parse(File.ReadAllText(physicalPath), NodeOptions, DocumentOptions) as JsonObject
                ?? throw new InvalidOperationException($"Settings file '{physicalPath}' does not contain a JSON object.");

            var section = root.TryGetPropertyValue(_section, out var existing) && existing is not null
                ? existing.Deserialize<T>(SerializerOptions) ?? new T()
                : Value ?? new T();

            applyChanges(section);

            root[_section] = JsonSerializer.SerializeToNode(section, SerializerOptions);
            File.WriteAllText(physicalPath, root.ToJsonString(SerializerOptions));
            _configuration.Reload();
        }
    }
}
