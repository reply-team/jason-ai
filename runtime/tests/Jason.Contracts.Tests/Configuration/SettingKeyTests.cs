using Jason.Contracts.Configuration;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Tests.Configuration;

/// <summary>
/// Which keys of a settings path may be printed as they were typed. A key is text a person wrote, and text
/// written into the wrong place is often a secret, so only the names the code already knows travel and anything
/// else is printed as a placeholder.
/// </summary>
public class SettingKeyTests
{
    private const string InstalledPlugin = "fake-provider";

    private static readonly IReadOnlySet<string> Installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { InstalledPlugin };

    [Theory]
    [InlineData("TickSeconds")]
    [InlineData("tickseconds")]
    [InlineData("DISPATCHER")]
    [InlineData("Binding")]
    [InlineData(InstalledPlugin)]
    public void A_key_the_code_already_knows_is_shown_as_it_was_typed(string key)
    {
        Assert.Equal(key, SettingKey.Shown(key, Installed));
    }

    [Fact]
    public void A_published_operation_and_a_public_id_are_shown_as_they_were_typed()
    {
        var operation = OperationCatalog.All[0].Id;
        var campaign = PublicId.New("cmp");
        var workItem = PublicId.New("wi");

        Assert.Equal(operation, SettingKey.Shown(operation, Installed));
        Assert.Equal(campaign, SettingKey.Shown(campaign, Installed));
        Assert.Equal(workItem, SettingKey.Shown(workItem, Installed));
    }

    [Theory]
    [InlineData("other-provider")]
    [InlineData("ghp_abc123def456")]
    [InlineData("my secret")]
    [InlineData("cmp_01ARZ3NDEKTSV4RRFFQ69G5FA")]
    [InlineData("xyz_01ARZ3NDEKTSV4RRFFQ69G5FAV")]
    [InlineData("cmp_01arz3ndektsv4rrffq69g5fav")]
    [InlineData("cmp_01ARZ3NDEKTSV4RRFFQ69G5FAU")]
    [InlineData("12a")]
    [InlineData("4111111111111111")]
    [InlineData("")]

    // On its own a number is no index into anything: it is shown only where a list stands before it.
    [InlineData("0")]
    [InlineData("12")]
    [InlineData("123456")]
    public void Anything_else_is_written_as_a_placeholder(string key)
    {
        Assert.Equal(SettingKey.Hidden, SettingKey.Shown(key, Installed));
    }

    /// <summary>
    /// A key made of digits is an index only where the key before it names a list. No map in the settings is keyed
    /// by a number — a plugin's id and an operation's name both start with a letter — and a number typed where a
    /// name belonged can be a secret as easily as a word can.
    /// </summary>
    [Theory]
    [InlineData("Manager:Triggers:0", "Manager:Triggers:0")]
    [InlineData("manager:triggers:3", "manager:triggers:3")]
    [InlineData("Roles:DefaultEntryCommand:1", "Roles:DefaultEntryCommand:1")]
    [InlineData("Plugins:Grants:fake-provider:Exec:0", "Plugins:Grants:fake-provider:Exec:0")]
    [InlineData("Plugins:Grants:not-installed:Env:12", "Plugins:Grants:<key>:Env:12")]
    [InlineData("Plugins:Grants:123456", "Plugins:Grants:<key>")]
    [InlineData("Plugins:Grants:123456:Exec:0", "Plugins:Grants:<key>:Exec:0")]
    [InlineData("Notifications:Plugins:42:Binding", "Notifications:Plugins:<key>:Binding")]
    [InlineData("Routes:Operations:7", "Routes:Operations:<key>")]
    [InlineData("Dispatcher:0", "Dispatcher:<key>")]
    [InlineData("0:Triggers", "<key>:Triggers")]
    public void A_number_is_shown_only_as_an_index_into_a_list(string path, string expected)
    {
        Assert.Equal(expected, SettingKey.ShownPath(path, Installed));
    }

    /// <summary>The three kinds of list there are, each shown with its indices where it stands.</summary>
    [Theory]
    [InlineData("Manager:Triggers:0")]
    [InlineData("Roles:DefaultEntryCommand:4")]
    [InlineData("Plugins:Grants:fake-provider:Exec:2")]
    [InlineData("Plugins:Grants:fake-provider:Http:2")]
    [InlineData("Plugins:Grants:fake-provider:Env:2")]
    [InlineData("PLUGINS:grants:FAKE-PROVIDER:ENV:2")]
    public void A_list_shows_its_indices_at_its_whole_path(string path)
    {
        Assert.Equal(path, SettingKey.ShownPath(path, Installed));
    }

    /// <summary>
    /// A list is a place, not a name. <c>Exec</c> and <c>Http</c> also name two objects of the plugin limits and
    /// <c>Env</c>, <c>Exec</c> and <c>Triggers</c> are keys anybody can type inside a binding; a number under any of
    /// them is a number somebody chose — a phone number, a PIN, an account — and is never shown.
    /// </summary>
    [Theory]
    [InlineData("Plugins:Http:5551234567", "Plugins:Http:<key>")]
    [InlineData("Plugins:Exec:5551234567", "Plugins:Exec:<key>")]
    [InlineData("Routes:Default:Binding:Env:5551234567", "Routes:Default:Binding:Env:<key>")]
    [InlineData("Routes:Default:Binding:Triggers:3", "Routes:Default:Binding:Triggers:<key>")]
    [InlineData("Notifications:Plugins:fake-provider:Binding:Exec:12", "Notifications:Plugins:fake-provider:Binding:Exec:<key>")]
    [InlineData("Extra:Manager:Triggers:0", "<key>:Manager:Triggers:<key>")]
    [InlineData("Plugins:Grants:Exec:0", "Plugins:Grants:Exec:<key>")]
    [InlineData("Plugins:Grants:a:b:Exec:0", "Plugins:Grants:<key>:<key>:Exec:<key>")]
    [InlineData("Plugins:Grants:fake-provider:Exec:0:0", "Plugins:Grants:fake-provider:Exec:0:<key>")]
    [InlineData("Manager:Triggers:0:5551234567", "Manager:Triggers:0:<key>")]
    public void A_number_is_never_an_index_anywhere_but_the_whole_path_of_a_list(string path, string expected)
    {
        Assert.Equal(expected, SettingKey.ShownPath(path, Installed));
    }

    /// <summary>
    /// Nobody writes a list of ten thousand settings by hand, so an index of five digits or more is not one: it is a
    /// number somebody typed — a phone number, a PIN — that happens to stand where a list does, and is not shown.
    /// </summary>
    [Theory]
    [InlineData("Manager:Triggers:9999", "Manager:Triggers:9999")]
    [InlineData("Manager:Triggers:12345", "Manager:Triggers:<key>")]
    [InlineData("Manager:Triggers:5551234567", "Manager:Triggers:<key>")]
    [InlineData("Plugins:Grants:fake-provider:Env:12345", "Plugins:Grants:fake-provider:Env:<key>")]
    public void An_index_is_shown_only_up_to_four_digits(string path, string expected)
    {
        Assert.Equal(expected, SettingKey.ShownPath(path, Installed));
    }

    [Fact]
    public void A_path_is_shown_one_key_at_a_time()
    {
        Assert.Equal("Plugins:Grants:fake-provider:Exec", SettingKey.ShownPath("Plugins:Grants:fake-provider:Exec", Installed));
        Assert.Equal("Plugins:Grants:<key>:Exec", SettingKey.ShownPath("Plugins:Grants:ghp_abc123def456:Exec", Installed));
        Assert.Equal("Manager:Triggers:0", SettingKey.ShownPath("Manager:Triggers:0", Installed));
        Assert.Equal("<key>:<key>", SettingKey.ShownPath("my secret:other", Installed));
    }
}
