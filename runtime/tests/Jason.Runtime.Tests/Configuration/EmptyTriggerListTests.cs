using System.Text.Json.Nodes;
using Jason.Runtime.Configuration;
using Jason.Runtime.Hosting;
using Jason.Runtime.Journal;
using Jason.Runtime.Tests.Integration;
using Microsoft.Extensions.Configuration;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// A trigger list that is written and names no kind — <c>[]</c>, <c>null</c>, an empty object, an empty variable — used
/// to read as one that was never written, so a person who meant "wake my manager for nothing" got the five defaults and
/// was told nothing. It is refused, at a start and at a live edit, in one sentence that says what to write instead. A
/// list that is not written at all is still the defaults, and a narrowed list still stands — however its key is spelled,
/// since the binder reads keys without regard to case and this rule must read them the same way.
/// </summary>
public sealed class EmptyTriggerListTests
{
    private const string Refusal = "Manager:Triggers is written but lists no trigger kind: leave it out for the defaults, or list the kinds.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("""{"Manager":{"Triggers":[]}}""")]
    [InlineData("""{"Manager":{"Triggers":null}}""")]
    [InlineData("""{"Manager":{"Triggers":{}}}""")]
    [InlineData("""{"Manager":{"triggers":[]}}""")]
    [InlineData("""{"manager":{"TRIGGERS":null}}""")]
    public void A_trigger_list_written_with_no_kind_in_it_is_refused(string file)
    {
        var options = FromFile(file);

        Assert.True(options.Triggers.Count == 0, $"The list read as: {string.Join(", ", options.Triggers)}");
        Assert.Equal([Refusal], Failures(options));
    }

    /// <summary>
    /// What an empty <c>JASON_Manager__Triggers</c> becomes: the environment's provider makes the key, spelled as the
    /// variable spelled it, with an empty value. The variable itself, through the shipped program, is held by the start
    /// tests of the app.
    /// </summary>
    [Theory]
    [InlineData("Manager:Triggers")]
    [InlineData("MANAGER:TRIGGERS")]
    public void An_empty_value_for_the_list_is_refused_as_an_empty_list(string key)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = string.Empty }).Build();
        var options = new ManagerOptions();

        ManagerOptions.Fill(configuration.GetSection(ManagerOptions.Section), options);

        Assert.True(options.Triggers.Count == 0, $"The list read as: {string.Join(", ", options.Triggers)}");
        Assert.Equal([Refusal], Failures(options));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"Manager":{}}""")]
    [InlineData("""{"Manager":{"ReviewSeconds":600}}""")]
    public void A_trigger_list_left_out_is_the_defaults(string file)
    {
        var options = FromFile(file);

        Assert.Equal(ManagerOptions.Default, options.Triggers);
        Assert.Empty(Failures(options));
    }

    [Theory]
    [InlineData("""{"Manager":{"Triggers":["workitem_failed"]}}""")]
    [InlineData("""{"Manager":{"triggers":["workitem_failed"]}}""")]
    [InlineData("""{"MANAGER":{"TRIGGERS":["workitem_failed"]}}""")]
    public void A_narrowed_list_stands_however_its_key_is_spelled(string file)
    {
        var options = FromFile(file);

        Assert.Equal([JournalKinds.WorkItemFailed], options.Triggers);
        Assert.Empty(Failures(options));
    }

    /// <summary>A start with the empty list is refused in the runtime's own sentence, through the writer a detached start reads.</summary>
    [Fact]
    public async Task A_start_with_an_empty_trigger_list_is_refused()
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, """{"Manager":{"Triggers":[]}}""");
        using var refusals = new StringWriter();

        // Bounded, because a runtime that took the list for the defaults would start and run until it was stopped.
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        bound.CancelAfter(TimeSpan.FromSeconds(30));
        var exit = await RuntimeHost.RunAsync(dir.Paths, TestRuntimeOptions.Quiet with { Refusals = refusals }, bound.Token);

        Assert.True(exit == 1, $"The start answered {exit}: {refusals}");
        Assert.Equal("Invalid Jason configuration: " + Refusal, refusals.ToString().Trim());
    }

    /// <summary>
    /// A live edit to the empty list is refused like any other: the narrowed list the runtime started with stays in force,
    /// and the log says why in the same sentence.
    /// </summary>
    [Fact]
    [Critical(Family.C1, "an empty trigger list never applies")]
    public async Task A_live_edit_to_an_empty_trigger_list_is_refused_and_the_list_in_force_is_kept()
    {
        var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => File.WriteAllText(paths.UserSettingsFile, """{"Manager":{"Triggers":["workitem_failed"]}}"""));
        await using (fixture)
        {
            var settings = fixture.Resolve<LiveSettings<ManagerOptions>>();
            Assert.Equal([JournalKinds.WorkItemFailed], settings.Current.Triggers);

            File.WriteAllText(fixture.Paths.UserSettingsFile, """{"Manager":{"Triggers":[]}}""");
            var refused = await TestOptions.RefusedAsync(settings, Ct);

            Assert.True(refused, $"The edit was not refused; the list in force is: {string.Join(", ", settings.Current.Triggers)}");
            Assert.Equal([JournalKinds.WorkItemFailed], settings.Current.Triggers);
            await fixture.Runtime.StopAsync();

            var failures = string.Concat(Directory.GetFiles(fixture.Paths.LogsDirectory).Select(File.ReadAllText))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonNode.Parse(line)!)
                .Where(line => (string?)line["@l"] == "Error" && (string?)line["Section"] == ManagerOptions.Section)
                .Select(line => (string?)line["Failure"])
                .ToList();
            Assert.Equal([Refusal], failures);
        }
    }

    private static ManagerOptions FromFile(string json)
    {
        using var dir = new TempDataDir();
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, json);
        var options = new ManagerOptions();

        using var configuration = JasonConfiguration.Build(dir.Paths, shippedSettingsDirectory: null);
        ManagerOptions.Fill(configuration.GetSection(ManagerOptions.Section), options);
        return options;
    }

    private static IEnumerable<string> Failures(ManagerOptions options) =>
        new ManagerOptionsValidator().Validate(null, options).Failures ?? [];
}
