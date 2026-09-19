namespace Jason.Contracts.Api;

/// <summary>
/// Operation names shared by the runtime, the CLI and the skills. One vocabulary at three levels:
/// canonical operation <c>system.info</c> ↔ <c>POST /v1/system.info</c> ↔ <c>jason system info</c>.
/// </summary>
public static class Operations
{
    public const string SystemInfo = "system.info";
    public const string SystemShutdown = "system.shutdown";

    public const string CampaignCreate = "campaign.create";
    public const string CampaignGet = "campaign.get";
    public const string CampaignList = "campaign.list";
    public const string CampaignUpdate = "campaign.update";
    public const string CampaignStart = "campaign.start";
    public const string CampaignPause = "campaign.pause";
    public const string CampaignArchive = "campaign.archive";
    public const string CampaignUpdateContext = "campaign.update_context";
    public const string CampaignAddContacts = "campaign.add_contacts";
    public const string CampaignRemoveContacts = "campaign.remove_contacts";
    public const string CampaignListContacts = "campaign.list_contacts";

    public const string ContactCreate = "contact.create";
    public const string ContactGet = "contact.get";
    public const string ContactList = "contact.list";
    public const string ContactUpdate = "contact.update";
    public const string ContactArchive = "contact.archive";

    public const string WorkItemCreate = "workitem.create";
    public const string WorkItemGet = "workitem.get";
    public const string WorkItemList = "workitem.list";
    public const string WorkItemUpdate = "workitem.update";
    public const string WorkItemCancel = "workitem.cancel";
    public const string WorkItemHeartbeat = "workitem.heartbeat";
    public const string WorkItemSetResult = "workitem.set_result";
    public const string WorkItemComplete = "workitem.complete";

    public const string ApprovalList = "approval.list";
    public const string ApprovalGet = "approval.get";
    public const string ApprovalApprove = "approval.approve";
    public const string ApprovalReject = "approval.reject";

    public const string DecisionRaise = "decision.raise";
    public const string DecisionAnswer = "decision.answer";
    public const string DecisionGet = "decision.get";
    public const string DecisionList = "decision.list";

    public const string ReportSubmit = "report.submit";
    public const string ReportGet = "report.get";
    public const string ReportList = "report.list";

    public const string RoleList = "role.list";
    public const string RoleAdd = "role.add";
    public const string RoleSetProfile = "role.set_profile";

    public const string RoleNoteGet = "rolenote.get";
    public const string RoleNoteSet = "rolenote.set";
    public const string RoleNoteList = "rolenote.list";

    public const string ProfileCreate = "profile.create";
    public const string ProfileUpdate = "profile.update";
    public const string ProfileGet = "profile.get";
    public const string ProfileList = "profile.list";
    public const string ProfileDisable = "profile.disable";
    public const string ProfileEnable = "profile.enable";

    public const string JournalAppend = "journal.append";
    public const string JournalList = "journal.list";

    public const string PluginList = "plugin.list";
    public const string PluginReload = "plugin.reload";

    public const string RouteResolve = "route.resolve";
    public const string RouteList = "route.list";
    public const string RouteSet = "route.set";
    public const string RouteUnset = "route.unset";

    public const string SuppressionAdd = "suppression.add";
    public const string SuppressionRemove = "suppression.remove";
    public const string SuppressionList = "suppression.list";

    public static string Route(string operation) => $"/{ApiVersion.Current}/{operation}";
}
