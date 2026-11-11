using Jason.Runtime.Audiences;
using Jason.Runtime.Contacts;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Tests.Approvals;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Persistence;

/// <summary>
/// Each read the poll, the claim, the landing and the people list make of a campaign's searches, and of where its people
/// came from, finds its rows through an index, so it costs the same at the thousandth search, and in an installation of
/// many campaigns and people, as at the first.
/// </summary>
public class AudienceSearchesSchemaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The poll reads only the searches still followed, off the index that holds nothing else, and a campaign's reads under
    /// way — a read or a stop — off the index of its work by status; the claim and the landing find a search's row by its work item's id.
    /// </summary>
    [Fact]
    public async Task The_poll_the_claim_and_the_landing_find_a_search_by_an_index()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        Assert.Equal(
            [
                "SCAN a USING INDEX ix_audience_searches_in_flight",
                "SEARCH c USING INTEGER PRIMARY KEY (rowid=?)",
                "SEARCH w USING INTEGER PRIMARY KEY (rowid=?)",
                "SEARCH w0 USING INTEGER PRIMARY KEY (rowid=?) LEFT-JOIN",
            ],
            await PlanAsync(db, SearchPoll.Following(db).ToQueryString()));
        Assert.Equal(
            ["SEARCH w USING INDEX ix_work_items_campaign_id_status (campaign_id=? AND status=?)"],
            await PlanAsync(db, AudienceSearches.OpenWork(db, [1, 2]).Select(item => item.Context).ToQueryString()));
        Assert.Equal(
            [
                "SEARCH w USING COVERING INDEX ix_work_items_public_id (public_id=?)",
                "SEARCH a USING INDEX ix_audience_searches_search_work_item_id (search_work_item_id=?)",
            ],
            await PlanAsync(db, AudienceSearches.Named(db, 1, "wi_1").Select(search => search.Cursor).ToQueryString()));
    }

    /// <summary>
    /// A page's people are found by the pins' lookup index and the channels' lookup index, a value at a time, never by a
    /// scan of the directory.
    /// </summary>
    [Fact]
    public async Task A_page_s_people_are_found_by_the_pins_and_the_channels_indexes()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        Assert.Equal(
            ["SEARCH e USING INDEX ix_external_ids_plugin_kind_value (plugin_id=? AND kind=? AND value=?)"],
            await PlanAsync(db, SearchLanding.Pinned(db, "stand-in-search", ["p-1", "p-2"]).Select(pin => pin.ContactId).ToQueryString()));
        Assert.Equal(
            [
                "MULTI-INDEX OR",
                "INDEX 1",
                "SEARCH c USING INDEX ix_contact_channels_channel_value (channel=? AND value=?)",
                "INDEX 2",
                "SEARCH c USING INDEX ix_contact_channels_channel_value (channel=? AND value=?)",
            ],
            await PlanAsync(db, SearchLanding.Reached(db, ["sam@quayside.example"], ["https://www.linkedin.com/in/sam"]).Select(channel => channel.ContactId).ToQueryString()));
    }

    /// <summary>
    /// The people list reads where each member came from by a left join on the landing rows' unique index: a member added
    /// by hand costs one probe of it, and the audience and the search are read by their keys.
    /// </summary>
    [Fact]
    public async Task The_people_list_reads_a_member_s_source_by_a_left_join_on_the_landing_rows()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        Assert.Equal(
            [
                "SEARCH c USING INDEX ix_campaign_contacts_campaign_id_contact_id (campaign_id=? AND contact_id=?)",
                "SEARCH a USING INDEX ix_audience_landings_campaign_contact_id (campaign_contact_id=?) LEFT-JOIN",
                "SEARCH a0 USING INTEGER PRIMARY KEY (rowid=?) LEFT-JOIN",
                "SEARCH w USING INTEGER PRIMARY KEY (rowid=?) LEFT-JOIN",
            ],
            await PlanAsync(db, MembershipService.Listed(db.CampaignContacts.Where(membership => membership.CampaignId == 1), [1, 2]).ToQueryString()));
    }

    /// <summary>
    /// The audience view reads a page of a campaign's audiences off the end of its index on their numbers, the next page from
    /// where the last stopped; each audience's latest searches off the end of the searches' index on the audience, with no
    /// sort, stopping at the limit; and each audience's counts by the same index: never a sort of the campaign's audiences or
    /// of an audience's searches, and never a scan.
    /// </summary>
    [Fact]
    public async Task The_audience_view_reads_a_campaign_s_audiences_their_searches_and_their_people_by_an_index()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        Assert.Equal(
            ["SEARCH a USING INDEX ix_audiences_campaign_id_number (campaign_id=?)"],
            await PlanAsync(db, CampaignAudienceView.Page(db, 1, null, 101).ToQueryString()));
        Assert.Equal(
            ["SEARCH a USING INDEX ix_audiences_campaign_id_number (campaign_id=? AND number<?)"],
            await PlanAsync(db, CampaignAudienceView.Page(db, 1, 4, 101).ToQueryString()));
        Assert.Equal(
            [
                "SEARCH a USING INTEGER PRIMARY KEY (rowid=?)",
                "LIST SUBQUERY 2",
                "SCAN json_each VIRTUAL TABLE INDEX 1:",
                "SEARCH s USING INTEGER PRIMARY KEY (rowid=?)",
                "CORRELATED LIST SUBQUERY 1",
                "SEARCH t USING COVERING INDEX ix_audience_searches_audience_id (audience_id=?)",
                "SEARCH w USING INTEGER PRIMARY KEY (rowid=?)",
            ],
            await PlanAsync(db, CampaignAudienceView.Searches(db, [1, 2], 10).ToQueryString()));
        Assert.Equal(
            ["SEARCH a USING INDEX ix_audience_searches_audience_id (audience_id=?)"],
            await PlanAsync(db, CampaignAudienceView.Tallies(db, [1, 2]).ToQueryString()));
    }

    private static Task<List<string>> PlanAsync(Jason.Runtime.Persistence.JasonDbContext db, string sql) => QueryPlans.PlanAsync(db, sql, Ct);
}
