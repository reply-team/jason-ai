using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using YamlDotNet.RepresentationModel;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// Each document says which operation of the vendor-neutral catalogue it is: its <c>l1</c> name, the family the catalogue
/// files it in, and whether the catalogue marks it core. All three are read from the catalogue's own fragment under
/// <c>skills/business/sdr-operations/operations/</c>, so a document cannot claim a family or a core mark its fragment does
/// not give it — a fragment that states no <c>core</c> is not core.
/// </summary>
public class LevelOneFragmentTests
{
    public static TheoryData<string> Published()
    {
        var data = new TheoryData<string>();
        foreach (var contract in OperationCatalog.All)
        {
            data.Add(contract.Id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Published))]
    public void A_document_s_level_one_name_family_and_core_are_its_fragment_s(string id)
    {
        var level1 = JsonNode.Parse(Source($"docs/contracts/operations/{id}.json"))!["l1"]!;
        var name = (string)level1["name"]!;
        var family = (int)level1["family"]!;

        var fragments = Directory.GetFiles(Path.Combine(Root(), "skills", "business", "sdr-operations", "operations"), $"{family:D2}-*.yaml");
        var fragment = Assert.Single(fragments);
        var yaml = new YamlStream();
        using (var reader = new StreamReader(fragment))
        {
            yaml.Load(reader);
        }

        var document = (YamlMappingNode)yaml.Documents[0].RootNode;
        Assert.Equal(family.ToString(System.Globalization.CultureInfo.InvariantCulture), ((YamlScalarNode)document.Children[new YamlScalarNode("family")]).Value);
        var operation = Assert.Single(
            ((YamlSequenceNode)document.Children[new YamlScalarNode("operations")]).Children.OfType<YamlMappingNode>(),
            entry => entry.Children.TryGetValue(new YamlScalarNode("name"), out var named) && ((YamlScalarNode)named).Value == name);
        var core = operation.Children.TryGetValue(new YamlScalarNode("core"), out var marked) && ((YamlScalarNode)marked).Value == "true";

        Assert.Equal(core, (bool)level1["core"]!);
    }

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine(Root(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
