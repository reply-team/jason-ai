using System.Globalization;
using System.Text.RegularExpressions;
using Jason.Contracts.Operations;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// A schema pattern's timeout is never a verdict on the value, and what it costs follows where it happened. Each place
/// that rule is published is held to the code that applies it, so the page a reader is sent to says what the runtime
/// does: the dialect's own sentence and reason code, the API's answer, the claim's transient end, the plugin answer's
/// ambiguous one, the route's retryable answer and the notification channel's.
/// </summary>
public sealed partial class PatternTimeoutDocTests
{
    /// <summary>
    /// Where a pattern's time is decided, and only where: the build's own patterns at start, a plugin's binding patterns
    /// at load. A result_format pattern is probed when the item is written, and the page says so.
    /// </summary>
    [Fact]
    public void The_dialect_says_where_a_pattern_s_time_is_decided_and_that_a_timeout_is_never_a_verdict()
    {
        var readme = Flat(Source("docs/contracts/README.md"));

        Assert.Contains(
            "**The build's own patterns are warmed when the runtime starts, and a plugin's binding patterns are decided when its package loads, on a warm engine; at a work item a timeout is never a verdict on the value, and what happens then depends on where it happened.**",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "A work item's `result_format` schema is probed the same way when the item is written, and when a patch names it, and a pattern that still cannot finish is refused there as `schema_pattern_invalid`, final: the schema has to change.",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "over a plugin's answer the plugin has run, so the attempt is `ambiguous` and the operation's own repeat rule decides.",
            readme,
            StringComparison.Ordinal);
        Assert.DoesNotContain("say each in place", readme, StringComparison.Ordinal);
        Assert.Contains(
            "The exception is a branch no pattern could finish measuring: unless a measured branch has already decided the combinator, its `pattern_timed_out` is passed up as it stands, at its own pointer",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"Every match runs under a published {SchemaValidator.PatternTimeoutMilliseconds} ms"),
            readme,
            StringComparison.Ordinal);
        Assert.Contains($"`{SchemaValidator.PatternTimedOut}` is about neither", readme, StringComparison.Ordinal);
    }

    /// <summary>The answer the API gives is the one the code builds: its code, its status and its retryable flag.</summary>
    [Fact]
    public void Work_execution_publishes_the_api_s_answer_as_the_code_builds_it()
    {
        var built = DomainErrors.ValidationTimedOut([]);
        var page = Source("docs/work-execution.md");

        Assert.Equal((AttemptErrors.ValidationTimedOut, 503, true), (built.Code, built.StatusCode, built.Retryable));
        Assert.Contains(
            $"| `{AttemptErrors.ValidationTimedOut}` | 503, and attempt | a pattern a value is measured against did not finish matching in time, twice, so nothing was decided and nothing changed: send the request again. At the claim the attempt ends `transient`; over a plugin's answer, `ambiguous` |",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "the request is answered `validation_timed_out`, 503, `retryable: true`",
            Flat(page),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Work_execution_says_the_claim_ends_transient_and_a_plugin_s_answer_ambiguous()
    {
        var page = Flat(Source("docs/work-execution.md"));

        Assert.Contains(
            "so the attempt ends `validation_timed_out`, `transient`, and the dispatcher retries it under the ordinary retry rule — never `input_invalid` or `binding_invalid`",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "is a lost answer instead: `validation_timed_out`, ambiguous, and the operation's own repeat rule decides",
            page,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The claim's row of <c>input_invalid</c> says a pattern's timeout is not that refusal, and its row of
    /// <c>binding_invalid</c> not that one; each row is found by its code, wherever the order puts it.
    /// </summary>
    [Fact]
    public void The_claim_s_rows_say_a_pattern_s_timeout_is_not_the_refusal()
    {
        var lines = Source("docs/routing.md").Split('\n');
        var input = lines.Single(line => line.StartsWith("| ", StringComparison.Ordinal) && line.Contains(" | `input_invalid` | ", StringComparison.Ordinal));
        var binding = lines.Single(line => line.StartsWith("| ", StringComparison.Ordinal) && line.Contains(" | `binding_invalid` | ", StringComparison.Ordinal));

        Assert.Contains("is not `input_invalid`", input, StringComparison.Ordinal);
        Assert.Contains("`validation_timed_out`, `transient`", input, StringComparison.Ordinal);
        Assert.Contains("is not `binding_invalid`", binding, StringComparison.Ordinal);
        Assert.Contains("`validation_timed_out`, `transient`", binding, StringComparison.Ordinal);
    }

    [Fact]
    public void Routing_says_route_set_and_a_reload_answer_as_one_to_send_again()
    {
        var page = Flat(Source("docs/routing.md"));

        Assert.Contains(
            "it is not `route_binding_invalid`. Alone, it leaves nothing written or activated, and `route.set`, `route.unset` and `plugin.reload` answer `validation_timed_out`, 503, `retryable: true`",
            page,
            StringComparison.Ordinal);
        Assert.Contains("under the dialect's own code, `pattern_timed_out`, rather than a route problem", page, StringComparison.Ordinal);
        Assert.Contains("Beside a route that really is wrong, it is left out of the 409, which names only what is wrong.", page, StringComparison.Ordinal);
        Assert.Contains("reported as `field_invalid` at `binding#/properties/<name>/pattern` with a message naming `pattern_timed_out`", page, StringComparison.Ordinal);
        Assert.Contains("so a pattern slow only on a long value is refused at load", page, StringComparison.Ordinal);
        Assert.DoesNotContain("It can only be a busy machine's cold engine", page, StringComparison.Ordinal);
    }

    /// <summary>route.resolve predicts the claim, and the claim would not refuse such a binding: the page says resolve answers 503.</summary>
    [Fact]
    public void Routing_says_route_resolve_answers_an_unmeasured_binding_as_one_to_ask_again()
    {
        Assert.Contains(
            "**A route whose binding no pattern could finish measuring** → `validation_timed_out`, a 503 with `retryable: true`",
            Flat(Source("docs/routing.md")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// What an operator meets when a binding pattern cannot finish at load is the manifest's own code, `field_invalid`,
    /// at the pattern's place — the dialect's reason is not what a reload carries — and both pages say so.
    /// </summary>
    [Fact]
    public void The_load_time_refusal_is_named_as_an_operator_sees_it()
    {
        Assert.Contains(
            "the reload reports it as `field_invalid` at `binding#/properties/<name>/pattern`, the code a manifest gives every dialect problem in its binding schema, with a message that names the cause, `pattern_timed_out`",
            Flat(Source("docs/contracts/README.md")),
            StringComparison.Ordinal);
        Assert.Contains(
            "including a pattern that cannot finish matching the short probe or the 64 KiB one within the dialect's 100 ms on a warm engine, twice | `field_invalid` |",
            Source("docs/plugins.md"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A pattern that could not finish is judged by where it happened, and the page says the two places one way each: a
    /// busy start loads once more, a live reload on a warm engine refuses the package. The plugin author's page says the
    /// start's extra load too, and the resolve heading counts the answers it lists.
    /// </summary>
    [Fact]
    public void The_pages_say_what_the_start_and_a_live_reload_each_do_with_a_pattern_that_could_not_finish()
    {
        var routing = Flat(Source("docs/routing.md"));

        Assert.Contains(
            "At a busy **start**, a binding probe that timed out twice, or a binding no pattern could finish measuring (§7), is not yet known to be bad",
            routing,
            StringComparison.Ordinal);
        Assert.Contains(
            "a load whose only problems are of these two kinds is built once more, whole, on the now warm engine, and a package whose probe still cannot finish on that second load loads `unavailable` with `probe_timed_out`",
            routing,
            StringComparison.Ordinal);
        Assert.Contains(
            "every claim on them ends `plugin_unavailable` at check 4, before a binding is measured at check 7, until the next load measures them",
            routing,
            StringComparison.Ordinal);
        Assert.Contains(
            "A binding of an available plugin no pattern could finish measuring on the second load still leaves the registries empty",
            routing,
            StringComparison.Ordinal);
        Assert.Contains(
            "At a **live reload** the engine is warm, so a binding pattern whose probe times out twice is at fault: the package is refused, as any package problem is, and the previous snapshot stays active.",
            routing,
            StringComparison.Ordinal);
        Assert.DoesNotContain("not a bad route or a bad package", routing, StringComparison.Ordinal);
        Assert.Contains("### `route resolve` has three different answers", Source("docs/routing.md"), StringComparison.Ordinal);
        Assert.Contains(
            "the start builds the whole load again on the now warm engine, because a cold engine on a busy machine is not yet a fault in the package",
            Flat(Source("docs/plugins.md")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Routing_says_an_answer_whose_validation_timed_out_pins_nothing()
    {
        Assert.Contains(
            "An answer whose validation timed out pins nothing: a pin is never overwritten, so it is written only from an answer that was checked, and that one never was. Its result stays on the attempt's trace and the identifiers it returned in the attempt's provenance, as `external_ids_returned`.",
            Flat(Source("docs/routing.md")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Notifications_say_what_a_channel_records_for_a_pattern_s_timeout()
    {
        Assert.Contains(
            "A binding or an input a pattern could not finish measuring is failed `validation_timed_out`, `transient` — nothing was sent — and an answer it could not measure is failed `validation_timed_out`, `ambiguous`",
            Flat(Source("docs/notifications.md")),
            StringComparison.Ordinal);
    }

    private static string Flat(string text) => Whitespace().Replace(text, " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
    }
}
