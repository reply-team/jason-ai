using Jason.Contracts.Discovery;
using Microsoft.Extensions.Configuration;

namespace Jason.Runtime.Configuration;

public static class JasonConfiguration
{
    public const string EnvironmentPrefix = "JASON_";

    public static IConfigurationRoot Build(JasonPaths paths, string? shippedSettingsDirectory)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var builder = new ConfigurationBuilder();
        if (shippedSettingsDirectory is not null)
        {
            builder.AddJsonFile(Path.Combine(shippedSettingsDirectory, "appsettings.json"), optional: true, reloadOnChange: false);
        }

        builder.AddJsonFile(paths.UserSettingsFile, optional: true, reloadOnChange: true);
        builder.AddEnvironmentVariables(EnvironmentPrefix);
        return builder.Build();
    }

    public static JasonOptions Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = new JasonOptions();
        configuration.Bind(options);
        var errors = options.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Invalid Jason configuration: " + string.Join(" ", errors));
        }

        return options;
    }
}
