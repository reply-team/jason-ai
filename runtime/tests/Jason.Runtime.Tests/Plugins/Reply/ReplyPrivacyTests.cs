using System.Text.Json.Nodes;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// What must never leave, proven by running all three operations and then looking everywhere a value could have
/// gone: the outcomes the runtime kept, the argument vectors the stand-in recorded, the diagnostics the child
/// wrote, and the log files on disk. Two different things are being ruled out and they leak differently — the
/// account's credential, which is the CLI's alone and may reach nothing of ours, and a person's own data, which
/// legitimately reaches Reply but only ever inside a body on stdin.
/// </summary>
/// <remarks>
/// The class writes process-wide variables — the one the CLI finds its account through, and the vendor-named
/// ones an operator might have exported — so it belongs to the collection that never runs two such classes at
/// once. Every fixture starts through <see cref="ReplyPlugins.StartAsync"/>, which holds the resolved program
/// to the test tree before anything runs.
/// </remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyPrivacyTests
{
    /// <summary>
    /// The credential the stand-in account holds. It is distinctive on purpose: a match somewhere it may not be
    /// could not then be a coincidence.
    /// </summary>
    private const string Credential = "reply-credential-marker-8c41f7";

    /// <summary>
    /// What an operator has in their own shell. No <c>REPLY_*</c> name enters the runtime, so neither of these
    /// may reach a child — and a value that did would be a key in a place nobody granted one.
    /// </summary>
    private const string ExportedKey = "reply-exported-key-marker-2ae9";

    private const string ExportedTeam = "reply-exported-team-marker-5d30";

    /// <summary>The one vendor-named variable a child may see: the package's own, and not a credential.</summary>
    private const string UpdateCheck = "REPLY_NO_UPDATE_CHECK";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task What_the_vendor_cli_holds_reaches_no_outcome_argument_diagnostic_or_log()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount()).WithMarker(Credential);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var results = await ReplyOperations.RunEachAsync(api, binding: null, Ct);

        // The credential is held by the CLI and by nothing else. The whole reason for driving a vendor's own
        // tool is that its store is the only place one exists, so finding this value in anything of ours would
        // mean the arrangement bought nothing.
        // Each of the four searches below runs over a corpus that demonstrably holds what did travel, so a
        // value that was there would be found: the package holds no credential and no capability that could
        // reach one, which makes this a claim about the arrangement rather than about the package's care.
        Assert.Equal(ReplyOperations.EveryCall, Paths(account));
        Assert.Contains(ReplyOperations.Ensured, Answered(results[1]), StringComparison.Ordinal);

        foreach (var result in results)
        {
            Assert.DoesNotContain(Credential, Answered(result), StringComparison.Ordinal);
        }

        foreach (var call in account.Calls)
        {
            Assert.DoesNotContain(Credential, string.Join(' ', call.Args), StringComparison.Ordinal);
        }

        // Every diagnostic line every child wrote: the `exec` record of what was started with which arguments
        // is here, which is exactly why an argument may never carry one.
        var diagnostics = ReadAll(api.Paths.PluginWorkDirectory);
        Assert.Contains("\"exec\"", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain(Credential, diagnostics, StringComparison.Ordinal);

        // Stopping flushes and closes the rolling file sink, so what is on disk now is everything there is.
        await api.Runtime.StopAsync();
        var logs = ReadAll(api.Paths.LogsDirectory);

        // The invocations are accounted for — which package, which operation — and then say nothing about what
        // any of them carried.
        Assert.Contains(ReplyPlugins.PluginId, logs, StringComparison.Ordinal);
        Assert.Contains(ReplyOperations.Enroll, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(Credential, logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_contact_field_ever_appears_in_an_argument()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        await ReplyOperations.RunEachAsync(api, binding: null, Ct);

        // This is the rule that makes the whole privacy claim real, and it is about data that legitimately
        // travels rather than about a secret that never should. The `exec` diagnostic records every argument
        // and the runtime's logs may never hold contact data, so a person's own fields ride on stdin and
        // nowhere else — asserted over the calls of all three operations, not the one that imports.
        var personal = new[] { ReplyOperations.Address, ReplyOperations.FirstName, ReplyOperations.LastName, ReplyOperations.Company };
        Assert.Equal(ReplyOperations.EveryCall, Paths(account));
        foreach (var call in account.Calls)
        {
            var line = string.Join(' ', call.Args);
            foreach (var field in personal)
            {
                Assert.DoesNotContain(field, line, StringComparison.OrdinalIgnoreCase);
            }
        }

        // And the second half, which is what makes the first half mean something: the data really did reach
        // Reply, and it reached it the only way it may. Both ensures carry all four fields in a body.
        var ensures = account.Calls.Where(call => call.Path == "/v3/contacts/import").ToList();
        Assert.Equal(2, ensures.Count);
        foreach (var ensure in ensures)
        {
            foreach (var field in personal)
            {
                Assert.Contains(field, ensure.Body!, StringComparison.Ordinal);
            }
        }

        // Nor may they turn up on disk afterwards, which is the same rule seen from the other end.
        Assert.DoesNotContain(ReplyOperations.Address, ReadAll(api.Paths.PluginWorkDirectory), StringComparison.OrdinalIgnoreCase);
        await api.Runtime.StopAsync();
        var logs = ReadAll(api.Paths.LogsDirectory);
        foreach (var field in personal)
        {
            Assert.DoesNotContain(field, logs, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task No_vendor_named_variable_of_the_operators_own_reaches_the_child()
    {
        using var account = ReplyOperations.Plant(new ReplyAccount());
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);

        // Set on the runtime's own process, which is where an operator would have them: exported in the shell
        // that started it. The base environment is machine configuration rather than an integration point, so
        // no name of a vendor's is copied out of it — and an account is chosen by the profile store and the
        // route's binding instead.
        using var key = new ProcessVariable("REPLY_API_KEY", ExportedKey);
        using var team = new ProcessVariable("REPLY_TEAM_ID", ExportedTeam);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var results = await ReplyOperations.RunEachAsync(api, binding: null, Ct);

        Assert.Equal(ReplyOperations.EveryCall, Paths(account));
        foreach (var call in account.Calls)
        {
            // Read from the child's own environment as it saw it: the only vendor-named variable there is the
            // one the package sets on the call, and it switches off an update check rather than carrying a
            // credential.
            Assert.Equal([UpdateCheck], call.Environment.Keys.Order());
            Assert.Equal("1", call.Environment[UpdateCheck]);
        }

        // And neither value reached anything else either: not an argument, not an outcome, not a log.
        foreach (var call in account.Calls)
        {
            Assert.DoesNotContain(ExportedKey, string.Join(' ', call.Args), StringComparison.Ordinal);
            Assert.DoesNotContain(ExportedTeam, string.Join(' ', call.Args), StringComparison.Ordinal);
        }

        foreach (var result in results)
        {
            Assert.DoesNotContain(ExportedKey, Answered(result), StringComparison.Ordinal);
        }

        await api.Runtime.StopAsync();
        var logs = ReadAll(api.Paths.LogsDirectory);
        Assert.DoesNotContain(ExportedKey, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(ExportedTeam, logs, StringComparison.Ordinal);
    }

    /// <summary>Everything one invocation answered, whichever way it ended, as one string to look through.</summary>
    private static string Answered(PluginInvocationResult result) => result.Outcome switch
    {
        InvocationOutcome.Succeeded succeeded =>
            (succeeded.Result?.ToJsonString() ?? string.Empty) + (succeeded.ExternalIds?.ToJsonString() ?? string.Empty),
        InvocationOutcome.Failed failed =>
            failed.Error.Code + failed.Error.Message + (failed.Error.Details?.ToJsonString() ?? string.Empty)
            + (failed.Error.ExternalIds?.ToJsonString() ?? string.Empty),
        InvocationOutcome.ProtocolFailure protocol => protocol.Code + protocol.Message + protocol.StderrTail,
        _ => string.Empty,
    };

    /// <summary>Every call the account was asked to make, as "METHOD /path", in the order it was asked.</summary>
    private static IReadOnlyList<string> Paths(ReplyAccount account) =>
        [.. account.Calls.Select(call => call.Method + " " + call.Path)];

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
