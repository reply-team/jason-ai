using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Tests.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// An invocation carries a contact's details into a vendor's tool and brings an answer back, and a credential
/// the user granted travels beside it. None of that may survive anywhere a person later reads: not in the log
/// files an agent pastes into a bug report, not in the working directory the runtime leaves behind, and not on
/// a command line every process on the machine can see. Distinctive canaries are used so a match cannot be a
/// coincidence, and the credential is a value this test invents rather than any real secret.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class PluginPrivacyTests
{
    /// <summary>What the caller sent: the sort of thing an operation's input holds about a person.</summary>
    private const string Marker = "secret_marker_9f3c";

    /// <summary>What a plugin answered, which the caller never sent, so a log line quoting it has only one source.</summary>
    private const string Answer = "answer_marker_4b71";

    /// <summary>
    /// The granted variable is named for this run alone. Test classes run side by side, and a variable of the
    /// whole process is the one piece of state two of them could not otherwise share safely.
    /// </summary>
    private readonly string _variable = "PRIVACY_TOKEN_" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    private readonly string _token = "privacy-credential-" + Guid.NewGuid().ToString("N");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task What_a_plugin_was_sent_answered_and_granted_reaches_no_file_a_person_reads()
    {
        Environment.SetEnvironmentVariable(_variable, _token);
        var fixture = await StartAsync();
        try
        {
            var echo = await InvokeAsync(fixture, TestPlugins.FakeProviderId, new JsonObject { ["note"] = Marker });
            Assert.Equal(Marker, Succeeded(echo)["echo"]!["note"]!.GetValue<string>());

            // A plugin that says its credential out loud, and answers with something nobody sent it: the first
            // is what the redaction exists for, the second proves the logs are silent about answers too.
            var spoken = await InvokeAsync(fixture, "talkative", new JsonObject { ["note"] = Marker });
            Assert.Equal(Answer, Succeeded(spoken)["answer"]!.GetValue<string>());

            // argv is readable by every process on the machine, so the whole of it is the mode word and four
            // public values. The command in front of them is wherever the executable happens to live.
            var command = echo.Launch!.Command;
            Assert.Equal(
                ["plugin-host", "--protocol", "1", "--plugin", TestPlugins.FakeProviderId, "--operation", "echo.run", "--correlation", echo.Provenance.CorrelationId],
                command.TakeLast(9));
            Assert.All(command.TakeLast(9), element => Assert.True(element.Length <= 128, $"'{element}' is longer than a public value has any reason to be."));
            Assert.DoesNotContain(command, element => element.Contains(Marker, StringComparison.Ordinal));
            Assert.DoesNotContain(command, element => element.Contains(_token, StringComparison.Ordinal));

            // The only file an invocation leaves behind is the child's stderr, with the credential taken out.
            var workDir = spoken.Launch!.WorkDir;
            Assert.Equal("stderr.log", Path.GetFileName(Assert.Single(Directory.GetFiles(workDir))));
            Assert.Empty(Directory.GetDirectories(workDir));
            var stderr = await File.ReadAllTextAsync(Path.Combine(workDir, "stderr.log"), Ct);
            Assert.Contains(Redactor.Mask, stderr, StringComparison.Ordinal);
            Assert.DoesNotContain(_token, stderr, StringComparison.Ordinal);

            // And never the invocation or the outcome: an input may hold contact data, and so may an answer.
            var artifacts = ReadAll(fixture.Paths.PluginWorkDirectory);
            Assert.DoesNotContain(Marker, artifacts, StringComparison.Ordinal);
            Assert.DoesNotContain(Answer, artifacts, StringComparison.Ordinal);
            Assert.DoesNotContain(_token, artifacts, StringComparison.Ordinal);

            // Stopping flushes and closes the rolling file sink, so what is on disk now is everything there is.
            await fixture.Runtime.StopAsync();
            var logs = ReadAll(fixture.Paths.LogsDirectory);

            // An invocation is accounted for by what it was — which package, at which digest, under which id.
            Assert.Contains(TestPlugins.FakeProviderId, logs, StringComparison.Ordinal);
            Assert.Contains(echo.Provenance.Digest!, logs, StringComparison.Ordinal);
            Assert.Contains(echo.Provenance.InvocationId, logs, StringComparison.Ordinal);
            Assert.Contains(echo.Provenance.CorrelationId, logs, StringComparison.Ordinal);
            Assert.Contains("echo.run", logs, StringComparison.Ordinal);

            // Never by what it carried.
            Assert.DoesNotContain(Marker, logs, StringComparison.Ordinal);
            Assert.DoesNotContain(Answer, logs, StringComparison.Ordinal);
            Assert.DoesNotContain(_token, logs, StringComparison.Ordinal);
            Assert.DoesNotContain(_variable, logs, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.Runtime.Token, logs, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(_variable, null);
            await fixture.DisposeAsync();
        }
    }

    private static JsonObject Succeeded(PluginInvocationResult result) =>
        Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).Result!.AsObject();

    private static async Task<PluginInvocationResult> InvokeAsync(RuntimeApiFixture fixture, string plugin, JsonObject input)
    {
        using var scope = fixture.Runtime.Services.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<PluginInvoker>();
        return await invoker.InvokeAsync(
            new PluginInvocationRequest(plugin, "echo.run", input, null, "att_01K0PRIVACY"),
            Ct);
    }

    private Task<RuntimeApiFixture> StartAsync() =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, RuntimeApiFixture.DispatcherOff);
                TestPlugins.InstallFakeProvider(paths);
                TestPlugins.Write(
                    paths,
                    "talkative",
                    TestPlugins.Manifest("talkative", extra: $"""
                        capabilities:
                          env:
                            variables: [{_variable}]
                        """),
                    $$"""
                    export function invoke(operation, input) {
                      const credential = host.env("{{_variable}}");
                      host.log("info", "calling with " + credential, { authorization: credential });
                      return { result: { echo: input, answer: "{{Answer}}" } };
                    }
                    """);
                TestPlugins.Grant(paths, "talkative", env: [_variable]);
            },
            configureServices: services => services.AddSingleton<IPluginHostLocator>(new JasonDllLocator()));

    private static string ReadAll(string directory)
    {
        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        return string.Concat(files.Select(ReadShared));
    }

    /// <summary>A log file the runtime may still hold open is read rather than fought over.</summary>
    private static string ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
