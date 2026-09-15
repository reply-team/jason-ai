using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// One provider account, as a directory of JSON files: the state the stand-in vendor CLI reads and writes. It is
/// what a test hands a plugin through its binding, and what the test reads back afterwards to say what really
/// happened at the provider — how many contacts exist, who is on which list, and what the per-key ledger records.
/// </summary>
/// <remarks>
/// Everything stays inside a temporary directory the test owns: no network, no shell, nothing the machine running
/// the tests has to have installed. The files are the account, so asserting on them is asserting on effects.
/// </remarks>
public sealed class TestWorkspace : IDisposable
{
    public const string ContactsFile = "contacts.json";
    public const string ListsFile = "lists.json";
    public const string CampaignsFile = "campaigns.json";
    public const string LedgerFile = "ledger.json";
    public const string SuppressionFile = "suppression.json";
    public const string InstructionsFile = "instructions.json";
    public const string CallsFile = "calls.json";

    public TestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"), "workspace");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>A person the provider already holds, with the one address it was created from.</summary>
    public TestWorkspace WithContact(string id, string channel, string value) =>
        Edit(ContactsFile, contacts => contacts[id] = new JsonObject
        {
            ["id"] = id,
            ["channel"] = channel,
            ["value"] = value,
            ["asks"] = new JsonArray(),
        });

    public TestWorkspace WithList(string id, string name, params string[] members) =>
        Edit(ListsFile, lists => lists[id] = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["members"] = new JsonArray([.. members.Select(member => (JsonNode)JsonValue.Create(member))]),
        });

    /// <summary>A campaign, named by the provider's own word for its state rather than by Jason's vocabulary.</summary>
    public TestWorkspace WithCampaign(string id, string name, string providerStatus, params string[] enrolled) =>
        Edit(CampaignsFile, campaigns => campaigns[id] = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["status"] = providerStatus,
            ["enrollments"] = new JsonArray([.. enrolled.Select(contact => (JsonNode)JsonValue.Create(contact))]),
            ["vendor"] = new JsonObject { ["state"] = providerStatus, ["owner"] = "acct_1" },
        });

    /// <summary>Addresses the provider's own suppression register holds, which no operation may write to.</summary>
    public TestWorkspace WithSuppressed(params string[] values)
    {
        var existing = ReadArray(SuppressionFile);
        foreach (var value in values)
        {
            existing.Add(value);
        }

        Write(SuppressionFile, existing);
        return this;
    }

    /// <summary>
    /// Forces the one failure the recovery read exists for: the effect under this key is written, and then the
    /// program dies before it can say so. Nothing about the canonical input changes, which is the point.
    /// </summary>
    public TestWorkspace FailAfterEffect(string key, bool once = true) =>
        Edit(InstructionsFile, instructions => instructions["fail_after_effect"] = new JsonObject
        {
            ["key"] = key,
            ["once"] = once,
        });

    public JsonObject Contacts => ReadObject(ContactsFile);

    public JsonObject Lists => ReadObject(ListsFile);

    public JsonObject Campaigns => ReadObject(CampaignsFile);

    public JsonObject Ledger => ReadObject(LedgerFile);

    /// <summary>Who is on a list right now, which is the only honest answer to "did the add happen twice".</summary>
    public IReadOnlyList<string> MembersOf(string listId) =>
        [.. (Lists[listId]?["members"]?.AsArray() ?? []).Select(member => member!.GetValue<string>())];

    public IReadOnlyList<string> EnrollmentsIn(string campaignId) =>
        [.. (Campaigns[campaignId]?["enrollments"]?.AsArray() ?? []).Select(contact => contact!.GetValue<string>())];

    /// <summary>
    /// Every subcommand this account was asked for, in order. It is what makes an obligation to read before
    /// writing testable: the state left behind cannot tell a plugin that checked from one that guessed right.
    /// </summary>
    public IReadOnlyList<string> Calls =>
        [.. ReadArray(CallsFile).Select(call => call!["subcommand"]!.GetValue<string>())];

    /// <summary>How the caller asked for this person each time: <c>by_id</c> once a pin exists, never by address.</summary>
    public IReadOnlyList<string> AsksFor(string contactId) =>
        [.. (Contacts[contactId]?["asks"]?.AsArray() ?? []).Select(ask => ask!.GetValue<string>())];

    /// <summary>
    /// Runs one of the CLI's workspace subcommands the way a plugin would, without any JavaScript in between:
    /// the request goes in on stdin and exactly one answer comes back on stdout.
    /// </summary>
    public static async Task<(int ExitCode, JsonObject? Answer, string Stderr)> RunAsync(
        string workspace,
        IReadOnlyList<string> subcommand,
        JsonObject request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subcommand);
        ArgumentNullException.ThrowIfNull(request);

        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        info.ArgumentList.Add(FakeProviderCli.Dll);
        info.ArgumentList.Add("--workspace");
        info.ArgumentList.Add(workspace);
        foreach (var argument in subcommand)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        await process.StandardInput.WriteAsync(request.ToJsonString(JasonJson.Options));
        process.StandardInput.Close();

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var answer = stdout.Trim().Length == 0 ? null : JsonNode.Parse(stdout)!.AsObject();
        return (process.ExitCode, answer, stderr);
    }

    public Task<(int ExitCode, JsonObject? Answer, string Stderr)> RunAsync(
        IReadOnlyList<string> subcommand,
        JsonObject request,
        CancellationToken cancellationToken) =>
        RunAsync(Root, subcommand, request, cancellationToken);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(Root)!, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private TestWorkspace Edit(string fileName, Action<JsonObject> change)
    {
        var content = ReadObject(fileName);
        change(content);
        Write(fileName, content);
        return this;
    }

    private JsonObject ReadObject(string fileName)
    {
        var file = Path.Combine(Root, fileName);
        return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))!.AsObject() : [];
    }

    private JsonArray ReadArray(string fileName)
    {
        var file = Path.Combine(Root, fileName);
        return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))!.AsArray() : [];
    }

    private void Write(string fileName, JsonNode content) =>
        File.WriteAllText(Path.Combine(Root, fileName), content.ToJsonString(JasonJson.Options), new UTF8Encoding(false));
}
