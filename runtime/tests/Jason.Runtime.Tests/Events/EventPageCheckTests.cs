using System.Text.Json.Nodes;
using Jason.Contracts.Events;
using Jason.Runtime.Events;
using static Jason.Runtime.Tests.Events.EventStoreFixture;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// A read's result becomes a page one way: held to the events contract's output schema first. What the schema refuses —
/// a subject beside a moved thread — is never a page, so nothing it holds can reach the store.
/// </summary>
public class EventPageCheckTests
{
    [Fact]
    public void A_result_the_schema_refuses_is_no_page_and_says_why()
    {
        var moved = Moved();
        moved["subject"] = "Re: pricing";
        var result = new JsonObject { ["events"] = new JsonArray(moved), ["cursor"] = "c-1", ["has_more"] = false };

        var page = EventPage.Read(EventsCatalog.Find("events.read")!, result, out var problems);

        Assert.Null(page);
        var problem = Assert.Single(problems);
        Assert.Equal(("/events/0", "any_of"), (problem.Pointer, problem.Reason));
    }

    [Fact]
    public void A_result_the_schema_accepts_is_a_page()
    {
        var result = new JsonObject { ["events"] = new JsonArray(Moved(), Bounced()), ["cursor"] = "c-2", ["has_more"] = true };

        var page = EventPage.Read(EventsCatalog.Find("events.read")!, result, out var problems);

        Assert.Empty(problems);
        Assert.NotNull(page);
        Assert.Equal(("c-2", true, 2), (page.Cursor, page.HasMore, page.Events.Count));
    }
}
