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

    [Fact]
    public void The_shape_in_words_names_the_settings_file() =>
        Assert.Equal("settings.json.<id>.tmp", SettingsTemporaryFiles.Shape("settings.json"));
}
