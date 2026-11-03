using Jason.Contracts.Notifications;
using Jason.Runtime.Notifications.Desktop;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The toast's XML, as plain strings on every operating system: what a click launches, and the text unchanged. Nothing
/// here is loaded into Windows.
/// </summary>
public sealed class ToastXmlTests
{
    private const string Approval = "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6";

    private const string Question = "dec_01JBZ3X7Q9K2M4N6P8R0S2T4V6";

    private const string Visual =
        "<visual><binding template=\"ToastGeneric\"><text>Jason</text><text>1 approval is waiting in Jason.</text><text>Ask your agent.</text></binding></visual>";

    private static readonly string[] Lines = ["1 approval is waiting in Jason.", "Ask your agent."];

    /// <summary>Where the link registration is another program's or failed, the toast is the one shown before there was a link.</summary>
    [Fact]
    public void Without_a_link_the_toast_is_text_alone() =>
        Assert.Equal("<toast>" + Visual + "</toast>", ToastXml.Compose("Jason", Lines, launch: null));

    [Fact]
    public void One_approval_s_toast_launches_that_approval_by_protocol() =>
        Assert.Equal(
            $"<toast activationType=\"protocol\" launch=\"jason://attention/approvals/{Approval}\">" + Visual + "</toast>",
            ToastXml.Compose("Jason", Lines, AttentionLinks.Item(Approval)));

    [Fact]
    public void One_question_s_toast_launches_that_question_by_protocol() =>
        Assert.Contains(
            $"<toast activationType=\"protocol\" launch=\"jason://attention/questions/{Question}\">",
            ToastXml.Compose("Jason", Lines, AttentionLinks.Item(Question)),
            StringComparison.Ordinal);

    [Fact]
    public void Any_other_toast_launches_the_list() =>
        Assert.Equal(
            "<toast activationType=\"protocol\" launch=\"jason://attention\">" + Visual + "</toast>",
            ToastXml.Compose("Jason", Lines, AttentionLinks.List));

    /// <summary>A link names an item by the prefix the runtime makes its id with, never a copy that could drift.</summary>
    [Fact]
    public void The_links_prefixes_are_the_ones_the_runtime_makes_ids_with() =>
        Assert.Equal(
            (Jason.Runtime.Approvals.ApprovalGate.IdPrefix, Jason.Runtime.Decisions.DecisionService.IdPrefix),
            (AttentionLinks.ApprovalPrefix, AttentionLinks.QuestionPrefix));

    /// <summary>The composer makes no markup, but a title and lines are escaped as XML text whatever they hold.</summary>
    [Fact]
    public void The_title_and_lines_are_escaped_as_before() =>
        Assert.Equal(
            "<toast><visual><binding template=\"ToastGeneric\"><text>a &lt;b&gt; &amp; &quot;c&quot;</text><text>d&apos;e</text></binding></visual></toast>",
            ToastXml.Compose("a <b> & \"c\"", ["d'e"], launch: null));
}
