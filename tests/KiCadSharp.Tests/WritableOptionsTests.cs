using System.Text.Json;

using KiCadSharp.Settings;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace KiCadSharp.Tests;

/// <summary>
/// <see cref="WritableOptions{T}"/> over a real file and a real configuration root.
/// </summary>
/// <remarks>
/// These went in with the move from <c>Newtonsoft.Json</c> to <c>System.Text.Json</c>, because the
/// two libraries disagree by default on three things this class depends on: property-name casing
/// (Newtonsoft is case-insensitive, STJ is not), non-ASCII escaping, and how unknown properties
/// survive a round trip.
/// </remarks>
public class WritableOptionsTests
{
    public sealed class KiCadOptions
    {
        public string? PipeName { get; set; }

        public int Timeout { get; set; }

        public bool Enabled { get; set; }

        public string? Label { get; set; }
    }

    [Fact]
    public void Update_WritesTheSectionBack()
    {
        var (options, path) = Build("""
            {
              "KiCad": { "PipeName": "ipc:///tmp/a", "Timeout": 5, "Enabled": false }
            }
            """);

        options.Update(o =>
        {
            o.PipeName = "ipc:///tmp/b";
            o.Timeout = 30;
            o.Enabled = true;
        });

        var section = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("KiCad");
        Assert.Equal("ipc:///tmp/b", section.GetProperty("PipeName").GetString());
        Assert.Equal(30, section.GetProperty("Timeout").GetInt32());
        Assert.True(section.GetProperty("Enabled").GetBoolean());
    }

    [Fact]
    public void Update_LeavesEveryOtherSectionAlone()
    {
        var (options, path) = Build("""
            {
              "Logging": { "LogLevel": { "Default": "Information" } },
              "KiCad": { "Timeout": 5 },
              "AllowedHosts": "*"
            }
            """);

        options.Update(o => o.Timeout = 30);

        var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        Assert.Equal("Information", root.GetProperty("Logging").GetProperty("LogLevel").GetProperty("Default").GetString());
        Assert.Equal("*", root.GetProperty("AllowedHosts").GetString());
        Assert.Equal(30, root.GetProperty("KiCad").GetProperty("Timeout").GetInt32());
    }

    [Fact]
    public void Update_ReadsASectionWrittenInADifferentCase()
    {
        // Microsoft.Extensions.Configuration binds case-insensitively, so a file written with
        // camelCase keys binds fine. System.Text.Json does not, by default — deserialising that
        // section with the stock options would silently return an empty object and the update would
        // wipe every value it did not set.
        var (options, path) = Build("""
            {
              "KiCad": { "pipeName": "ipc:///tmp/a", "timeout": 5 }
            }
            """);

        options.Update(o => o.Enabled = true);

        var section = JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("KiCad");
        Assert.Equal("ipc:///tmp/a", section.GetProperty("PipeName").GetString());
        Assert.Equal(5, section.GetProperty("Timeout").GetInt32());
        Assert.True(section.GetProperty("Enabled").GetBoolean());
    }

    [Fact]
    public void Update_DoesNotEscapeTextThatDoesNotNeedIt()
    {
        // STJ's default encoder escapes +, &, < and every non-ASCII character. A settings file is
        // read by people.
        var (options, path) = Build("""{ "KiCad": { } }""");

        options.Update(o => o.Label = "Ampère & Ohm <5Ω>");

        var text = File.ReadAllText(path);
        Assert.Contains("Ampère & Ohm <5Ω>", text);
        Assert.DoesNotContain("\\u", text);
    }

    [Fact]
    public void Update_CreatesTheSectionWhenTheFileDoesNotHaveIt()
    {
        var (options, path) = Build("""{ "AllowedHosts": "*" }""");

        options.Update(o => o.Timeout = 30);

        var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        Assert.Equal(30, root.GetProperty("KiCad").GetProperty("Timeout").GetInt32());
        Assert.Equal("*", root.GetProperty("AllowedHosts").GetString());
    }

    [Fact]
    public void Update_WritesIndentedJson()
    {
        var (options, path) = Build("""{ "KiCad": { "Timeout": 5 } }""");

        options.Update(o => o.Timeout = 30);

        Assert.Contains('\n', File.ReadAllText(path));
    }

    [Fact]
    public void Update_ReloadsConfiguration()
    {
        var (options, path) = Build("""{ "KiCad": { "Timeout": 5 } }""");
        var configuration = options.Configuration;

        Assert.Equal("5", configuration["KiCad:Timeout"]);
        options.Update(o => o.Timeout = 30);
        Assert.Equal("30", configuration["KiCad:Timeout"]);
    }

    [Fact]
    public void Update_RejectsAFileThatIsNotAJsonObject()
    {
        // Written after the configuration root is built, because the JSON configuration provider
        // refuses a non-object outright and this test is about what Update does.
        var (options, path) = Build("""{ "KiCad": { } }""");
        File.WriteAllText(path, "[1, 2, 3]");

        var error = Assert.Throws<InvalidOperationException>(() => options.Update(_ => { }));
        Assert.Contains("does not contain a JSON object", error.Message);
    }

    private static (TestableOptions Options, string Path) Build(string json)
    {
        var directory = TestData.NewScratchDirectory();
        var path = Path.Combine(directory, "appsettings.json");
        File.WriteAllText(path, json);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(directory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var options = new TestableOptions(
            new TestEnvironment(directory),
            new TestMonitor(configuration),
            configuration,
            "KiCad",
            "appsettings.json");

        return (options, path);
    }

    private sealed class TestableOptions(IHostEnvironment environment, IOptionsMonitor<KiCadOptions> monitor, IConfigurationRoot configuration, string section, string file)
        : WritableOptions<KiCadOptions>(environment, monitor, configuration, section, file)
    {
        public IConfigurationRoot Configuration { get; } = configuration;
    }

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "tests";

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(root);

        public string ContentRootPath { get; set; } = root;

        public string EnvironmentName { get; set; } = "Test";
    }

    private sealed class TestMonitor(IConfiguration configuration) : IOptionsMonitor<KiCadOptions>
    {
        public KiCadOptions CurrentValue => Get(null);

        public KiCadOptions Get(string? name)
        {
            var options = new KiCadOptions();
            configuration.GetSection("KiCad").Bind(options);
            return options;
        }

        public IDisposable OnChange(Action<KiCadOptions, string?> listener) => new Noop();

        private sealed class Noop : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
