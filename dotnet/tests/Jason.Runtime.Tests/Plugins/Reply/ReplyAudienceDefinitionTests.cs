using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Tests.Routing;

namespace Jason.Runtime.Tests.Plugins.Reply;

/// <summary>
/// The definition the official package declares under <c>audience_definition</c>: Reply's own Live Data filters by Reply's
/// own names, each described and paired with the typeahead that lists its values, with Reply's bounds in the schema, so
/// a definition Reply would refuse for them is refused at the claim and at the preview before Reply is asked.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class ReplyAudienceDefinitionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Every typeahead the declaration pairs a filter with, as <c>jason operation describe audience.search</c> answers it for a
    /// campaign routed to this package, is one whose values the package reads from Reply, by that kind: a typeahead the
    /// module does not answer, or one reply-cli has no kind for, fails here.
    /// </summary>
    [Fact]
    public async Task Every_typeahead_the_declaration_names_is_one_whose_values_reply_answers()
    {
        string[] kinds = ["industries", "locations", "job-titles", "departments", "seniorities"];
        using var account = new ReplyAccount();
        foreach (var kind in kinds)
        {
            account.WithFilterValues(kind, "A value of " + kind);
        }

        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);

        var described = await api.PostOkAsync<OperationDescriptionDto>(Operations.OperationDescribe, new { campaign_id = campaign, operation = "audience.search" }, Ct);
        var typeaheads = described.Definition!["keys"]!.AsObject()
            .Select(key => (string?)key.Value!["typeahead"])
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(kinds.Order(StringComparer.Ordinal), typeaheads.Order(StringComparer.Ordinal));
        foreach (var typeahead in typeaheads)
        {
            var values = await api.PostOkAsync<AudienceFilterValuesResponse>(
                Operations.AudienceFilterValues, new { campaign_id = campaign, filter = typeahead, query = string.Empty, limit = 5 }, Ct);
            Assert.Equal(["A value of " + typeahead], values.Values.Select(value => (string?)value));
        }
    }

    /// <summary>A definition written in Reply's shape reaches Reply as written: the preview's filters are the definition, whole.</summary>
    [Fact]
    public async Task A_definition_in_reply_s_shape_reaches_reply_as_written()
    {
        using var account = new ReplyAccount().WithPreview(1200, 340);
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        var definition = JsonNode.Parse("""
            {"jobTitles":[{"value":"Head of Operations","type":"include"},{"value":"Intern","type":"exclude"}],
             "seniorities":["vp","director"],
             "companySizes":["fifty","twoHundred"],
             "headcountGrowth":{"min":10},
             "departmentHeadcountGrowth":{"department":"Engineering","min":5,"max":40},
             "minRevenue":"oneMillion","maxRevenue":"fiftyMillion",
             "accountKeywords":{"values":[{"value":"freight","type":"include"}],"type":"or"},
             "contactKeywords":{"values":[{"value":"logistics","type":"include"}],"type":"and"},
             "industries":[{"value":"Logistics","type":"include"}],
             "companyLocations":[{"value":"Portugal","type":"include"}],
             "contactLocations":[{"value":"Porto","type":"include"}],
             "departments":[{"value":"Operations","type":"include"}],
             "isHiringOnLinkedIn":true}
            """)!;

        var preview = await api.PostOkAsync<AudiencePreviewResponse>(Operations.AudiencePreview, new { campaign_id = campaign, definition }, Ct);

        Assert.Equal((1200L, 340L), (preview.Count, preview.Companies));
        var call = Assert.Single(account.Calls);
        Assert.Equal("live-data preview", call.Line);
        Assert.True(JsonNode.DeepEquals(definition, JsonNode.Parse(call.Body!)), call.Body);
    }

    /// <summary>
    /// A definition Reply would refuse for a bound of its own is refused before Reply is asked, at the place it breaks it:
    /// more than twenty-five values in one filter, a value longer than two hundred characters or empty, a company size
    /// that is none of Reply's ten bands, a filter Reply does not take, and no filter at all — nothing, a list with no
    /// value, a hiring flag that is false, a growth range with neither bound, or keywords with none.
    /// </summary>
    [Theory]
    [InlineData("too_many", "/definition/jobTitles")]
    [InlineData("too_long", "/definition/jobTitles/0/value")]
    [InlineData("empty_value", "/definition/jobTitles/0/value")]
    [InlineData("no_type", "/definition/jobTitles/0/type")]
    [InlineData("no_band", "/definition/companySizes/0")]
    [InlineData("unknown_filter", "/definition/titles")]
    [InlineData("nothing", "/definition")]
    [InlineData("empty_list", "/definition/jobTitles")]
    [InlineData("hiring_false", "/definition/isHiringOnLinkedIn")]
    [InlineData("no_bound", "/definition/headcountGrowth")]
    [InlineData("no_keyword", "/definition/accountKeywords/values")]
    public async Task A_definition_past_reply_s_own_bounds_is_refused_before_reply_is_asked(string kind, string pointer)
    {
        using var account = new ReplyAccount();
        using var found = new ProcessVariable(ReplyAccount.ConfigHomeVariable, account.ConfigHome);
        await using var api = await StartAsync();
        var campaign = await CampaignAsync(api);
        var title = new JsonObject { ["value"] = "CTO", ["type"] = "include" };
        JsonNode definition = kind switch
        {
            "too_many" => new JsonObject { ["jobTitles"] = new JsonArray([.. Enumerable.Range(1, 26).Select(at => (JsonNode)new JsonObject { ["value"] = "Title " + at, ["type"] = "include" })]) },
            "too_long" => new JsonObject { ["jobTitles"] = new JsonArray(new JsonObject { ["value"] = new string('t', 201), ["type"] = "include" }) },
            "empty_value" => new JsonObject { ["jobTitles"] = new JsonArray(new JsonObject { ["value"] = string.Empty, ["type"] = "include" }) },
            "no_type" => new JsonObject { ["jobTitles"] = new JsonArray(new JsonObject { ["value"] = "CTO" }) },
            "no_band" => new JsonObject { ["companySizes"] = new JsonArray("51-200") },
            "unknown_filter" => new JsonObject { ["jobTitles"] = new JsonArray(title), ["titles"] = new JsonArray("CTO") },
            "nothing" => new JsonObject(),
            "empty_list" => new JsonObject { ["jobTitles"] = new JsonArray() },
            "hiring_false" => new JsonObject { ["isHiringOnLinkedIn"] = false },
            "no_bound" => new JsonObject { ["headcountGrowth"] = new JsonObject() },
            "no_keyword" => new JsonObject { ["accountKeywords"] = new JsonObject { ["values"] = new JsonArray(), ["type"] = "or" } },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var refused = await api.PostErrorAsync(Operations.AudiencePreview, new { campaign_id = campaign, definition }, HttpStatusCode.BadRequest, Ct);

        Assert.Equal(AttemptErrors.DefinitionInvalid, refused.Code);
        Assert.True(
            refused.Details!.Any(detail => detail.Field == pointer),
            string.Join("; ", refused.Details!.Select(detail => detail.Field + " " + detail.Code)));
        Assert.Empty(account.Calls);
    }

    private static Task<RuntimeApiFixture> StartAsync() =>
        ReplyPlugins.StartAsync(Ct, paths => TestRoutes.WriteGlobal(paths, TestRoutes.GlobalDefault(ReplyPlugins.PluginId)));

    private static async Task<string> CampaignAsync(RuntimeApiFixture api)
    {
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Q3 LatAm founders" }, Ct);
        return campaign.Id;
    }
}
