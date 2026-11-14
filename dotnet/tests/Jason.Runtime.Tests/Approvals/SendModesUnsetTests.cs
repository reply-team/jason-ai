using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Approvals;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Preferences;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// The installation's open question, carried by every approval answer until it is answered: which of the two send-mode
/// preferences nobody has set yet. It is on every answer whatever the approval is about and however it stands, always
/// present, empty once both are set; nothing is stored about having asked it, so an unset brings a key back.
/// </summary>
public class SendModesUnsetTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ActorRef Marta = new(ActorType.Human, "Marta");

    [Fact]
    public async Task Every_answer_carries_the_keys_nobody_has_set_until_both_are_and_again_after_an_unset()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(new DateTimeOffset(ParkedWork.Noon));
        var approvals = new ApprovalService(db, new JournalWriter(clock), clock);
        var preferences = new PreferenceService(db, new JournalWriter(clock), clock);
        var (_, pending) = await ParkedWork.WriteAsync(db, Ct);
        var (_, decided) = await ParkedWork.WriteAsync(db, Ct);

        async Task<IReadOnlyList<string>> Get(Approval approval) =>
            (await approvals.GetAsync(new ApprovalGetRequest(approval.PublicId), Ct)).SendModesUnset!;

        Assert.Equal([SendModes.ColdKey, SendModes.WarmKey], await Get(pending));

        await preferences.SetAsync(new PreferenceSetRequest(SendModes.ColdKey, Mode("automatic"), Marta, null), Ct);
        Assert.Equal([SendModes.WarmKey], await Get(pending));

        // A decided approval carries it too: the answer is the installation's fact, not the approval's.
        var rejected = await approvals.RejectAsync(new ApprovalDecisionRequest(decided.PublicId, Marta, "not this one"), Ct);
        Assert.Equal([SendModes.WarmKey], rejected.SendModesUnset);

        await preferences.SetAsync(new PreferenceSetRequest(SendModes.WarmKey, Mode("every_message"), Marta, null), Ct);
        Assert.Empty(await Get(pending));
        Assert.Empty(await Get(decided));
        var page = await approvals.ListAsync(new ApprovalListRequest(null, null, null, null, null, [ApprovalStatus.Pending, ApprovalStatus.Rejected]), Ct);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, item => Assert.Empty(item.SendModesUnset!));

        await preferences.UnsetAsync(new PreferenceUnsetRequest(SendModes.ColdKey, Marta, null), Ct);
        Assert.Equal([SendModes.ColdKey], await Get(pending));
        var approved = await approvals.ApproveAsync(new ApprovalDecisionRequest(pending.PublicId, Marta, null), Ct);
        Assert.Equal([SendModes.ColdKey], approved.SendModesUnset);
    }

    /// <summary>Always present, as a list, empty rather than absent once both are set.</summary>
    [Fact]
    public async Task The_member_is_always_written_and_an_empty_list_once_both_are_set()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(new DateTimeOffset(ParkedWork.Noon));
        var approvals = new ApprovalService(db, new JournalWriter(clock), clock);
        var preferences = new PreferenceService(db, new JournalWriter(clock), clock);
        var (_, approval) = await ParkedWork.WriteAsync(db, Ct);

        var open = JsonSerializer.Serialize(await approvals.GetAsync(new ApprovalGetRequest(approval.PublicId), Ct), JasonJson.Options);
        Assert.Contains("\"send_modes_unset\":[\"send_mode.cold\",\"send_mode.warm\"]", open, StringComparison.Ordinal);

        await preferences.SetAsync(new PreferenceSetRequest(SendModes.ColdKey, Mode("automatic"), Marta, null), Ct);
        await preferences.SetAsync(new PreferenceSetRequest(SendModes.WarmKey, Mode("automatic"), Marta, null), Ct);
        var answered = JsonSerializer.Serialize(
            await approvals.ListAsync(new ApprovalListRequest(null, null, null, null, null), Ct), JasonJson.Options);
        Assert.Contains("\"send_modes_unset\":[]", answered, StringComparison.Ordinal);
    }

    /// <summary>Over the API, as a page reads it: one key unset on a listing, none on an approval once both are chosen.</summary>
    [Fact]
    public async Task The_api_answers_carry_it_as_recorded()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(new DateTimeOffset(ParkedWork.Noon)));
        string id;
        using (var scope = api.Resolve<IServiceScopeFactory>().CreateScope())
        {
            id = (await ParkedWork.WriteAsync(scope.ServiceProvider.GetRequiredService<JasonDbContext>(), Ct)).Approval.PublicId;
        }

        var marta = new { type = "human", id = "Marta" };
        await api.PostOkAsync<PreferenceDto>(Operations.PreferenceSet, new { key = SendModes.ColdKey, value = new { mode = "automatic" }, actor = marta }, Ct);
        var listed = await api.RecordAsync<Page<ApprovalSummaryDto>>(Operations.ApprovalList, "one-mode-unset", new { }, Ct);
        Assert.Equal([SendModes.WarmKey], Assert.Single(listed.Items).SendModesUnset);

        await api.PostOkAsync<PreferenceDto>(Operations.PreferenceSet, new { key = SendModes.WarmKey, value = new { mode = "every_message" }, actor = marta }, Ct);
        var chosen = await api.RecordAsync<ApprovalDto>(Operations.ApprovalGet, "modes-chosen", new { approval_id = id }, Ct);
        Assert.Empty(chosen.SendModesUnset!);
    }

    private static JsonObject Mode(string word) => new() { ["mode"] = word };
}
