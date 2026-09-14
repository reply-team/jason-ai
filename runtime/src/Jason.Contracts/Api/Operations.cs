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

    public const string JournalAppend = "journal.append";
    public const string JournalList = "journal.list";

    public const string SuppressionAdd = "suppression.add";
    public const string SuppressionRemove = "suppression.remove";
    public const string SuppressionList = "suppression.list";

    public static string Route(string operation) => $"/{ApiVersion.Current}/{operation}";
}
