using Jason.Contracts.Journal;
using Jason.Runtime.Plugins.Subscriptions;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// A manifest's subscriptions and an order are held to the published contract of each kind, the documents under
/// <c>docs/contracts/journal/</c>: a kind is known where a document is published for it, and subscribable where that
/// document says so.
/// </summary>
public sealed class SubscribableKindsTests
{
    [Fact]
    public void Every_published_kind_stands_as_its_document_says()
    {
        foreach (var contract in JournalKindCatalog.All)
        {
            var expected = contract.Subscribable ? KindStanding.Subscribable : KindStanding.NotSubscribable;
            Assert.Equal(expected, SubscribableKinds.Catalog.Of(contract.Kind));
        }
    }

    [Fact]
    public void A_kind_no_document_is_published_for_is_unknown()
    {
        Assert.Equal(KindStanding.Unknown, SubscribableKinds.Catalog.Of("not_a_kind_we_write"));
        Assert.Equal(KindStanding.Unknown, SubscribableKinds.Catalog.Of("WORKITEM_FAILED"));
    }
}
