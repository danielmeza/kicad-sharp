namespace KiCadSharp.Tests;

/// <summary>
/// KiCad's versioned path variables (#82). KiCad names them after the major version it runs,
/// <c>KICAD%d_%s</c> in <c>ENV_VAR::GetVersionedEnvVarName</c>, so KiCad 10 sets <c>KICAD10_*</c>
/// where these getters used to read <c>KICAD9_*</c> only.
/// </summary>
/// <remarks>
/// Almost everything here goes through the internal overload, which takes the environment as an
/// argument, so no test depends on, or changes, what the process running the tests has set. The one
/// exception is <see cref="EachGetter_ReadsItsOwnVariable_FromTheProcessEnvironment"/>, which
/// needs the real environment to test the public path end to end.
/// </remarks>
public class KiCadEnvironmentTests
{
    /// <summary>
    /// Every <c>KICAD*</c> variable KiCad 10.0.6 gave an IPC API plugin, measured: pcbnew in
    /// <c>ghcr.io/danielmeza/orbion-kicad-release:10.0.6</c> with a fresh <c>HOME</c>, running an
    /// <c>exec</c> plugin whose action dumps its environment. The container had set none of them.
    /// <c>KICAD9_SYMBOL_DIR</c> was planted in <c>kicad_common.json</c>'s <c>environment.vars</c>,
    /// where a settings migration from KiCad 9 would leave it, and KiCad 10 exported it too.
    /// </summary>
    private static readonly Dictionary<string, string?> KiCad10_0_6PluginEnvironment = new()
    {
        ["KICAD10_3DMODEL_DIR"] = "/usr/share/kicad/3dmodels/",
        ["KICAD10_3RD_PARTY"] = "/home/probe/.local/share/kicad/10.0/3rdparty/",
        ["KICAD10_DESIGN_BLOCK_DIR"] = "/usr/share/kicad/blocks/",
        ["KICAD10_FOOTPRINT_DIR"] = "/usr/share/kicad/footprints/",
        ["KICAD10_SYMBOL_DIR"] = "/usr/share/kicad/symbols/",
        ["KICAD10_TEMPLATE_DIR"] = "/usr/share/kicad/template/",
        ["KICAD9_SYMBOL_DIR"] = "/planted/kicad9/symbols",
        ["KICAD_API_SOCKET"] = "ipc:///tmp/kicad/api.sock",
        ["KICAD_API_TOKEN"] = "01598d93-0000-0000-0000-000000000000",
        ["KICAD_USER_TEMPLATE_DIR"] = "/home/probe/.local/share/kicad/10.0/template/",
        ["KIPRJMOD"] = "/project",
    };

    /// <summary>The six base names KiCad 10.0.6 sets (<c>COMMON_SETTINGS::InitializeEnvironment</c>).</summary>
    public static TheoryData<string> BaseNames =>
        ["3DMODEL_DIR", "3RD_PARTY", "DESIGN_BLOCK_DIR", "FOOTPRINT_DIR", "SYMBOL_DIR", "TEMPLATE_DIR"];

    private static string? Newest(string baseName, IDictionary<string, string?> environment) =>
        KiCadEnvironment.GetVersionedVariable(baseName, null, environment, StringComparison.Ordinal);

    private static string? ForVersion(string baseName, uint majorVersion, IDictionary<string, string?> environment) =>
        KiCadEnvironment.GetVersionedVariable(baseName, majorVersion, environment, StringComparison.Ordinal);

    // ------------------------------------------------------------------- one KiCad, both, or none

    [Theory]
    [MemberData(nameof(BaseNames))]
    public void KiCad9Only_ReadsTheKiCad9Name(string baseName)
    {
        var environment = new Dictionary<string, string?> { [$"KICAD9_{baseName}"] = "/kicad9" };

        Assert.Equal("/kicad9", Newest(baseName, environment));
    }

    [Theory]
    [MemberData(nameof(BaseNames))]
    public void KiCad10Only_ReadsTheKiCad10Name(string baseName)
    {
        // The bug: with only the KiCad 10 names set, every getter returned null.
        var environment = new Dictionary<string, string?> { [$"KICAD10_{baseName}"] = "/kicad10" };

        Assert.Equal("/kicad10", Newest(baseName, environment));
    }

    [Theory]
    [MemberData(nameof(BaseNames))]
    public void BothSet_ReadsTheNewest(string baseName)
    {
        // Compared as numbers: as strings, "KICAD9_" sorts after "KICAD10_".
        var environment = new Dictionary<string, string?>
        {
            [$"KICAD9_{baseName}"] = "/kicad9",
            [$"KICAD10_{baseName}"] = "/kicad10",
        };

        Assert.Equal("/kicad10", Newest(baseName, environment));
    }

    [Theory]
    [MemberData(nameof(BaseNames))]
    public void NoneSet_IsNull(string baseName)
    {
        // Everything else KiCad puts in a plugin's environment, and a versioned name for a different
        // variable, but not this one.
        var environment = new Dictionary<string, string?>
        {
            ["KICAD_API_SOCKET"] = "ipc:///tmp/kicad/api.sock",
            ["KICAD_API_TOKEN"] = "token",
            ["KICAD_USER_TEMPLATE_DIR"] = "/templates",
            ["KIPRJMOD"] = "/project",
            [baseName == "SYMBOL_DIR" ? "KICAD10_FOOTPRINT_DIR" : "KICAD10_SYMBOL_DIR"] = "/other",
        };

        Assert.Null(Newest(baseName, environment));
        Assert.Null(Newest(baseName, new Dictionary<string, string?>()));
    }

    [Fact]
    public void TheEnvironmentKiCad10_0_6GivesAPlugin_ReadsAsKiCad10()
    {
        Assert.Equal("/usr/share/kicad/3dmodels/", Newest("3DMODEL_DIR", KiCad10_0_6PluginEnvironment));
        Assert.Equal("/usr/share/kicad/footprints/", Newest("FOOTPRINT_DIR", KiCad10_0_6PluginEnvironment));
        Assert.Equal("/usr/share/kicad/blocks/", Newest("DESIGN_BLOCK_DIR", KiCad10_0_6PluginEnvironment));
        Assert.Equal("/usr/share/kicad/template/", Newest("TEMPLATE_DIR", KiCad10_0_6PluginEnvironment));
        Assert.Equal("/home/probe/.local/share/kicad/10.0/3rdparty/", Newest("3RD_PARTY", KiCad10_0_6PluginEnvironment));

        // Not the KICAD9_SYMBOL_DIR carried over beside it.
        Assert.Equal("/usr/share/kicad/symbols/", Newest("SYMBOL_DIR", KiCad10_0_6PluginEnvironment));
    }

    // ------------------------------------------------------------------------------ the lookup rules

    [Fact]
    public void ANewerKiCad_IsReadWithoutAChange()
    {
        // Nothing names version 10: KiCad 11 will set KICAD11_*, and this reads it.
        var environment = new Dictionary<string, string?>
        {
            ["KICAD9_SYMBOL_DIR"] = "/kicad9",
            ["KICAD10_SYMBOL_DIR"] = "/kicad10",
            ["KICAD11_SYMBOL_DIR"] = "/kicad11",
        };

        Assert.Equal("/kicad11", Newest("SYMBOL_DIR", environment));
    }

    [Fact]
    public void AnEmptyValue_CountsAsUnset_AndTheNextVersionDownIsRead()
    {
        var environment = new Dictionary<string, string?>
        {
            ["KICAD9_SYMBOL_DIR"] = "/kicad9",
            ["KICAD10_SYMBOL_DIR"] = "",
        };

        Assert.Equal("/kicad9", Newest("SYMBOL_DIR", environment));
        Assert.Null(Newest("SYMBOL_DIR", new Dictionary<string, string?> { ["KICAD10_SYMBOL_DIR"] = "" }));
        Assert.Null(Newest("SYMBOL_DIR", new Dictionary<string, string?> { ["KICAD10_SYMBOL_DIR"] = null }));
    }

    [Theory]
    [InlineData("KICAD_SYMBOL_DIR")]        // KiCad 5's unversioned name
    [InlineData("KICADX_SYMBOL_DIR")]       // not a number
    [InlineData("KICAD1O_SYMBOL_DIR")]      // a letter O among the digits
    [InlineData("KICAD-10_SYMBOL_DIR")]     // a sign
    [InlineData("KICAD10SYMBOL_DIR")]       // no separator
    [InlineData("KICAD10_SYMBOL_DIRS")]     // a longer base name
    [InlineData("KICAD10_OLD_SYMBOL_DIR")]  // text between the version and the base name
    [InlineData("MYKICAD10_SYMBOL_DIR")]    // text before the prefix
    [InlineData("KICAD99999999999_SYMBOL_DIR")] // too large to be a version
    public void ANameThatIsNotKiCadsVersionedName_IsNotRead(string name)
    {
        var environment = new Dictionary<string, string?> { [name] = "/wrong" };

        Assert.Null(Newest("SYMBOL_DIR", environment));
    }

    [Fact]
    public void ABaseNameThatEndsAnother_DoesNotMatchIt()
    {
        // "DIR" ends "KICAD10_SYMBOL_DIR", but "10_SYMBOL" is not a version.
        var environment = new Dictionary<string, string?> { ["KICAD10_SYMBOL_DIR"] = "/symbols" };

        Assert.Null(Newest("DIR", environment));
        Assert.Null(Newest("PARTY", new Dictionary<string, string?> { ["KICAD10_3RD_PARTY"] = "/3rd" }));
    }

    [Fact]
    public void NameCase_MattersExceptWhereTheEnvironmentIgnoresIt()
    {
        var environment = new Dictionary<string, string?> { ["kicad10_symbol_dir"] = "/lower" };

        // Linux and macOS: a different variable, which KiCad would not read either.
        Assert.Null(Newest("SYMBOL_DIR", environment));

        // Windows: the same variable.
        Assert.Equal("/lower", KiCadEnvironment.GetVersionedVariable("SYMBOL_DIR", null, environment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ATie_IsSettledByName_NotByEnumerationOrder()
    {
        // KICAD010 and KICAD10 are both version 10; only a hand-made name can do this.
        var forward = new List<KeyValuePair<string, string?>>
        {
            new("KICAD10_SYMBOL_DIR", "/ten"),
            new("KICAD010_SYMBOL_DIR", "/zero-ten"),
        };
        var backward = Enumerable.Reverse(forward).ToList();

        Assert.Equal("/zero-ten", KiCadEnvironment.GetVersionedVariable("SYMBOL_DIR", null, forward, StringComparison.Ordinal));
        Assert.Equal("/zero-ten", KiCadEnvironment.GetVersionedVariable("SYMBOL_DIR", null, backward, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnEmptyBaseName_Throws(string? baseName)
    {
        Assert.ThrowsAny<ArgumentException>(() => KiCadEnvironment.GetVersionedVariable(baseName!));
        Assert.ThrowsAny<ArgumentException>(() => KiCadEnvironment.GetVersionedVariable(baseName!, 10));
    }

    // --------------------------------------------------------------------- a known running version

    [Fact]
    public void AKnownVersion_IsReadEvenWhenANewerOneIsSet()
    {
        var environment = new Dictionary<string, string?>
        {
            ["KICAD9_SYMBOL_DIR"] = "/kicad9",
            ["KICAD10_SYMBOL_DIR"] = "/kicad10",
        };

        Assert.Equal("/kicad9", ForVersion("SYMBOL_DIR", 9, environment));
        Assert.Equal("/kicad10", ForVersion("SYMBOL_DIR", 10, environment));
    }

    [Fact]
    public void AKnownVersionThatIsNotSet_FallsBackToTheNewest()
    {
        var environment = new Dictionary<string, string?>
        {
            ["KICAD8_SYMBOL_DIR"] = "/kicad8",
            ["KICAD9_SYMBOL_DIR"] = "/kicad9",
            ["KICAD10_SYMBOL_DIR"] = "",
        };

        Assert.Equal("/kicad9", ForVersion("SYMBOL_DIR", 10, environment));
        Assert.Null(ForVersion("SYMBOL_DIR", 10, new Dictionary<string, string?>()));
    }

    // ------------------------------------------------------------------ the public getters, end to end

    public static TheoryData<string, string> Getters => new()
    {
        { nameof(KiCadEnvironment.GetModelsDirectory), "3DMODEL_DIR" },
        { nameof(KiCadEnvironment.GetFootprintDirectory), "FOOTPRINT_DIR" },
        { nameof(KiCadEnvironment.GetSymbolDirectory), "SYMBOL_DIR" },
        { nameof(KiCadEnvironment.GetDesignBlockDirectory), "DESIGN_BLOCK_DIR" },
        { nameof(KiCadEnvironment.GetTemplateDirectory), "TEMPLATE_DIR" },
        { nameof(KiCadEnvironment.GetThirdPartyDirectory), "3RD_PARTY" },
    };

    [Theory]
    [MemberData(nameof(Getters))]
    public void EachGetter_ReadsItsOwnVariable_FromTheProcessEnvironment(string getter, string baseName)
    {
        // Through the real environment, so the enumeration and the name comparison are covered too.
        // Version 9999 is newer than anything a real KiCad on this machine could have set, so the
        // result is this test's whatever else is there. No other test reads these names.
        var name = $"KICAD9999_{baseName}";
        var value = $"/kicadsharp-test/{Guid.NewGuid():N}";
        var read = typeof(KiCadEnvironment).GetMethod(getter, Type.EmptyTypes)!;

        Environment.SetEnvironmentVariable(name, value);
        try
        {
            Assert.Equal(value, read.Invoke(null, null));
            Assert.Equal(value, KiCadEnvironment.GetVersionedVariable(baseName));
            Assert.Equal(value, KiCadEnvironment.GetVersionedVariable(baseName, 9999));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
