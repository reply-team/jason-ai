// Guard 2, the UI's half: every recorded exchange imported here is held to the UI's own types by `satisfies`.
// A field renamed in the runtime fails `dotnet test` first (the recording), then the typecheck here.
import type {
    Approval,
    ApprovalDecisionRequest,
    ApprovalListRequest,
    ApprovalSummary,
    Decision,
    DecisionAnswerRequest,
    DecisionListRequest,
    DecisionSummary,
    ErrorEnvelope,
    Page,
    Routes,
    SystemInfo,
    UiRevoke,
    UiRevokeRequest,
    UiTokens,
    CampaignListRequest,
    CampaignSummary,
    Campaign,
    CampaignTransitionRequest,
    CampaignSummaryRequest,
    CampaignSummaryResponse,
    CampaignActivityRequest,
    CampaignActivityResponse,
    ListContactsRequest,
    MembershipItem,
    Contact,
    ContactListRequest,
    JournalEntry,
    JournalListRequest,
    Role,
    WorkItem,
    WorkItemCancelRequest,
    WorkItemCount,
    WorkItemFilterRequest,
    WorkItemListRequest,
    WorkItemSummary,
    WorkItemUpdateRequest,
    AddContactsRequest,
    AddContactsResult,
    ContactThreadsRequest,
    ContactThreads,
    ContactThreadRequest,
    ContactThread,
} from '../api/types';

import approvalApproveAlreadyDecided from '../../../docs/api/fixtures/approval.approve/already-decided.json';
import approvalApproveApproved from '../../../docs/api/fixtures/approval.approve/approved.json';
import approvalGetPendingWithPreview from '../../../docs/api/fixtures/approval.get/pending-with-preview.json';
import approvalListDecided from '../../../docs/api/fixtures/approval.list/decided.json';
import approvalListEmpty from '../../../docs/api/fixtures/approval.list/empty.json';
import approvalListPending from '../../../docs/api/fixtures/approval.list/pending.json';
import approvalListUnauthorized from '../../../docs/api/fixtures/approval.list/unauthorized.json';
import approvalRejectRejected from '../../../docs/api/fixtures/approval.reject/rejected.json';
import decisionAnswerAnswered from '../../../docs/api/fixtures/decision.answer/answered.json';
import decisionGetPendingWithOptions from '../../../docs/api/fixtures/decision.get/pending-with-options.json';
import decisionListEmpty from '../../../docs/api/fixtures/decision.list/empty.json';
import decisionListPending from '../../../docs/api/fixtures/decision.list/pending.json';
import pluginListNone from '../../../docs/api/fixtures/plugin.list/none.json';
import routeListEmpty from '../../../docs/api/fixtures/route.list/empty.json';
import systemInfoWithUi from '../../../docs/api/fixtures/system.info/with-ui.json';
import systemInfoWithoutUi from '../../../docs/api/fixtures/system.info/without-ui.json';
import uiRevokeOne from '../../../docs/api/fixtures/ui.revoke/one.json';
import uiTokensAged from '../../../docs/api/fixtures/ui.tokens/aged.json';
import uiTokensNone from '../../../docs/api/fixtures/ui.tokens/none.json';
import uiTokensTwo from '../../../docs/api/fixtures/ui.tokens/two.json';
import campaignAddContactsOnePerson from '../../../docs/api/fixtures/campaign.add_contacts/one-person.json';
import campaignAddContactsReEnrolExcluded from '../../../docs/api/fixtures/campaign.add_contacts/re-enrol-excluded.json';
import campaignAddContactsAlreadyMember from '../../../docs/api/fixtures/campaign.add_contacts/already-member.json';
import campaignAddContactsArchivedCampaign from '../../../docs/api/fixtures/campaign.add_contacts/archived-campaign.json';
import campaignListForPicker from '../../../docs/api/fixtures/campaign.list/for-picker.json';
import contactGetWithCampaigns from '../../../docs/api/fixtures/contact.get/with-campaigns.json';
import contactGetNoFirstName from '../../../docs/api/fixtures/contact.get/no-first-name.json';
import contactGetArchived from '../../../docs/api/fixtures/contact.get/archived.json';
import contactListByActivity from '../../../docs/api/fixtures/contact.list/by-activity.json';
import contactListByActivityNext from '../../../docs/api/fixtures/contact.list/by-activity-next.json';
import contactListByActivityQuery from '../../../docs/api/fixtures/contact.list/by-activity-query.json';
import contactListByCampaign from '../../../docs/api/fixtures/contact.list/by-campaign.json';
import contactListArchived from '../../../docs/api/fixtures/contact.list/archived.json';
import contactThreadsTwoThreads from '../../../docs/api/fixtures/contact.threads/two-threads.json';
import contactThreadsHasMore from '../../../docs/api/fixtures/contact.threads/has-more.json';
import contactThreadsProviderFailed from '../../../docs/api/fixtures/contact.threads/provider-failed.json';
import contactThreadsNotLinked from '../../../docs/api/fixtures/contact.threads/not-linked.json';
import contactThreadsNoRoute from '../../../docs/api/fixtures/contact.threads/no-route.json';
import contactThreadMessages from '../../../docs/api/fixtures/contact.thread/messages.json';
import contactThreadHasMore from '../../../docs/api/fixtures/contact.thread/has-more.json';
import contactThreadNotThisContact from '../../../docs/api/fixtures/contact.thread/not-this-contact.json';
import journalListByContact from '../../../docs/api/fixtures/journal.list/by-contact.json';
import journalListByContactReplies from '../../../docs/api/fixtures/journal.list/by-contact-replies.json';
import journalListByContactWork from '../../../docs/api/fixtures/journal.list/by-contact-work.json';
import journalListByContactDecisions from '../../../docs/api/fixtures/journal.list/by-contact-decisions.json';
import journalListByContactChanges from '../../../docs/api/fixtures/journal.list/by-contact-changes.json';
import journalListByContactFailed from '../../../docs/api/fixtures/journal.list/by-contact-failed.json';
import journalListByContactEmpty from '../../../docs/api/fixtures/journal.list/by-contact-empty.json';
import workitemListByContactOpen from '../../../docs/api/fixtures/workitem.list/by-contact-open.json';
import approvalListByWorkItem from '../../../docs/api/fixtures/approval.list/by-work-item.json';
import campaignGetOne from '../../../docs/api/fixtures/campaign.get/one.json';
import campaignGetUnknown from '../../../docs/api/fixtures/campaign.get/unknown.json';
import campaignListFirstPage from '../../../docs/api/fixtures/campaign.list/first-page.json';
import campaignListQuery from '../../../docs/api/fixtures/campaign.list/query.json';
import contactListFirstPage from '../../../docs/api/fixtures/contact.list/first-page.json';
import contactGetOne from '../../../docs/api/fixtures/contact.get/one.json';
import contactListQueryEmail from '../../../docs/api/fixtures/contact.list/query-email.json';
import contactListQueryNone from '../../../docs/api/fixtures/contact.list/query-none.json';
import contactListQueryPhone from '../../../docs/api/fixtures/contact.list/query-phone.json';
import journalListByWorkItem from '../../../docs/api/fixtures/journal.list/by-work-item.json';
import roleListAll from '../../../docs/api/fixtures/role.list/all.json';
import workitemCancelReason from '../../../docs/api/fixtures/workitem.cancel/reason.json';
import workitemCountDone from '../../../docs/api/fixtures/workitem.count/done.json';
import workitemCountFailed from '../../../docs/api/fixtures/workitem.count/failed.json';
import workitemCountFiltered from '../../../docs/api/fixtures/workitem.count/filtered.json';
import workitemCountNow from '../../../docs/api/fixtures/workitem.count/now.json';
import workitemCountPlanned from '../../../docs/api/fixtures/workitem.count/planned.json';
import workitemCountZero from '../../../docs/api/fixtures/workitem.count/zero.json';
import workitemGetAwaiting from '../../../docs/api/fixtures/workitem.get/awaiting.json';
import workitemGetExpired from '../../../docs/api/fixtures/workitem.get/expired.json';
import workitemGetFailed from '../../../docs/api/fixtures/workitem.get/failed.json';
import workitemGetOpen from '../../../docs/api/fixtures/workitem.get/open.json';
import workitemGetSucceeded from '../../../docs/api/fixtures/workitem.get/succeeded.json';
import workitemListByContact from '../../../docs/api/fixtures/workitem.list/by-contact.json';
import workitemListDone from '../../../docs/api/fixtures/workitem.list/done.json';
import workitemListEmpty from '../../../docs/api/fixtures/workitem.list/empty.json';
import workitemListFailed from '../../../docs/api/fixtures/workitem.list/failed.json';
import workitemListNow from '../../../docs/api/fixtures/workitem.list/now.json';
import workitemListPlanned from '../../../docs/api/fixtures/workitem.list/planned.json';
import workitemListPlannedNext from '../../../docs/api/fixtures/workitem.list/planned-next.json';
import workitemUpdateDates from '../../../docs/api/fixtures/workitem.update/dates.json';
import workitemUpdateTerminal from '../../../docs/api/fixtures/workitem.update/terminal.json';
import approvalGetForCampaignRow from '../../../docs/api/fixtures/approval.get/for-campaign-row.json';
import campaignActivityLast7Days from '../../../docs/api/fixtures/campaign.activity/last-7-days.json';
import campaignActivityOne from '../../../docs/api/fixtures/campaign.activity/one.json';
import campaignActivityToday from '../../../docs/api/fixtures/campaign.activity/today.json';
import campaignAddContactsSeveral from '../../../docs/api/fixtures/campaign.add_contacts/several.json';
import campaignGetDetails from '../../../docs/api/fixtures/campaign.get/details.json';
import campaignListIncludeArchived from '../../../docs/api/fixtures/campaign.list/include-archived.json';
import campaignListScreenQuery from '../../../docs/api/fixtures/campaign.list/screen-query.json';
import campaignListScreen from '../../../docs/api/fixtures/campaign.list/screen.json';
import campaignListContactsActivityReplied from '../../../docs/api/fixtures/campaign.list_contacts/activity-replied.json';
import campaignListContactsQuery from '../../../docs/api/fixtures/campaign.list_contacts/query.json';
import campaignListContactsSummaryFailed from '../../../docs/api/fixtures/campaign.list_contacts/summary-failed.json';
import campaignListContactsSummaryWaiting from '../../../docs/api/fixtures/campaign.list_contacts/summary-waiting.json';
import campaignPauseWithReason from '../../../docs/api/fixtures/campaign.pause/with-reason.json';
import campaignStartAfterPause from '../../../docs/api/fixtures/campaign.start/after-pause.json';
import campaignSummaryAll from '../../../docs/api/fixtures/campaign.summary/all.json';
import campaignSummarySome from '../../../docs/api/fixtures/campaign.summary/some.json';
import campaignSummaryWithArchived from '../../../docs/api/fixtures/campaign.summary/with-archived.json';
import decisionGetForCampaignRow from '../../../docs/api/fixtures/decision.get/for-campaign-row.json';
import journalListByCampaignChanges from '../../../docs/api/fixtures/journal.list/by-campaign-changes.json';
import journalListByCampaign from '../../../docs/api/fixtures/journal.list/by-campaign.json';
import approvalApproveFromCampaignRow from '../../../docs/api/fixtures/approval.approve/from-campaign-row.json';
import campaignSummaryAllAfter from '../../../docs/api/fixtures/campaign.summary/all-after.json';
import campaignListContactsSummaryRemoved from '../../../docs/api/fixtures/campaign.list_contacts/summary-removed.json';
import campaignListContactsActivityRemoved from '../../../docs/api/fixtures/campaign.list_contacts/activity-removed.json';
import campaignListContactsActivityPeople from '../../../docs/api/fixtures/campaign.list_contacts/activity-people.json';
import campaignListContactsEveryone from '../../../docs/api/fixtures/campaign.list_contacts/everyone.json';
import campaignSummaryArchived from '../../../docs/api/fixtures/campaign.summary/archived.json';
import approvalApproveFromCampaignRowAgain from '../../../docs/api/fixtures/approval.approve/from-campaign-row-again.json';
import campaignActivityMany from '../../../docs/api/fixtures/campaign.activity/many.json';
import campaignActivityManyOne from '../../../docs/api/fixtures/campaign.activity/many-one.json';
import campaignAddContactsToArchived from '../../../docs/api/fixtures/campaign.add_contacts/to-archived.json';
import campaignGetMany from '../../../docs/api/fixtures/campaign.get/many.json';
import campaignListContactsActivityMany from '../../../docs/api/fixtures/campaign.list_contacts/activity-many.json';
import campaignListContactsActivityManyNext from '../../../docs/api/fixtures/campaign.list_contacts/activity-many-next.json';
import contactListCampaignPicker from '../../../docs/api/fixtures/contact.list/campaign-picker.json';
import contactListCampaignPickerZoe from '../../../docs/api/fixtures/contact.list/campaign-picker-zoe.json';
import campaignListContactsSummaryWaitingAfter from '../../../docs/api/fixtures/campaign.list_contacts/summary-waiting-after.json';
import campaignSummarySomeAfter from '../../../docs/api/fixtures/campaign.summary/some-after.json';
import approvalListByCampaignPending from '../../../docs/api/fixtures/approval.list/by-campaign-pending.json';
import decisionListByCampaignPending from '../../../docs/api/fixtures/decision.list/by-campaign-pending.json';
import campaignGetArchived from '../../../docs/api/fixtures/campaign.get/archived.json';
import campaignGetUnlinked from '../../../docs/api/fixtures/campaign.get/unlinked.json';
import campaignListScreenManyNext from '../../../docs/api/fixtures/campaign.list/screen-many-next.json';
import campaignListScreenMany from '../../../docs/api/fixtures/campaign.list/screen-many.json';
import campaignListContactsQueryTooLong from '../../../docs/api/fixtures/campaign.list_contacts/query-too-long.json';
import campaignListContactsUnlinked from '../../../docs/api/fixtures/campaign.list_contacts/unlinked.json';
import campaignSummaryManyNext from '../../../docs/api/fixtures/campaign.summary/many-next.json';
import campaignSummaryMany from '../../../docs/api/fixtures/campaign.summary/many.json';
import campaignSummaryUnlinked from '../../../docs/api/fixtures/campaign.summary/unlinked.json';
import workitemListByCampaignPlanned from '../../../docs/api/fixtures/workitem.list/by-campaign-planned.json';
import workitemListByCampaignRunning from '../../../docs/api/fixtures/workitem.list/by-campaign-running.json';

export type Exchange<Req, Res> = { operation: string; case: string; request: Req; status: number; response: Res };

export const fixtures = {
    'approval.approve/already-decided': approvalApproveAlreadyDecided satisfies Exchange<
        ApprovalDecisionRequest,
        ErrorEnvelope
    >,
    'approval.approve/approved': approvalApproveApproved satisfies Exchange<ApprovalDecisionRequest, Approval>,
    'approval.get/pending-with-preview': approvalGetPendingWithPreview satisfies Exchange<
        { approval_id: string },
        Approval
    >,
    'approval.list/decided': approvalListDecided satisfies Exchange<ApprovalListRequest, Page<ApprovalSummary>>,
    'approval.list/empty': approvalListEmpty satisfies Exchange<ApprovalListRequest, Page<ApprovalSummary>>,
    'approval.list/pending': approvalListPending satisfies Exchange<ApprovalListRequest, Page<ApprovalSummary>>,
    'approval.list/unauthorized': approvalListUnauthorized satisfies Exchange<ApprovalListRequest, ErrorEnvelope>,
    'approval.reject/rejected': approvalRejectRejected satisfies Exchange<ApprovalDecisionRequest, Approval>,
    'decision.answer/answered': decisionAnswerAnswered satisfies Exchange<DecisionAnswerRequest, Decision>,
    'decision.get/pending-with-options': decisionGetPendingWithOptions satisfies Exchange<
        { decision_id: string },
        Decision
    >,
    'decision.list/empty': decisionListEmpty satisfies Exchange<DecisionListRequest, Page<DecisionSummary>>,
    'decision.list/pending': decisionListPending satisfies Exchange<DecisionListRequest, Page<DecisionSummary>>,
    'plugin.list/none': pluginListNone satisfies Exchange<object, object>,
    'route.list/empty': routeListEmpty satisfies Exchange<object, Routes>,
    'system.info/with-ui': systemInfoWithUi satisfies Exchange<object, SystemInfo>,
    'system.info/without-ui': systemInfoWithoutUi satisfies Exchange<object, SystemInfo>,
    'ui.revoke/one': uiRevokeOne satisfies Exchange<UiRevokeRequest, UiRevoke>,
    'ui.tokens/aged': uiTokensAged satisfies Exchange<object, UiTokens>,
    'ui.tokens/none': uiTokensNone satisfies Exchange<object, UiTokens>,
    'ui.tokens/two': uiTokensTwo satisfies Exchange<object, UiTokens>,
    'approval.list/by-work-item': approvalListByWorkItem satisfies Exchange<
        ApprovalListRequest & { work_item_id: string },
        Page<ApprovalSummary>
    >,
    'campaign.get/one': campaignGetOne satisfies Exchange<{ campaign_id: string }, CampaignSummary>,
    'campaign.get/unknown': campaignGetUnknown satisfies Exchange<{ campaign_id: string }, ErrorEnvelope>,
    'campaign.list/first-page': campaignListFirstPage satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'campaign.list/query': campaignListQuery satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'contact.list/first-page': contactListFirstPage satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.get/one': contactGetOne satisfies Exchange<{ contact_id: string }, Contact>,
    'contact.list/query-email': contactListQueryEmail satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/query-none': contactListQueryNone satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/query-phone': contactListQueryPhone satisfies Exchange<ContactListRequest, Page<Contact>>,
    'journal.list/by-work-item': journalListByWorkItem satisfies Exchange<JournalListRequest, Page<JournalEntry>>,
    'role.list/all': roleListAll satisfies Exchange<object, Page<Role>>,
    'workitem.cancel/reason': workitemCancelReason satisfies Exchange<WorkItemCancelRequest, WorkItem>,
    'workitem.count/done': workitemCountDone satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/failed': workitemCountFailed satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/filtered': workitemCountFiltered satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/now': workitemCountNow satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/planned': workitemCountPlanned satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/zero': workitemCountZero satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.get/awaiting': workitemGetAwaiting satisfies Exchange<{ work_item_id: string }, WorkItem>,
    'workitem.get/expired': workitemGetExpired satisfies Exchange<{ work_item_id: string }, WorkItem>,
    'workitem.get/failed': workitemGetFailed satisfies Exchange<{ work_item_id: string }, WorkItem>,
    'workitem.get/open': workitemGetOpen satisfies Exchange<{ work_item_id: string }, WorkItem>,
    'workitem.get/succeeded': workitemGetSucceeded satisfies Exchange<{ work_item_id: string }, WorkItem>,
    'workitem.list/by-contact': workitemListByContact satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/done': workitemListDone satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/empty': workitemListEmpty satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/failed': workitemListFailed satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/now': workitemListNow satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/planned': workitemListPlanned satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/planned-next': workitemListPlannedNext satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.update/dates': workitemUpdateDates satisfies Exchange<WorkItemUpdateRequest, WorkItem>,
    'workitem.update/terminal': workitemUpdateTerminal satisfies Exchange<WorkItemUpdateRequest, ErrorEnvelope>,
    'campaign.add_contacts/one-person': campaignAddContactsOnePerson satisfies Exchange<
        AddContactsRequest,
        AddContactsResult
    >,
    'campaign.add_contacts/re-enrol-excluded': campaignAddContactsReEnrolExcluded satisfies Exchange<
        AddContactsRequest,
        AddContactsResult
    >,
    'campaign.add_contacts/already-member': campaignAddContactsAlreadyMember satisfies Exchange<
        AddContactsRequest,
        AddContactsResult
    >,
    'campaign.add_contacts/archived-campaign': campaignAddContactsArchivedCampaign satisfies Exchange<
        AddContactsRequest,
        ErrorEnvelope
    >,
    'campaign.list/for-picker': campaignListForPicker satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'contact.get/with-campaigns': contactGetWithCampaigns satisfies Exchange<{ contact_id: string }, Contact>,
    'contact.get/no-first-name': contactGetNoFirstName satisfies Exchange<{ contact_id: string }, Contact>,
    'contact.get/archived': contactGetArchived satisfies Exchange<{ contact_id: string }, Contact>,
    'contact.list/by-activity': contactListByActivity satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/by-activity-next': contactListByActivityNext satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/by-activity-query': contactListByActivityQuery satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/by-campaign': contactListByCampaign satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/archived': contactListArchived satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.threads/two-threads': contactThreadsTwoThreads satisfies Exchange<ContactThreadsRequest, ContactThreads>,
    'contact.threads/has-more': contactThreadsHasMore satisfies Exchange<ContactThreadsRequest, ContactThreads>,
    'contact.threads/provider-failed': contactThreadsProviderFailed satisfies Exchange<
        ContactThreadsRequest,
        ErrorEnvelope
    >,
    'contact.threads/not-linked': contactThreadsNotLinked satisfies Exchange<ContactThreadsRequest, ErrorEnvelope>,
    'contact.threads/no-route': contactThreadsNoRoute satisfies Exchange<ContactThreadsRequest, ErrorEnvelope>,
    'contact.thread/messages': contactThreadMessages satisfies Exchange<ContactThreadRequest, ContactThread>,
    'contact.thread/has-more': contactThreadHasMore satisfies Exchange<ContactThreadRequest, ContactThread>,
    'contact.thread/not-this-contact': contactThreadNotThisContact satisfies Exchange<
        ContactThreadRequest,
        ErrorEnvelope
    >,
    'journal.list/by-contact': journalListByContact satisfies Exchange<JournalListRequest, Page<JournalEntry>>,
    'journal.list/by-contact-replies': journalListByContactReplies satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/by-contact-work': journalListByContactWork satisfies Exchange<JournalListRequest, Page<JournalEntry>>,
    'journal.list/by-contact-decisions': journalListByContactDecisions satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/by-contact-changes': journalListByContactChanges satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/by-contact-failed': journalListByContactFailed satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/by-contact-empty': journalListByContactEmpty satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'workitem.list/by-contact-open': workitemListByContactOpen satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'approval.get/for-campaign-row': approvalGetForCampaignRow satisfies Exchange<{ approval_id: string }, Approval>,
    'campaign.activity/last-7-days': campaignActivityLast7Days satisfies Exchange<
        CampaignActivityRequest,
        CampaignActivityResponse
    >,
    'campaign.activity/one': campaignActivityOne satisfies Exchange<CampaignActivityRequest, CampaignActivityResponse>,
    'campaign.activity/today': campaignActivityToday satisfies Exchange<
        CampaignActivityRequest,
        CampaignActivityResponse
    >,
    'campaign.add_contacts/several': campaignAddContactsSeveral satisfies Exchange<
        AddContactsRequest,
        AddContactsResult
    >,
    'campaign.get/details': campaignGetDetails satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.list/include-archived': campaignListIncludeArchived satisfies Exchange<
        CampaignListRequest,
        Page<CampaignSummary>
    >,
    'campaign.list/screen-query': campaignListScreenQuery satisfies Exchange<
        CampaignListRequest,
        Page<CampaignSummary>
    >,
    'campaign.list/screen': campaignListScreen satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'campaign.list_contacts/activity-replied': campaignListContactsActivityReplied satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/query': campaignListContactsQuery satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/summary-failed': campaignListContactsSummaryFailed satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/summary-waiting': campaignListContactsSummaryWaiting satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.pause/with-reason': campaignPauseWithReason satisfies Exchange<CampaignTransitionRequest, Campaign>,
    'campaign.start/after-pause': campaignStartAfterPause satisfies Exchange<CampaignTransitionRequest, Campaign>,
    'campaign.summary/all': campaignSummaryAll satisfies Exchange<CampaignSummaryRequest, CampaignSummaryResponse>,
    'campaign.summary/some': campaignSummarySome satisfies Exchange<CampaignSummaryRequest, CampaignSummaryResponse>,
    'campaign.summary/with-archived': campaignSummaryWithArchived satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'decision.get/for-campaign-row': decisionGetForCampaignRow satisfies Exchange<{ decision_id: string }, Decision>,
    'journal.list/by-campaign-changes': journalListByCampaignChanges satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/by-campaign': journalListByCampaign satisfies Exchange<JournalListRequest, Page<JournalEntry>>,
    'approval.approve/from-campaign-row-again': approvalApproveFromCampaignRowAgain satisfies Exchange<
        ApprovalDecisionRequest,
        ErrorEnvelope
    >,
    'campaign.activity/many': campaignActivityMany satisfies Exchange<
        CampaignActivityRequest,
        CampaignActivityResponse
    >,
    'campaign.activity/many-one': campaignActivityManyOne satisfies Exchange<
        CampaignActivityRequest,
        CampaignActivityResponse
    >,
    'campaign.add_contacts/to-archived': campaignAddContactsToArchived satisfies Exchange<
        AddContactsRequest,
        ErrorEnvelope
    >,
    'campaign.get/many': campaignGetMany satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.list_contacts/activity-many': campaignListContactsActivityMany satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/activity-many-next': campaignListContactsActivityManyNext satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'contact.list/campaign-picker': contactListCampaignPicker satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/campaign-picker-zoe': contactListCampaignPickerZoe satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'campaign.list_contacts/everyone': campaignListContactsEveryone satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.summary/archived': campaignSummaryArchived satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.list_contacts/summary-removed': campaignListContactsSummaryRemoved satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/activity-removed': campaignListContactsActivityRemoved satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/activity-people': campaignListContactsActivityPeople satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.summary/all-after': campaignSummaryAllAfter satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'approval.approve/from-campaign-row': approvalApproveFromCampaignRow satisfies Exchange<
        ApprovalDecisionRequest,
        Approval
    >,
    'campaign.list_contacts/summary-waiting-after': campaignListContactsSummaryWaitingAfter satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.summary/some-after': campaignSummarySomeAfter satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'approval.list/by-campaign-pending': approvalListByCampaignPending satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'decision.list/by-campaign-pending': decisionListByCampaignPending satisfies Exchange<
        DecisionListRequest,
        Page<DecisionSummary>
    >,
    'campaign.get/archived': campaignGetArchived satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/unlinked': campaignGetUnlinked satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.list/screen-many-next': campaignListScreenManyNext satisfies Exchange<
        CampaignListRequest,
        Page<CampaignSummary>
    >,
    'campaign.list/screen-many': campaignListScreenMany satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'campaign.list_contacts/query-too-long': campaignListContactsQueryTooLong satisfies Exchange<
        ListContactsRequest,
        ErrorEnvelope
    >,
    'campaign.list_contacts/unlinked': campaignListContactsUnlinked satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.summary/many-next': campaignSummaryManyNext satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/many': campaignSummaryMany satisfies Exchange<CampaignSummaryRequest, CampaignSummaryResponse>,
    'campaign.summary/unlinked': campaignSummaryUnlinked satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'workitem.list/by-campaign-planned': workitemListByCampaignPlanned satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/by-campaign-running': workitemListByCampaignRunning satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
} as const;

export type FixtureName = keyof typeof fixtures;
