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
/// file too. A fact of the layout rather than of the file's content, so that it can be said without reading the file.
/// </param>
/// <param name="Layout">
/// The layout the data directory is in, as its stamp says: 1 where there is none, the layout a readable stamp names, and
/// null where it cannot be read — never a layout guessed for it. A runtime is only ever on a layout it runs, so in
/// <c>system.info</c> it is always a number; null is said by <c>jason status</c>, and the doctor's copy of its report, of a stamp it found unreadable.
/// </param>
public sealed record SetupInfo(
    string RoleSkillsDirectory,
    string PluginsDirectory,
    string CustomUiDirectory,
    string SettingsFile,
    bool SettingsFileHoldsSecrets,
    int? Layout = null)
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
            paths.UserSettingsFileHoldsSecrets,
            paths.Stamp switch
            {
                null => paths.Layout,
                { State: LayoutStampState.Read } stamp => stamp.Layout,
                { State: LayoutStampState.Missing } => 1,
                _ => null,
            });
    }
}
