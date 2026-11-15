using Jason.Contracts.Discovery;
using Jason.Contracts.Files;

namespace Jason.Contracts.Tests.Files;

/// <summary>
/// The settings writers' temporary files are named in exactly two shapes, the runtime's and the desktop's, and no other
/// file of the settings file's folder is one of them.
/// </summary>
public sealed class SettingsTemporaryFilesTests
{
    [Theory]
    [InlineData("settings.json.0123456789abcdef0123456789abcdef.tmp")] // the runtime's, a GUID in the N format
    [InlineData("settings.json.4242.tmp")] // the desktop's, its process id
    [InlineData("settings.json.1.tmp")]
    [InlineData("settings.json.4294967295.tmp")]
    public void A_writer_s_temporary_name_is_one(string name) =>
        Assert.True(SettingsTemporaryFiles.IsOne("settings.json", name));

    [Theory]
    [InlineData("settings.json")]
    [InlineData("secrets.json")]
    [InlineData("settings.json.tmp")]
    [InlineData("settings.json..tmp")]
    [InlineData("settings.json.bak")]
    [InlineData("settings.json.4242.tmp.bak")]
    [InlineData("settings.json.0123456789ABCDEF0123456789ABCDEF.tmp")] // no writer writes upper case
    [InlineData("settings.json.0123456789abcdef0123456789abcde.tmp")] // 31 digits
    [InlineData("settings.json.0123456789abcdef0123456789abcdef0.tmp")] // 33 digits
    [InlineData("settings.json.12345678901.tmp")] // eleven decimal digits: no process id
    [InlineData("settings.json.notes.tmp")]
    [InlineData("settings.json.-1.tmp")]
    [InlineData("Settings.json.4242.tmp")]
    [InlineData("other.json.4242.tmp")]
    [InlineData("settings.json.4242.TMP")]
    public void Any_other_name_is_not(string name) =>
        Assert.False(SettingsTemporaryFiles.IsOne("settings.json", name));

    /// <summary>The runtime's writer takes its name from here, so the name it makes is one by construction.</summary>
    [Fact]
    public void The_runtime_s_name_for_a_write_is_one_and_carries_the_write_s_id()
    {
        var id = Guid.Parse("0123456789abcdef0123456789abcdef");

        var name = SettingsTemporaryFiles.NameFor("settings.json", id);

        Assert.Equal("settings.json.0123456789abcdef0123456789abcdef.tmp", name);
        Assert.True(SettingsTemporaryFiles.IsOne("settings.json", name));
    }

    /// <summary>
    /// The files whose writes leave such names: in layout 1 the one settings file, which holds the secrets; in layout 2
    /// the settings file a person may change and the secrets file beside the configuration, each in its own folder.
    /// </summary>
    [Fact]
    public void The_written_files_are_the_settings_file_and_where_the_layout_keeps_the_secrets_file_apart()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-written-files");

        Assert.Equal([Path.Combine(root, "config", "settings.json")], SettingsTemporaryFiles.WrittenFiles(new JasonPaths(root, 1)));
        Assert.Equal(
            [Path.Combine(root, "setup", "settings.json"), Path.Combine(root, "config", "secrets.json")],
            SettingsTemporaryFiles.WrittenFiles(new JasonPaths(root, 2)));
    }

    [Fact]
    public void The_shape_in_words_names_the_settings_file() =>
        Assert.Equal("settings.json.<id>.tmp", SettingsTemporaryFiles.Shape("settings.json"));
}
