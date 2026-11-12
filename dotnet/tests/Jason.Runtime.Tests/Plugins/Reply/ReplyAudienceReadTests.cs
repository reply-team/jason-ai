using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The audience family's three reads as the official package performs them: <c>audience.preview</c> by Reply's preview,
/// waited for inside the invocation's budget; <c>audience.filter_values</c> by the values Reply suggests for each of its
/// five typeaheads; and <c>audience.availability</c>, which asks Reply nothing, since Reply has no read of what an
/// account may spend.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyAudienceReadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Software => new()
    {
        ["jobTitles"] = new JsonArray(new JsonObject { ["value"] = "CTO", ["type"] = "include" }),
    };

    /// <summary>
    /// A preview answers Reply's counts and its sample, each person in Jason's shape with no way to reach them: no channels,
    /// and beside them only the keys of Reply's record that are meant to be shown — never the LinkedIn link Reply shows, nor
    /// an address, a number or a key Reply adds later. People per company go to Reply as its flag, and
    /// the wait Reply's job is given is at most half the invocation's budget, the reads around it having the other half.
    /// </summary>
    [Fact]
    public async Task A_preview_answers_reply_s_counts_and_a_sample_nobody_can_be_reached_from()
    {
        var reachable = ReplyAccount.Sampled("Lena", "Novak", "CTO", "Porto, Portugal", "https://www.linkedin.com/in/lena-novak-example", "Northwind Freight", "northwind.example");
        reachable["email"] = "lena.novak@northwind.example";
        reachable["phone"] = "+351 555 0100";
        reachable["twitter_url"] = "https://social.example/lena-novak";
        using var account = new ReplyAccount().WithPreview(1200, 340, reachable, ReplyAccount.Sampled(null, null, null, null, null, null, null));
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded("audience.preview", await ReplyOperations.InvokeAsync(api, "audience.preview", PreviewInput(perCompany: 2), Ct, timeout: TimeSpan.FromSeconds(60)));

        Assert.Equal((1200, 340), ((int)result["count"]!, (int)result["companies"]!));
        var lena = result["sample"]![0]!.AsObject();
        Assert.Equal(
            """{"external_id":null,"first_name":"Lena","last_name":"Novak","title":"CTO","company":{"name":"Northwind Freight","domain":"northwind.example","size":null,"industry":null},"location":"Porto, Portugal"}""",
            new JsonObject(lena.Where(member => member.Key != "raw").Select(member => KeyValuePair.Create(member.Key, member.Value?.DeepClone()))).ToJsonString());
        Assert.Equal(
            ["company_domain", "company_name", "first_name", "job_title", "last_name", "location"],
            JsonNode.Parse((string)lena["raw"]!)!.AsObject().Select(member => member.Key).Order(StringComparer.Ordinal));
        Assert.Equal("Northwind Freight", (string?)JsonNode.Parse((string)lena["raw"]!)!["company_name"]);
        Assert.Equal(
            """{"external_id":null,"first_name":null,"last_name":null,"title":null,"company":{"name":null,"domain":null,"size":null,"industry":null},"location":null}""",
            new JsonObject(result["sample"]![1]!.AsObject().Where(member => member.Key != "raw").Select(member => KeyValuePair.Create(member.Key, member.Value?.DeepClone()))).ToJsonString());

        var call = Assert.Single(account.Calls);
        Assert.Equal("2", call.Option("--max-per-company"));
        var wait = int.Parse(call.Option("--wait-seconds")!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(wait, 1, 30);
    }

    /// <summary>
    /// What Reply answers a preview it could not make: a definition it refused for a rule of its own, in its own words; a job
    /// still running when the wait ran out, one that failed, or one Reply no longer has, asked again.
    /// </summary>
    [Theory]
    [InlineData("pending", null, "provider_unavailable", FailureClass.Transient, "did not finish counting")]
    [InlineData("live_data.invalid_filters", 400, "definition_rejected", FailureClass.Validation, "minRevenue must be less than or equal to maxRevenue.")]
    [InlineData("live_data.preview_failed", null, "provider_unavailable", FailureClass.Transient, "did not finish counting")]
    [InlineData("live_data.preview_not_found", 404, "provider_unavailable", FailureClass.Transient, "did not finish counting")]
    [InlineData("live_data.service_failed", 502, "provider_unavailable", FailureClass.Transient, "did not finish counting")]
    public async Task A_preview_reply_could_not_make_is_the_document_s_refusal(string cliCode, int? status, string code, FailureClass failure, string said)
    {
        using var account = cliCode == "pending"
            ? new ReplyAccount().WithPendingPreview()
            : new ReplyAccount().Refuses("live-data preview", cliCode, status: status, detail: "minRevenue must be less than or equal to maxRevenue.");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed("audience.preview", await ReplyOperations.InvokeAsync(api, "audience.preview", PreviewInput(), Ct));

        Assert.Equal((code, failure), (error.Code, error.Class));
        Assert.Contains(said, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A filter's values are Reply's suggestions for the typeahead the declaration names, those that contain the text, at
    /// most the limit and none twice, so a value Reply repeats leaves the page shorter; with no text, the first Reply offers.
    /// </summary>
    [Fact]
    public async Task A_filter_s_values_are_reply_s_suggestions_for_its_typeahead()
    {
        using var account = new ReplyAccount().WithFilterValues("job-titles", "Chief Technology Officer", "Technology Lead", "Chief Technology Officer", "Head of Technology", "Technology Analyst");
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded("audience.filter_values", await ReplyOperations.InvokeAsync(api, "audience.filter_values", FilterInput("job-titles", "techno", 3), Ct));
        var first = ReplyOperations.Succeeded("audience.filter_values", await ReplyOperations.InvokeAsync(api, "audience.filter_values", FilterInput("job-titles", string.Empty, 100), Ct));

        Assert.Equal("""{"values":["Chief Technology Officer","Technology Lead"]}""", result.ToJsonString());
        Assert.Equal("""{"values":["Chief Technology Officer","Technology Lead","Head of Technology","Technology Analyst"]}""", first.ToJsonString());
    }

    /// <summary>A filter the declaration pairs with no typeahead has no values to list, and Reply is not asked.</summary>
    [Theory]
    [InlineData("companySizes")]
    [InlineData("jobtitles")]
    [InlineData("colours")]
    public async Task A_filter_with_no_typeahead_is_unknown_and_reply_is_not_asked(string filter)
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var error = ReplyOperations.Failed("audience.filter_values", await ReplyOperations.InvokeAsync(api, "audience.filter_values", FilterInput(filter.ToLowerInvariant(), "x", 5), Ct));

        Assert.Equal(("filter_unknown", FailureClass.Validation), (error.Code, error.Class));
        Assert.Empty(account.Calls);
    }

    /// <summary>
    /// Reply has no read of what an account may spend or how many contacts its plan still holds, so the package answers
    /// that the account may search and says nothing more, asking Reply nothing; a search's own answer is the gate.
    /// </summary>
    [Fact]
    public async Task Availability_answers_that_the_account_may_search_and_nothing_more()
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await ReplyPlugins.StartAsync(Ct);

        var result = ReplyOperations.Succeeded("audience.availability", await ReplyOperations.InvokeAsync(api, "audience.availability", new JsonObject
        {
            ["args"] = new JsonObject(),
            ["campaign"] = ReplyAudienceSearchTests.Campaign(),
        }, Ct));

        Assert.Equal("""{"available":true,"reason":null,"detail":null,"credits":null,"headroom":null,"vendor":{"status":null}}""", result.ToJsonString());
        Assert.Empty(account.Calls);
    }

    private static JsonObject PreviewInput(int? perCompany = null)
    {
        var args = new JsonObject { ["definition"] = Software };
        if (perCompany is { } most)
        {
            args["people_per_company"] = most;
        }

        return new JsonObject { ["args"] = args, ["definition"] = Software, ["campaign"] = ReplyAudienceSearchTests.Campaign() };
    }

    private static JsonObject FilterInput(string filter, string query, int limit) => new()
    {
        ["args"] = new JsonObject { ["filter"] = filter, ["query"] = query, ["limit"] = limit },
        ["campaign"] = ReplyAudienceSearchTests.Campaign(),
    };
}
