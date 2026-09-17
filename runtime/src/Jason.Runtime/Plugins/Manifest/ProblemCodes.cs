namespace Jason.Runtime.Plugins.Manifest;

/// <summary>
/// Everything that can be wrong with a candidate package, spelled exactly once. The vocabulary is part of the
/// contract with a plugin author: it is what <c>plugin.list</c> shows, what a rejected reload answers with, and
/// what the guide explains — so a rule without a code of its own would be a rule nobody could act on.
/// </summary>
public static class ProblemCodes
{
    /// <summary>A directory under <c>plugins/</c> with no <c>plugin.yaml</c>: not a plugin, never a rejection.</summary>
    public const string ManifestMissing = "manifest_missing";

    public const string YamlInvalid = "yaml_invalid";
    public const string ManifestVersionUnsupported = "manifest_version_unsupported";
    public const string FieldRequired = "field_required";
    public const string FieldInvalid = "field_invalid";
    public const string UnknownField = "unknown_field";
    public const string IdMismatch = "id_mismatch";
    public const string VersionInvalid = "version_invalid";
    public const string KindInvalid = "kind_invalid";
    public const string ContractUnsupported = "contract_unsupported";
    public const string OperationInvalid = "operation_invalid";
    public const string OperationDuplicate = "operation_duplicate";
    public const string OperationsNotAllowed = "operations_not_allowed";
    public const string EntryModuleMissing = "entry_module_missing";
    public const string EntryModuleOutsidePackage = "entry_module_outside_package";
    public const string EntryFunctionInvalid = "entry_function_invalid";

    /// <summary>
    /// The binding schema declares a field named like a credential. A binding names which identity the plugin
    /// should act as; the credential itself never travels in it, so a field that invites one is refused here
    /// rather than after somebody has already pasted a key into a route.
    /// </summary>
    public const string BindingSecretLike = "binding_secret_like";

    /// <summary>Environmental: nothing on the search path answers to a declared executable's name.</summary>
    public const string ExecutableMissing = "executable_missing";

    /// <summary>Environmental: something answers, but starting it would mean handing arguments to a shell.</summary>
    public const string ExecutableNotRunnable = "executable_not_runnable";

    /// <summary>Environmental: the program is older than the manifest asks for.</summary>
    public const string ExecutableIncompatible = "executable_incompatible";

    /// <summary>Environmental: the version command did not answer with a version.</summary>
    public const string ExecutableVersionCheckFailed = "executable_version_check_failed";

    public const string HostInvalid = "host_invalid";
    public const string VariableNameInvalid = "variable_name_invalid";
    public const string VariableReserved = "variable_reserved";
    public const string PackageTooLarge = "package_too_large";
    public const string PackageUnreadable = "package_unreadable";

    /// <summary>
    /// Not a package's problem at all: the <c>Plugins</c> section a load reads its own ceilings from is invalid,
    /// so no package can be measured against it. Named the way the routes section names its own
    /// (<c>routes_settings_invalid</c>), because an operator meets both while repairing one file.
    /// </summary>
    public const string PluginsSettingsInvalid = "plugins_settings_invalid";

    /// <summary>A warning: the settings grant something the manifest never asked for, so nothing was granted.</summary>
    public const string GrantUnrequested = "grant_unrequested";

    /// <summary>
    /// The problems that are about the machine rather than the package. An autostarted runtime sees a different
    /// search path than a shell does, so one uninstalled vendor tool holds its own plugin back and nothing else.
    /// </summary>
    public static IReadOnlySet<string> Environmental { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ExecutableMissing,
        ExecutableNotRunnable,
        ExecutableIncompatible,
        ExecutableVersionCheckFailed,
    };

    public static bool IsEnvironmental(string code) => Environmental.Contains(code);
}
