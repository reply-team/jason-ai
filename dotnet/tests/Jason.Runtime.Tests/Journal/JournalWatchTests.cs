using System.Text;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Journal;

/// <summary>
/// <c>jason journal watch</c> against a real runtime in this process: each line the stream sends is printed as it
/// arrives, compact JSON by default and rendered under <c>--human</c>, until the person interrupts it, which is exit 0;
/// a runtime that refuses the token or ends the stream is exit 3, and a refusal of the request as made is exit 1 with
/// the runtime's own answer.
/// </summary>
public class JournalWatchTests
{
    private static readonly TimeSpan Arrival = TimeSpan.FromSeconds(15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_watch_prints_each_line_written_after_it_opens_as_journal_list_answers_it_and_ends_at_an_interrupt_with_0()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Watched" }, Ct);
        using var watch = Watch.Start(api, "journal", "watch");

        // When the stream has opened is not printed, so lines are written until one arrives; then one more.
        var opened = await watch.FirstLineAsync(() => AppendAsync(api, campaign, "opening"));
        var appended = await AppendAsync(api, campaign, "after");
        var printed = await watch.LineAsync(line => (string?)JsonNode.Parse(line)!["id"] == appended.Id);

        var listed = JsonNode.Parse((await api.PostAsync(Operations.JournalList, new { CampaignId = campaign.Id, Limit = 1 }, Ct)).Body)!["items"]![0]!;
        Assert.True(JsonNode.DeepEquals(listed, JsonNode.Parse(printed)), $"journal list answers {listed.ToJsonString()}; the watch printed {printed}.");
        Assert.DoesNotContain('\n', printed);
        Assert.NotNull(opened);

        var (exit, _, error) = await watch.InterruptAsync();
        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public async Task A_watch_given_after_starts_after_the_line_it_names_and_renders_it_under_human()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Resumed" }, Ct);
        var named = await AppendAsync(api, campaign, "named");
        var next = await AppendAsync(api, campaign, "next");

        using (var json = Watch.Start(api, "journal", "watch", "--after", named.Id))
        {
            var first = await json.LineAsync(_ => true);
            Assert.Equal(next.Id, (string?)JsonNode.Parse(first)!["id"]);
            Assert.Equal(ExitCodes.Success, (await json.InterruptAsync()).Exit);
        }

        using var human = Watch.Start(api, "journal", "watch", "--after", named.Id, "--human");
        var listed = (await api.PostAsync(Operations.JournalList, new { Limit = 1 }, Ct)).Body;
        var line = JsonNode.Parse(listed)!["items"]![0]!.ToJsonString();
        var rendered = JournalRenderers.Entry(line)!;
        await human.LineAsync(printed => printed == rendered.Split(Environment.NewLine)[0]);
        var (exit, output, _) = await human.InterruptAsync();
        Assert.Equal(ExitCodes.Success, exit);
        Assert.StartsWith(rendered, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_after_the_runtime_refuses_is_exit_1_with_the_runtime_s_answer()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var (exit, output, _) = await Watch.RunAsync(api, "journal", "watch", "--after", "jrn_01K7QN00000000000000000000");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("validation_failed", (string?)JsonNode.Parse(output)!["error"]!["code"]);
        Assert.Equal(Streams.AfterParameter, (string?)JsonNode.Parse(output)!["error"]!["details"]![0]!["field"]);
        Assert.Equal("unknown", (string?)JsonNode.Parse(output)!["error"]!["details"]![0]!["code"]);
    }

    [Fact]
    public async Task A_token_the_runtime_refuses_is_exit_3_unauthorized()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var descriptor = JsonNode.Parse(await File.ReadAllTextAsync(api.Paths.DescriptorFile, Ct))!;
        descriptor["token"] = "not-the-token";
        await File.WriteAllTextAsync(api.Paths.DescriptorFile, descriptor.ToJsonString(), Ct);

        var (exit, output, _) = await Watch.RunAsync(api, "journal", "watch");

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Equal(CliErrors.Unauthorized, (string?)JsonNode.Parse(output)!["error"]!["code"]);
    }

    [Fact]
    public async Task A_watch_the_runtime_s_stop_ends_is_exit_3_saying_so()
    {
        var api = await RuntimeApiFixture.StartAsync(Ct);
        await using (api)
        {
            var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Stopped" }, Ct);
            using var watch = Watch.Start(api, "journal", "watch");
            await watch.FirstLineAsync(() => AppendAsync(api, campaign, "opening"));

            await api.Runtime.StopAsync();

            var (exit, output, _) = await watch.EndedAsync();
            Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
            Assert.Equal(CliErrors.RuntimeUnreachable, (string?)JsonNode.Parse(output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1])!["error"]!["code"]);
        }
    }

    private static Task<JournalEntryDto> AppendAsync(RuntimeApiFixture api, CampaignDto campaign, string key) =>
        api.PostOkAsync<JournalEntryDto>(
            Operations.JournalAppend,
            new { CampaignId = campaign.Id, Kind = "plan_revision", Key = key, New = "x", Actor = new { Type = "role", Id = "planner" } },
            Ct);

    /// <summary>One run of the CLI in this process, read while it runs and ended as a person ends it.</summary>
    private sealed class Watch : IDisposable
    {
        private readonly Lines _output = new();
        private readonly Lines _error = new();
        private readonly CancellationTokenSource _interrupt = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        private Task<int> _run = Task.FromResult(0);

        public static Watch Start(RuntimeApiFixture api, params string[] args)
        {
            var watch = new Watch();
            var env = new CliEnvironment(watch._output, watch._error, api.Paths, Variables: new Dictionary<string, string>());
            watch._run = Task.Run(() => CliApp.RunAsync(args, env, watch._interrupt.Token));
            return watch;
        }

        public static async Task<(int Exit, string Output, string Error)> RunAsync(RuntimeApiFixture api, params string[] args)
        {
            using var watch = Start(api, args);
            return await watch.EndedAsync();
        }

        /// <summary>Writes until the first line arrives, and answers it.</summary>
        public async Task<string> FirstLineAsync(Func<Task> write)
        {
            var deadline = DateTime.UtcNow + Arrival;
            while (DateTime.UtcNow < deadline && !_run.IsCompleted)
            {
                await write();
                if (await _output.WaitAsync(_ => true, TimeSpan.FromSeconds(2)) is { } line)
                {
                    return line;
                }
            }

            throw new Xunit.Sdk.XunitException($"No line arrived; the watch printed {_output.Text} and said {_error.Text}.");
        }

        public async Task<string> LineAsync(Func<string, bool> wanted) =>
            await _output.WaitAsync(wanted, Arrival)
            ?? throw new Xunit.Sdk.XunitException($"The line did not arrive; the watch printed {_output.Text} and said {_error.Text}.");

        public async Task<(int Exit, string Output, string Error)> InterruptAsync()
        {
            await _interrupt.CancelAsync();
            return await EndedAsync();
        }

        public async Task<(int Exit, string Output, string Error)> EndedAsync()
        {
            var exit = await _run.WaitAsync(Arrival, Ct);
            return (exit, _output.Text.Trim(), _error.Text.Trim());
        }

        public void Dispose()
        {
            _interrupt.Cancel();
            try
            {
                _run.Wait(Arrival, CancellationToken.None);
            }
            catch (AggregateException)
            {
                // The test has already failed for its own reason.
            }

            _interrupt.Dispose();
        }
    }

    /// <summary>What a run printed, written from its thread and read from the test's.</summary>
    private sealed class Lines : TextWriter
    {
        private readonly StringBuilder _text = new();
        private readonly SemaphoreSlim _written = new(0);

        public override Encoding Encoding => Encoding.UTF8;

        public string Text
        {
            get
            {
                lock (_text)
                {
                    return _text.ToString();
                }
            }
        }

        public override void Write(char value)
        {
            lock (_text)
            {
                _text.Append(value);
            }

            if (value == '\n')
            {
                _written.Release();
            }
        }

        public override void Write(string? value)
        {
            foreach (var character in value ?? string.Empty)
            {
                Write(character);
            }
        }

        /// <summary>The first whole line that is wanted, waiting for more while there is none; null when the wait runs out.</summary>
        public async Task<string?> WaitAsync(Func<string, bool> wanted, TimeSpan within)
        {
            var deadline = DateTime.UtcNow + within;
            while (true)
            {
                var whole = Text.Split('\n');
                var line = whole[..^1].Select(text => text.TrimEnd('\r')).FirstOrDefault(wanted);
                if (line is not null)
                {
                    return line;
                }

                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero || !await _written.WaitAsync(left, Ct))
                {
                    return null;
                }
            }
        }
    }
}
