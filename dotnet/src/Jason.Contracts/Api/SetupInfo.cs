using Jason.Contracts.Discovery;

namespace Jason.Contracts.Api;

/// <summary>
/// Where this Jason's parts are: the folder its roles are taught from, the folder its plugins are loaded from, the folder a
/// custom UI is served from, and its settings file — and whether that file holds plugins' configuration, their secrets
/// among it, as well. Facts, so that whoever changes one of these parts reads where it is rather than assuming it.
/// </summary>
/// <remarks>
/// <para>
/// The same record is the <c>setup</c> section of <c>system.info</c>, the runtime's own, and the <c>setup</c> member of
/// <c>jason status</c>'s body, which the CLI composes from its own data directory and so answers without a runtime.
/// </para>
/// <para>
/// Composed only by <see cref="From"/>, from <see cref="JasonPaths"/>, which is the one place the layout is written: a
/// folder that moves answers there, and every reader of this record follows it.
/// </para>
/// </remarks>
/// <param name="RoleSkillsDirectory">The role skills folder; the same folder as <c>skills.role_skills_directory</c>.</param>
/// <param name="PluginsDirectory">The folder holding one directory per installed plugin package.</param>
/// <param name="CustomUiDirectory">The folder a build of the UI of a person's own is served from, there or not.</param>
/// <param name="SettingsFile">The settings file of this installation, there or not.</param>
/// <param name="SettingsFileHoldsSecrets">
/// True where this layout keeps plugins' configuration (<c>Plugins:Config</c>), their secrets among it, in the settings
/// file too. A fact of the layout rather than of the file's content, so that nobody opens the file while it is true.
/// </param>
public sealed record SetupInfo(
    string RoleSkillsDirectory,
    string PluginsDirectory,
    string CustomUiDirectory,
    string SettingsFile,
    bool SettingsFileHoldsSecrets)
{
    /// <summary>The parts of the installation <paramref name="paths"/> names, as its layout places them.</summary>
    public static SetupInfo From(JasonPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return new SetupInfo(
            paths.RoleSkillsDirectory,
            paths.PluginsDirectory,
            paths.UiCustomDirectory,
            paths.UserSettingsFile,
            paths.UserSettingsFileHoldsSecrets);
    }
}
