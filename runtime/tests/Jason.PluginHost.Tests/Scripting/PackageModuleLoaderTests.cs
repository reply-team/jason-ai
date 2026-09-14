using System.Globalization;
using Jason.PluginHost.Scripting;
using Jason.PluginHost.Sdk;
using Jason.PluginHost.Tests.Fixtures;
using Jint;
using Jint.Runtime.Modules;

namespace Jason.PluginHost.Tests.Scripting;

public sealed class PackageModuleLoaderTests
{
    private static Engine Create(string root) =>
        EngineFactory.Create(InvocationFactory.DefaultLimits, new PackageModuleLoader(root), CancellationToken.None);

    [Fact]
    public void A_relative_module_inside_the_package_is_loaded()
    {
        using var package = new TempPackage(
            "import { helper } from \"./modules/a.js\"; export const value = helper();",
            new Dictionary<string, string> { ["modules/a.js"] = "export function helper() { return 42; }" });

        var module = Create(package.Root).Modules.Import("./main.js");

        Assert.Equal(42, module.Get("value").AsNumber());
    }

    [Fact]
    public void A_module_above_the_package_root_never_resolves()
    {
        using var package = new TempPackage("import \"../outside.js\"; export const value = 1;");
        File.WriteAllText(Path.Combine(package.Outside, "outside.js"), "export const x = 1;");

        Assert.Throws<ModuleResolutionException>(() => Create(package.Root).Modules.Import("./main.js"));
    }

    [Theory]
    [InlineData("lodash", "a bare specifier names a package this host does not have")]
    [InlineData("./data.json", "a JSON module is not JavaScript")]
    [InlineData("https://example.test/module.js", "a URL is not a file in the package")]
    [InlineData("/absolute.js", "an absolute path leaves the package")]
    public void A_specifier_that_is_not_a_relative_script_in_the_package_is_refused(string specifier, string why)
    {
        using var package = new TempPackage(string.Create(CultureInfo.InvariantCulture, $"import \"{specifier}\"; export const value = 1;"));

        var refused = Assert.Throws<HostRuleException>(() => Create(package.Root).Modules.Import("./main.js"));

        Assert.Equal(OutcomeCodes.ModuleNotAllowed, refused.Code);
        Assert.Contains(specifier, refused.Message, StringComparison.Ordinal);
        Assert.NotEmpty(why);
    }

    [Fact]
    public void A_module_larger_than_the_cap_is_refused()
    {
        var oversized = "export const text = \"" + new string('a', PackageModuleLoader.MaxModuleBytes) + "\";";
        using var package = new TempPackage(
            "import { text } from \"./modules/big.js\"; export const value = text.length;",
            new Dictionary<string, string> { ["modules/big.js"] = oversized });

        var refused = Assert.Throws<HostRuleException>(() => Create(package.Root).Modules.Import("./main.js"));

        Assert.Equal(OutcomeCodes.ModuleNotAllowed, refused.Code);
    }

    [Fact]
    public void More_modules_than_the_cap_are_refused()
    {
        var modules = new Dictionary<string, string>(StringComparer.Ordinal);
        var imports = new List<string>();
        for (var index = 0; index < PackageModuleLoader.MaxModules + 8; index++)
        {
            var name = string.Create(CultureInfo.InvariantCulture, $"modules/m{index}.js");
            modules[name] = string.Create(CultureInfo.InvariantCulture, $"export const v{index} = {index};");
            imports.Add(string.Create(CultureInfo.InvariantCulture, $"import {{ v{index} }} from \"./{name}\";"));
        }

        using var package = new TempPackage(string.Join('\n', imports) + "\nexport const value = 1;", modules);

        var refused = Assert.Throws<HostRuleException>(() => Create(package.Root).Modules.Import("./main.js"));

        Assert.Equal(OutcomeCodes.ModuleNotAllowed, refused.Code);
    }
}
