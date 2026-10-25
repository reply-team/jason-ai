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
    CampaignActivityByPeriodRequest,
    CampaignActivityByPeriodResponse,
} from '../api/types';

import approvalApproveAlreadyDecided from '../../../docs/api/fixtures/approval.approve/already-decided.json';
import approvalApproveApproved from '../../../docs/api/fixtures/approval.approve/approved.json';
import approvalApproveFromCampaignRow from '../../../docs/api/fixtures/approval.approve/from-campaign-row.json';
import approvalApproveFromCampaignRowAgain from '../../../docs/api/fixtures/approval.approve/from-campaign-row-again.json';
import approvalGetForCampaignRow from '../../../docs/api/fixtures/approval.get/for-campaign-row.json';
import approvalGetOtherPending from '../../../docs/api/fixtures/approval.get/other-pending.json';
import approvalGetPendingWithPreview from '../../../docs/api/fixtures/approval.get/pending-with-preview.json';
import approvalListByCampaignPending from '../../../docs/api/fixtures/approval.list/by-campaign-pending.json';
import approvalListByWorkItem from '../../../docs/api/fixtures/approval.list/by-work-item.json';
import approvalListDecided from '../../../docs/api/fixtures/approval.list/decided.json';
import approvalListEmpty from '../../../docs/api/fixtures/approval.list/empty.json';
import approvalListPending from '../../../docs/api/fixtures/approval.list/pending.json';
import approvalListSummerPending from '../../../docs/api/fixtures/approval.list/summer-pending.json';
import approvalListUnauthorized from '../../../docs/api/fixtures/approval.list/unauthorized.json';
import approvalRejectFromWorkItem from '../../../docs/api/fixtures/approval.reject/from-work-item.json';
import approvalRejectRejected from '../../../docs/api/fixtures/approval.reject/rejected.json';
import campaignActivityAll7Days from '../../../docs/api/fixtures/campaign.activity/all-7-days.json';
import campaignActivityAllToday from '../../../docs/api/fixtures/campaign.activity/all-today.json';
import campaignActivityLast7Days from '../../../docs/api/fixtures/campaign.activity/last-7-days.json';
import campaignActivityMany from '../../../docs/api/fixtures/campaign.activity/many.json';
import campaignActivityManyOne from '../../../docs/api/fixtures/campaign.activity/many-one.json';
import campaignActivityOne from '../../../docs/api/fixtures/campaign.activity/one.json';
import campaignActivityToday from '../../../docs/api/fixtures/campaign.activity/today.json';
import campaignActivityTwo from '../../../docs/api/fixtures/campaign.activity/two.json';
import campaignActivityByPeriodContacted7Days from '../../../docs/api/fixtures/campaign.activity_by_period/contacted-7-days.json';
import campaignActivityByPeriodContactedOne from '../../../docs/api/fixtures/campaign.activity_by_period/contacted-one.json';
import campaignActivityByPeriodContactedToday from '../../../docs/api/fixtures/campaign.activity_by_period/contacted-today.json';
import campaignActivityByPeriodContactedTwo from '../../../docs/api/fixtures/campaign.activity_by_period/contacted-two.json';
import campaignActivityByPeriodReplied7Days from '../../../docs/api/fixtures/campaign.activity_by_period/replied-7-days.json';
import campaignActivityByPeriodRepliedOne from '../../../docs/api/fixtures/campaign.activity_by_period/replied-one.json';
import campaignActivityByPeriodRepliedToday from '../../../docs/api/fixtures/campaign.activity_by_period/replied-today.json';
import campaignActivityByPeriodRepliedTwo from '../../../docs/api/fixtures/campaign.activity_by_period/replied-two.json';
import campaignAddContactsAlreadyMember from '../../../docs/api/fixtures/campaign.add_contacts/already-member.json';
import campaignAddContactsArchivedCampaign from '../../../docs/api/fixtures/campaign.add_contacts/archived-campaign.json';
import campaignAddContactsOnePerson from '../../../docs/api/fixtures/campaign.add_contacts/one-person.json';
import campaignAddContactsReEnrolExcluded from '../../../docs/api/fixtures/campaign.add_contacts/re-enrol-excluded.json';
import campaignAddContactsSeveral from '../../../docs/api/fixtures/campaign.add_contacts/several.json';
import campaignAddContactsToArchived from '../../../docs/api/fixtures/campaign.add_contacts/to-archived.json';
import campaignGetArchived from '../../../docs/api/fixtures/campaign.get/archived.json';
import campaignGetDetails from '../../../docs/api/fixtures/campaign.get/details.json';
import campaignGetGone from '../../../docs/api/fixtures/campaign.get/gone.json';
import campaignGetGoneHere from '../../../docs/api/fixtures/campaign.get/gone-here.json';
import campaignGetMany from '../../../docs/api/fixtures/campaign.get/many.json';
import campaignGetOne from '../../../docs/api/fixtures/campaign.get/one.json';
import campaignGetSummer from '../../../docs/api/fixtures/campaign.get/summer.json';
import campaignGetUnknown from '../../../docs/api/fixtures/campaign.get/unknown.json';
import campaignGetUnlinked from '../../../docs/api/fixtures/campaign.get/unlinked.json';
import campaignListCampaignsForPicker from '../../../docs/api/fixtures/campaign.list/campaigns-for-picker.json';
import campaignListFirstPage from '../../../docs/api/fixtures/campaign.list/first-page.json';
import campaignListForPicker from '../../../docs/api/fixtures/campaign.list/for-picker.json';
import campaignListIncludeArchived from '../../../docs/api/fixtures/campaign.list/include-archived.json';
import campaignListQuery from '../../../docs/api/fixtures/campaign.list/query.json';
import campaignListScreen from '../../../docs/api/fixtures/campaign.list/screen.json';
import campaignListScreenMany from '../../../docs/api/fixtures/campaign.list/screen-many.json';
import campaignListScreenManyNext from '../../../docs/api/fixtures/campaign.list/screen-many-next.json';
import campaignListScreenQuery from '../../../docs/api/fixtures/campaign.list/screen-query.json';
import campaignListContactsActivityMany from '../../../docs/api/fixtures/campaign.list_contacts/activity-many.json';
import campaignListContactsActivityManyNext from '../../../docs/api/fixtures/campaign.list_contacts/activity-many-next.json';
import campaignListContactsActivityPeople from '../../../docs/api/fixtures/campaign.list_contacts/activity-people.json';
import campaignListContactsActivityPeopleAll from '../../../docs/api/fixtures/campaign.list_contacts/activity-people-all.json';
import campaignListContactsActivityRemoved from '../../../docs/api/fixtures/campaign.list_contacts/activity-removed.json';
import campaignListContactsActivityReplied from '../../../docs/api/fixtures/campaign.list_contacts/activity-replied.json';
import campaignListContactsActivityRepliedAll from '../../../docs/api/fixtures/campaign.list_contacts/activity-replied-all.json';
import campaignListContactsActivityRepliedDay from '../../../docs/api/fixtures/campaign.list_contacts/activity-replied-day.json';
import campaignListContactsActivityRepliedToday from '../../../docs/api/fixtures/campaign.list_contacts/activity-replied-today.json';
import campaignListContactsEveryone from '../../../docs/api/fixtures/campaign.list_contacts/everyone.json';
import campaignListContactsQuery from '../../../docs/api/fixtures/campaign.list_contacts/query.json';
import campaignListContactsQueryTooLong from '../../../docs/api/fixtures/campaign.list_contacts/query-too-long.json';
import campaignListContactsSummaryFailed from '../../../docs/api/fixtures/campaign.list_contacts/summary-failed.json';
import campaignListContactsSummaryRemoved from '../../../docs/api/fixtures/campaign.list_contacts/summary-removed.json';
import campaignListContactsSummaryWaiting from '../../../docs/api/fixtures/campaign.list_contacts/summary-waiting.json';
import campaignListContactsSummaryWaitingAfter from '../../../docs/api/fixtures/campaign.list_contacts/summary-waiting-after.json';
import campaignListContactsUnlinked from '../../../docs/api/fixtures/campaign.list_contacts/unlinked.json';
import campaignPauseWithReason from '../../../docs/api/fixtures/campaign.pause/with-reason.json';
import campaignStartAfterPause from '../../../docs/api/fixtures/campaign.start/after-pause.json';
import campaignSummaryAll from '../../../docs/api/fixtures/campaign.summary/all.json';
import campaignSummaryAllAfter from '../../../docs/api/fixtures/campaign.summary/all-after.json';
import campaignSummaryArchived from '../../../docs/api/fixtures/campaign.summary/archived.json';
import campaignSummaryMany from '../../../docs/api/fixtures/campaign.summary/many.json';
import campaignSummaryManyNext from '../../../docs/api/fixtures/campaign.summary/many-next.json';
import campaignSummarySome from '../../../docs/api/fixtures/campaign.summary/some.json';
import campaignSummarySomeAfter from '../../../docs/api/fixtures/campaign.summary/some-after.json';
import campaignSummarySummer from '../../../docs/api/fixtures/campaign.summary/summer.json';
import campaignSummaryUnlinked from '../../../docs/api/fixtures/campaign.summary/unlinked.json';
import campaignSummaryWithArchived from '../../../docs/api/fixtures/campaign.summary/with-archived.json';
import contactGetArchived from '../../../docs/api/fixtures/contact.get/archived.json';
import contactGetImported from '../../../docs/api/fixtures/contact.get/imported.json';
import contactGetNoFirstName from '../../../docs/api/fixtures/contact.get/no-first-name.json';
import contactGetOli from '../../../docs/api/fixtures/contact.get/oli.json';
import contactGetOne from '../../../docs/api/fixtures/contact.get/one.json';
import contactGetUnrouted from '../../../docs/api/fixtures/contact.get/unrouted.json';
import contactGetWithCampaigns from '../../../docs/api/fixtures/contact.get/with-campaigns.json';
import contactListArchived from '../../../docs/api/fixtures/contact.list/archived.json';
import contactListByActivity from '../../../docs/api/fixtures/contact.list/by-activity.json';
import contactListByActivityQuery from '../../../docs/api/fixtures/contact.list/by-activity-query.json';
import contactListByActivityQueryAguilar from '../../../docs/api/fixtures/contact.list/by-activity-query-aguilar.json';
import contactListByCampaign from '../../../docs/api/fixtures/contact.list/by-campaign.json';
import contactListByCampaignQuery from '../../../docs/api/fixtures/contact.list/by-campaign-query.json';
import contactListByCampaignQueryArchived from '../../../docs/api/fixtures/contact.list/by-campaign-query-archived.json';
import contactListByCampaignQueryNone from '../../../docs/api/fixtures/contact.list/by-campaign-query-none.json';
import contactListCampaignPicker from '../../../docs/api/fixtures/contact.list/campaign-picker.json';
import contactListCampaignPickerZoe from '../../../docs/api/fixtures/contact.list/campaign-picker-zoe.json';
import contactListFirstPage from '../../../docs/api/fixtures/contact.list/first-page.json';
import contactListGoneCampaign from '../../../docs/api/fixtures/contact.list/gone-campaign.json';
import contactListMany from '../../../docs/api/fixtures/contact.list/many.json';
import contactListManyNext from '../../../docs/api/fixtures/contact.list/many-next.json';
import contactListQueryEmail from '../../../docs/api/fixtures/contact.list/query-email.json';
import contactListQueryPhone from '../../../docs/api/fixtures/contact.list/query-phone.json';
import contactListQueryTooLong from '../../../docs/api/fixtures/contact.list/query-too-long.json';
import contactThreadHasMore from '../../../docs/api/fixtures/contact.thread/has-more.json';
import contactThreadMessages from '../../../docs/api/fixtures/contact.thread/messages.json';
import contactThreadNext from '../../../docs/api/fixtures/contact.thread/next.json';
import contactThreadNotThisContact from '../../../docs/api/fixtures/contact.thread/not-this-contact.json';
import contactThreadsHasMore from '../../../docs/api/fixtures/contact.threads/has-more.json';
import contactThreadsNext from '../../../docs/api/fixtures/contact.threads/next.json';
import contactThreadsNoRoute from '../../../docs/api/fixtures/contact.threads/no-route.json';
import contactThreadsNotLinked from '../../../docs/api/fixtures/contact.threads/not-linked.json';
import contactThreadsProviderFailed from '../../../docs/api/fixtures/contact.threads/provider-failed.json';
import contactThreadsTwoThreads from '../../../docs/api/fixtures/contact.threads/two-threads.json';
import decisionAnswerAnswered from '../../../docs/api/fixtures/decision.answer/answered.json';
import decisionGetForCampaignRow from '../../../docs/api/fixtures/decision.get/for-campaign-row.json';
import decisionGetPendingWithOptions from '../../../docs/api/fixtures/decision.get/pending-with-options.json';
import decisionListByCampaignPending from '../../../docs/api/fixtures/decision.list/by-campaign-pending.json';
import decisionListDecided from '../../../docs/api/fixtures/decision.list/decided.json';
import decisionListEmpty from '../../../docs/api/fixtures/decision.list/empty.json';
import decisionListPending from '../../../docs/api/fixtures/decision.list/pending.json';
import decisionListSummerPending from '../../../docs/api/fixtures/decision.list/summer-pending.json';
import journalListByAwaitingItem from '../../../docs/api/fixtures/journal.list/by-awaiting-item.json';
import journalListByCampaign from '../../../docs/api/fixtures/journal.list/by-campaign.json';
import journalListByCampaignChanges from '../../../docs/api/fixtures/journal.list/by-campaign-changes.json';
import journalListByContact from '../../../docs/api/fixtures/journal.list/by-contact.json';
import journalListByContactChanges from '../../../docs/api/fixtures/journal.list/by-contact-changes.json';
import journalListByContactDecisions from '../../../docs/api/fixtures/journal.list/by-contact-decisions.json';
import journalListByContactEmpty from '../../../docs/api/fixtures/journal.list/by-contact-empty.json';
import journalListByContactFailed from '../../../docs/api/fixtures/journal.list/by-contact-failed.json';
import journalListByContactRepliedTwice from '../../../docs/api/fixtures/journal.list/by-contact-replied-twice.json';
import journalListByContactReplies from '../../../docs/api/fixtures/journal.list/by-contact-replies.json';
import journalListByContactWork from '../../../docs/api/fixtures/journal.list/by-contact-work.json';
import journalListByExpiredItem from '../../../docs/api/fixtures/journal.list/by-expired-item.json';
import journalListByFailedItem from '../../../docs/api/fixtures/journal.list/by-failed-item.json';
import journalListBySucceededItem from '../../../docs/api/fixtures/journal.list/by-succeeded-item.json';
import journalListByWorkItem from '../../../docs/api/fixtures/journal.list/by-work-item.json';
import journalListOli from '../../../docs/api/fixtures/journal.list/oli.json';
import journalListSummer from '../../../docs/api/fixtures/journal.list/summer.json';
import pluginListNone from '../../../docs/api/fixtures/plugin.list/none.json';
import roleListAll from '../../../docs/api/fixtures/role.list/all.json';
import routeListEmpty from '../../../docs/api/fixtures/route.list/empty.json';
import systemInfoWithUi from '../../../docs/api/fixtures/system.info/with-ui.json';
import systemInfoWithoutUi from '../../../docs/api/fixtures/system.info/without-ui.json';
import uiRevokeNeverUsed from '../../../docs/api/fixtures/ui.revoke/never-used.json';
import uiRevokeOne from '../../../docs/api/fixtures/ui.revoke/one.json';
import uiRevokeStale from '../../../docs/api/fixtures/ui.revoke/stale.json';
import uiTokensAged from '../../../docs/api/fixtures/ui.tokens/aged.json';
import uiTokensNone from '../../../docs/api/fixtures/ui.tokens/none.json';
import uiTokensTwo from '../../../docs/api/fixtures/ui.tokens/two.json';
import workitemCancelReason from '../../../docs/api/fixtures/workitem.cancel/reason.json';
import workitemCountAdaDone from '../../../docs/api/fixtures/workitem.count/ada-done.json';
import workitemCountAdaNow from '../../../docs/api/fixtures/workitem.count/ada-now.json';
import workitemCountAdaPlanned from '../../../docs/api/fixtures/workitem.count/ada-planned.json';
import workitemCountDone from '../../../docs/api/fixtures/workitem.count/done.json';
import workitemCountFailed from '../../../docs/api/fixtures/workitem.count/failed.json';
import workitemCountFailedOn16 from '../../../docs/api/fixtures/workitem.count/failed-on-16.json';
import workitemCountFailedOn17 from '../../../docs/api/fixtures/workitem.count/failed-on-17.json';
import workitemCountFiltered from '../../../docs/api/fixtures/workitem.count/filtered.json';
import workitemCountManyDone from '../../../docs/api/fixtures/workitem.count/many-done.json';
import workitemCountManyNow from '../../../docs/api/fixtures/workitem.count/many-now.json';
import workitemCountManyPlanned from '../../../docs/api/fixtures/workitem.count/many-planned.json';
import workitemCountNow from '../../../docs/api/fixtures/workitem.count/now.json';
import workitemCountPlanned from '../../../docs/api/fixtures/workitem.count/planned.json';
import workitemCountPlannedOn16 from '../../../docs/api/fixtures/workitem.count/planned-on-16.json';
import workitemCountPlannedOn17 from '../../../docs/api/fixtures/workitem.count/planned-on-17.json';
import workitemCountSmbDone from '../../../docs/api/fixtures/workitem.count/smb-done.json';
import workitemCountSmbManagerDone from '../../../docs/api/fixtures/workitem.count/smb-manager-done.json';
import workitemCountSmbManagerNow from '../../../docs/api/fixtures/workitem.count/smb-manager-now.json';
import workitemCountSmbNow from '../../../docs/api/fixtures/workitem.count/smb-now.json';
import workitemCountSmbOperationsDone from '../../../docs/api/fixtures/workitem.count/smb-operations-done.json';
import workitemCountSmbOperationsNow from '../../../docs/api/fixtures/workitem.count/smb-operations-now.json';
import workitemCountSmbOperationsPlanned from '../../../docs/api/fixtures/workitem.count/smb-operations-planned.json';
import workitemCountSmbPlanned from '../../../docs/api/fixtures/workitem.count/smb-planned.json';
import workitemCountSmbRolesDone from '../../../docs/api/fixtures/workitem.count/smb-roles-done.json';
import workitemCountSmbRolesNow from '../../../docs/api/fixtures/workitem.count/smb-roles-now.json';
import workitemCountSmbRolesPlanned from '../../../docs/api/fixtures/workitem.count/smb-roles-planned.json';
import workitemCountTomasDone from '../../../docs/api/fixtures/workitem.count/tomas-done.json';
import workitemCountTomasNow from '../../../docs/api/fixtures/workitem.count/tomas-now.json';
import workitemCountTomasPlanned from '../../../docs/api/fixtures/workitem.count/tomas-planned.json';
import workitemCountUnknownCampaignDone from '../../../docs/api/fixtures/workitem.count/unknown-campaign-done.json';
import workitemCountUnknownCampaignNow from '../../../docs/api/fixtures/workitem.count/unknown-campaign-now.json';
import workitemCountUnknownCampaignPlanned from '../../../docs/api/fixtures/workitem.count/unknown-campaign-planned.json';
import workitemCountZero from '../../../docs/api/fixtures/workitem.count/zero.json';
import workitemGetAwaiting from '../../../docs/api/fixtures/workitem.get/awaiting.json';
import workitemGetExpired from '../../../docs/api/fixtures/workitem.get/expired.json';
import workitemGetFailed from '../../../docs/api/fixtures/workitem.get/failed.json';
import workitemGetOpen from '../../../docs/api/fixtures/workitem.get/open.json';
import workitemGetSucceeded from '../../../docs/api/fixtures/workitem.get/succeeded.json';
import workitemListAdaNow from '../../../docs/api/fixtures/workitem.list/ada-now.json';
import workitemListByCampaignPlanned from '../../../docs/api/fixtures/workitem.list/by-campaign-planned.json';
import workitemListByCampaignRunning from '../../../docs/api/fixtures/workitem.list/by-campaign-running.json';
import workitemListByContact from '../../../docs/api/fixtures/workitem.list/by-contact.json';
import workitemListByContactOpen from '../../../docs/api/fixtures/workitem.list/by-contact-open.json';
import workitemListByContactOpenImported from '../../../docs/api/fixtures/workitem.list/by-contact-open-imported.json';
import workitemListByContactOpenNone from '../../../docs/api/fixtures/workitem.list/by-contact-open-none.json';
import workitemListDone from '../../../docs/api/fixtures/workitem.list/done.json';
import workitemListEmpty from '../../../docs/api/fixtures/workitem.list/empty.json';
import workitemListFailed from '../../../docs/api/fixtures/workitem.list/failed.json';
import workitemListFailedOn16 from '../../../docs/api/fixtures/workitem.list/failed-on-16.json';
import workitemListFailedOn17 from '../../../docs/api/fixtures/workitem.list/failed-on-17.json';
import workitemListNow from '../../../docs/api/fixtures/workitem.list/now.json';
import workitemListOliOpen from '../../../docs/api/fixtures/workitem.list/oli-open.json';
import workitemListPlanned from '../../../docs/api/fixtures/workitem.list/planned.json';
import workitemListPlannedMany from '../../../docs/api/fixtures/workitem.list/planned-many.json';
import workitemListPlannedManyNext from '../../../docs/api/fixtures/workitem.list/planned-many-next.json';
import workitemListSmbNow from '../../../docs/api/fixtures/workitem.list/smb-now.json';
import workitemListSmbOperationsPlanned from '../../../docs/api/fixtures/workitem.list/smb-operations-planned.json';
import workitemListSmbPlanned from '../../../docs/api/fixtures/workitem.list/smb-planned.json';
import workitemListSmbRolesPlanned from '../../../docs/api/fixtures/workitem.list/smb-roles-planned.json';
import workitemListSummerPlanned from '../../../docs/api/fixtures/workitem.list/summer-planned.json';
import workitemListSummerRunning from '../../../docs/api/fixtures/workitem.list/summer-running.json';
import workitemListTomasNow from '../../../docs/api/fixtures/workitem.list/tomas-now.json';
import workitemListUnknownCampaign from '../../../docs/api/fixtures/workitem.list/unknown-campaign.json';
import workitemUpdateDates from '../../../docs/api/fixtures/workitem.update/dates.json';
import workitemUpdateFinishedMeanwhile from '../../../docs/api/fixtures/workitem.update/finished-meanwhile.json';

export type Exchange<Req, Res> = { operation: string; case: string; request: Req; status: number; response: Res };

export const fixtures = {
    'approval.approve/already-decided': approvalApproveAlreadyDecided satisfies Exchange<
        ApprovalDecisionRequest,
        ErrorEnvelope
    >,
    'approval.approve/approved': approvalApproveApproved satisfies Exchange<ApprovalDecisionRequest, Approval>,
    'approval.approve/from-campaign-row': approvalApproveFromCampaignRow satisfies Exchange<
        ApprovalDecisionRequest,
        Approval
    >,
    'approval.approve/from-campaign-row-again': approvalApproveFromCampaignRowAgain satisfies Exchange<
        ApprovalDecisionRequest,
        ErrorEnvelope
    >,
    'approval.get/for-campaign-row': approvalGetForCampaignRow satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/other-pending': approvalGetOtherPending satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/pending-with-preview': approvalGetPendingWithPreview satisfies Exchange<
        { approval_id: string },
        Approval
    >,
    'approval.list/by-campaign-pending': approvalListByCampaignPending satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/by-work-item': approvalListByWorkItem satisfies Exchange<ApprovalListRequest, Page<ApprovalSummary>>,
    'approval.list/decided': approvalListDecided satisfies Exchange<ApprovalListRequest, Page<ApprovalSummary>>,
    'approval.list/empty': approvalListEmpty satisfies Exchange<ApprovalListRequest, Page<ApprovalSummary>>,
    'approval.list/pending': approvalListPending satisfies Exchange<ApprovalListRequest, Page<ApprovalSummary>>,
    'approval.list/summer-pending': approvalListSummerPending satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/unauthorized': approvalListUnauthorized satisfies Exchange<ApprovalListRequest, ErrorEnvelope>,
    'approval.reject/from-work-item': approvalRejectFromWorkItem satisfies Exchange<ApprovalDecisionRequest, Approval>,
    'approval.reject/rejected': approvalRejectRejected satisfies Exchange<ApprovalDecisionRequest, Approval>,
    'campaign.activity/all-7-days': campaignActivityAll7Days satisfies Exchange<
        CampaignActivityRequest,
        CampaignActivityResponse
    >,
    'campaign.activity/all-today': campaignActivityAllToday satisfies Exchange<
        CampaignActivityRequest,
        CampaignActivityResponse
    >,
    'campaign.activity/last-7-days': campaignActivityLast7Days satisfies Exchange<
        CampaignActivityRequest,
        CampaignActivityResponse
    >,
    'campaign.activity/many': campaignActivityMany satisfies Exchange<
        CampaignActivityRequest,
        CampaignActivityResponse
    >,
    'campaign.activity/many-one': campaignActivityManyOne satisfies Exchange<
        CampaignActivityRequest,
        CampaignActivityResponse
    >,
    'campaign.activity/one': campaignActivityOne satisfies Exchange<CampaignActivityRequest, CampaignActivityResponse>,
    'campaign.activity/today': campaignActivityToday satisfies Exchange<
        CampaignActivityRequest,
        CampaignActivityResponse
    >,
    'campaign.activity/two': campaignActivityTwo satisfies Exchange<CampaignActivityRequest, CampaignActivityResponse>,
    'campaign.activity_by_period/contacted-7-days': campaignActivityByPeriodContacted7Days satisfies Exchange<
        CampaignActivityByPeriodRequest,
        CampaignActivityByPeriodResponse
    >,
    'campaign.activity_by_period/contacted-one': campaignActivityByPeriodContactedOne satisfies Exchange<
        CampaignActivityByPeriodRequest,
        CampaignActivityByPeriodResponse
    >,
    'campaign.activity_by_period/contacted-today': campaignActivityByPeriodContactedToday satisfies Exchange<
        CampaignActivityByPeriodRequest,
        CampaignActivityByPeriodResponse
    >,
    'campaign.activity_by_period/contacted-two': campaignActivityByPeriodContactedTwo satisfies Exchange<
        CampaignActivityByPeriodRequest,
        CampaignActivityByPeriodResponse
    >,
    'campaign.activity_by_period/replied-7-days': campaignActivityByPeriodReplied7Days satisfies Exchange<
        CampaignActivityByPeriodRequest,
        CampaignActivityByPeriodResponse
    >,
    'campaign.activity_by_period/replied-one': campaignActivityByPeriodRepliedOne satisfies Exchange<
        CampaignActivityByPeriodRequest,
        CampaignActivityByPeriodResponse
    >,
    'campaign.activity_by_period/replied-today': campaignActivityByPeriodRepliedToday satisfies Exchange<
        CampaignActivityByPeriodRequest,
        CampaignActivityByPeriodResponse
    >,
    'campaign.activity_by_period/replied-two': campaignActivityByPeriodRepliedTwo satisfies Exchange<
        CampaignActivityByPeriodRequest,
        CampaignActivityByPeriodResponse
    >,
    'campaign.add_contacts/already-member': campaignAddContactsAlreadyMember satisfies Exchange<
        AddContactsRequest,
        AddContactsResult
    >,
    'campaign.add_contacts/archived-campaign': campaignAddContactsArchivedCampaign satisfies Exchange<
        AddContactsRequest,
        ErrorEnvelope
    >,
    'campaign.add_contacts/one-person': campaignAddContactsOnePerson satisfies Exchange<
        AddContactsRequest,
        AddContactsResult
    >,
    'campaign.add_contacts/re-enrol-excluded': campaignAddContactsReEnrolExcluded satisfies Exchange<
        AddContactsRequest,
        AddContactsResult
    >,
    'campaign.add_contacts/several': campaignAddContactsSeveral satisfies Exchange<
        AddContactsRequest,
        AddContactsResult
    >,
    'campaign.add_contacts/to-archived': campaignAddContactsToArchived satisfies Exchange<
        AddContactsRequest,
        ErrorEnvelope
    >,
    'campaign.get/archived': campaignGetArchived satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/details': campaignGetDetails satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/gone': campaignGetGone satisfies Exchange<{ campaign_id: string }, ErrorEnvelope>,
    'campaign.get/gone-here': campaignGetGoneHere satisfies Exchange<{ campaign_id: string }, ErrorEnvelope>,
    'campaign.get/many': campaignGetMany satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/one': campaignGetOne satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/summer': campaignGetSummer satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/unknown': campaignGetUnknown satisfies Exchange<{ campaign_id: string }, ErrorEnvelope>,
    'campaign.get/unlinked': campaignGetUnlinked satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.list/campaigns-for-picker': campaignListCampaignsForPicker satisfies Exchange<
        CampaignListRequest,
        Page<CampaignSummary>
    >,
    'campaign.list/first-page': campaignListFirstPage satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'campaign.list/for-picker': campaignListForPicker satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'campaign.list/include-archived': campaignListIncludeArchived satisfies Exchange<
        CampaignListRequest,
        Page<CampaignSummary>
    >,
    'campaign.list/query': campaignListQuery satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'campaign.list/screen': campaignListScreen satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'campaign.list/screen-many': campaignListScreenMany satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'campaign.list/screen-many-next': campaignListScreenManyNext satisfies Exchange<
        CampaignListRequest,
        Page<CampaignSummary>
    >,
    'campaign.list/screen-query': campaignListScreenQuery satisfies Exchange<
        CampaignListRequest,
        Page<CampaignSummary>
    >,
    'campaign.list_contacts/activity-many': campaignListContactsActivityMany satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/activity-many-next': campaignListContactsActivityManyNext satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/activity-people': campaignListContactsActivityPeople satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/activity-people-all': campaignListContactsActivityPeopleAll satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/activity-removed': campaignListContactsActivityRemoved satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/activity-replied': campaignListContactsActivityReplied satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/activity-replied-all': campaignListContactsActivityRepliedAll satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/activity-replied-day': campaignListContactsActivityRepliedDay satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/activity-replied-today': campaignListContactsActivityRepliedToday satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/everyone': campaignListContactsEveryone satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/query': campaignListContactsQuery satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/query-too-long': campaignListContactsQueryTooLong satisfies Exchange<
        ListContactsRequest,
        ErrorEnvelope
    >,
    'campaign.list_contacts/summary-failed': campaignListContactsSummaryFailed satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/summary-removed': campaignListContactsSummaryRemoved satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/summary-waiting': campaignListContactsSummaryWaiting satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/summary-waiting-after': campaignListContactsSummaryWaitingAfter satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/unlinked': campaignListContactsUnlinked satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.pause/with-reason': campaignPauseWithReason satisfies Exchange<CampaignTransitionRequest, Campaign>,
    'campaign.start/after-pause': campaignStartAfterPause satisfies Exchange<CampaignTransitionRequest, Campaign>,
    'campaign.summary/all': campaignSummaryAll satisfies Exchange<CampaignSummaryRequest, CampaignSummaryResponse>,
    'campaign.summary/all-after': campaignSummaryAllAfter satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/archived': campaignSummaryArchived satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/many': campaignSummaryMany satisfies Exchange<CampaignSummaryRequest, CampaignSummaryResponse>,
    'campaign.summary/many-next': campaignSummaryManyNext satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/some': campaignSummarySome satisfies Exchange<CampaignSummaryRequest, CampaignSummaryResponse>,
    'campaign.summary/some-after': campaignSummarySomeAfter satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/summer': campaignSummarySummer satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/unlinked': campaignSummaryUnlinked satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/with-archived': campaignSummaryWithArchived satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'contact.get/archived': contactGetArchived satisfies Exchange<{ contact_id: string }, Contact>,
    'contact.get/imported': contactGetImported satisfies Exchange<{ contact_id: string }, Contact>,
    'contact.get/no-first-name': contactGetNoFirstName satisfies Exchange<{ contact_id: string }, Contact>,
    'contact.get/oli': contactGetOli satisfies Exchange<{ contact_id: string }, Contact>,
    'contact.get/one': contactGetOne satisfies Exchange<{ contact_id: string }, Contact>,
    'contact.get/unrouted': contactGetUnrouted satisfies Exchange<{ contact_id: string }, Contact>,
    'contact.get/with-campaigns': contactGetWithCampaigns satisfies Exchange<{ contact_id: string }, Contact>,
    'contact.list/archived': contactListArchived satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/by-activity': contactListByActivity satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/by-activity-query': contactListByActivityQuery satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/by-activity-query-aguilar': contactListByActivityQueryAguilar satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'contact.list/by-campaign': contactListByCampaign satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/by-campaign-query': contactListByCampaignQuery satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/by-campaign-query-archived': contactListByCampaignQueryArchived satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'contact.list/by-campaign-query-none': contactListByCampaignQueryNone satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'contact.list/campaign-picker': contactListCampaignPicker satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/campaign-picker-zoe': contactListCampaignPickerZoe satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'contact.list/first-page': contactListFirstPage satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/gone-campaign': contactListGoneCampaign satisfies Exchange<ContactListRequest, ErrorEnvelope>,
    'contact.list/many': contactListMany satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/many-next': contactListManyNext satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/query-email': contactListQueryEmail satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/query-phone': contactListQueryPhone satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/query-too-long': contactListQueryTooLong satisfies Exchange<ContactListRequest, ErrorEnvelope>,
    'contact.thread/has-more': contactThreadHasMore satisfies Exchange<ContactThreadRequest, ContactThread>,
    'contact.thread/messages': contactThreadMessages satisfies Exchange<ContactThreadRequest, ContactThread>,
    'contact.thread/next': contactThreadNext satisfies Exchange<ContactThreadRequest, ContactThread>,
    'contact.thread/not-this-contact': contactThreadNotThisContact satisfies Exchange<
        ContactThreadRequest,
        ErrorEnvelope
    >,
    'contact.threads/has-more': contactThreadsHasMore satisfies Exchange<ContactThreadsRequest, ContactThreads>,
    'contact.threads/next': contactThreadsNext satisfies Exchange<ContactThreadsRequest, ContactThreads>,
    'contact.threads/no-route': contactThreadsNoRoute satisfies Exchange<ContactThreadsRequest, ErrorEnvelope>,
    'contact.threads/not-linked': contactThreadsNotLinked satisfies Exchange<ContactThreadsRequest, ErrorEnvelope>,
    'contact.threads/provider-failed': contactThreadsProviderFailed satisfies Exchange<
        ContactThreadsRequest,
        ErrorEnvelope
    >,
    'contact.threads/two-threads': contactThreadsTwoThreads satisfies Exchange<ContactThreadsRequest, ContactThreads>,
    'decision.answer/answered': decisionAnswerAnswered satisfies Exchange<DecisionAnswerRequest, Decision>,
    'decision.get/for-campaign-row': decisionGetForCampaignRow satisfies Exchange<{ decision_id: string }, Decision>,
    'decision.get/pending-with-options': decisionGetPendingWithOptions satisfies Exchange<
        { decision_id: string },
        Decision
    >,
    'decision.list/by-campaign-pending': decisionListByCampaignPending satisfies Exchange<
        DecisionListRequest,
        Page<DecisionSummary>
    >,
    'decision.list/decided': decisionListDecided satisfies Exchange<DecisionListRequest, Page<DecisionSummary>>,
    'decision.list/empty': decisionListEmpty satisfies Exchange<DecisionListRequest, Page<DecisionSummary>>,
    'decision.list/pending': decisionListPending satisfies Exchange<DecisionListRequest, Page<DecisionSummary>>,
    'decision.list/summer-pending': decisionListSummerPending satisfies Exchange<
        DecisionListRequest,
        Page<DecisionSummary>
    >,
    'journal.list/by-awaiting-item': journalListByAwaitingItem satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/by-campaign': journalListByCampaign satisfies Exchange<JournalListRequest, Page<JournalEntry>>,
    'journal.list/by-campaign-changes': journalListByCampaignChanges satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/by-contact': journalListByContact satisfies Exchange<JournalListRequest, Page<JournalEntry>>,
    'journal.list/by-contact-changes': journalListByContactChanges satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/by-contact-decisions': journalListByContactDecisions satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/by-contact-empty': journalListByContactEmpty satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/by-contact-failed': journalListByContactFailed satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/by-contact-replied-twice': journalListByContactRepliedTwice satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/by-contact-replies': journalListByContactReplies satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/by-contact-work': journalListByContactWork satisfies Exchange<JournalListRequest, Page<JournalEntry>>,
    'journal.list/by-expired-item': journalListByExpiredItem satisfies Exchange<JournalListRequest, Page<JournalEntry>>,
    'journal.list/by-failed-item': journalListByFailedItem satisfies Exchange<JournalListRequest, Page<JournalEntry>>,
    'journal.list/by-succeeded-item': journalListBySucceededItem satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/by-work-item': journalListByWorkItem satisfies Exchange<JournalListRequest, Page<JournalEntry>>,
    'journal.list/oli': journalListOli satisfies Exchange<JournalListRequest, Page<JournalEntry>>,
    'journal.list/summer': journalListSummer satisfies Exchange<JournalListRequest, Page<JournalEntry>>,
    'plugin.list/none': pluginListNone satisfies Exchange<object, object>,
    'role.list/all': roleListAll satisfies Exchange<object, Page<Role>>,
    'route.list/empty': routeListEmpty satisfies Exchange<object, Routes>,
    'system.info/with-ui': systemInfoWithUi satisfies Exchange<object, SystemInfo>,
    'system.info/without-ui': systemInfoWithoutUi satisfies Exchange<object, SystemInfo>,
    'ui.revoke/never-used': uiRevokeNeverUsed satisfies Exchange<UiRevokeRequest, UiRevoke>,
    'ui.revoke/one': uiRevokeOne satisfies Exchange<UiRevokeRequest, UiRevoke>,
    'ui.revoke/stale': uiRevokeStale satisfies Exchange<UiRevokeRequest, UiRevoke>,
    'ui.tokens/aged': uiTokensAged satisfies Exchange<object, UiTokens>,
    'ui.tokens/none': uiTokensNone satisfies Exchange<object, UiTokens>,
    'ui.tokens/two': uiTokensTwo satisfies Exchange<object, UiTokens>,
    'workitem.cancel/reason': workitemCancelReason satisfies Exchange<WorkItemCancelRequest, WorkItem>,
    'workitem.count/ada-done': workitemCountAdaDone satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/ada-now': workitemCountAdaNow satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/ada-planned': workitemCountAdaPlanned satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/done': workitemCountDone satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/failed': workitemCountFailed satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/failed-on-16': workitemCountFailedOn16 satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/failed-on-17': workitemCountFailedOn17 satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/filtered': workitemCountFiltered satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/many-done': workitemCountManyDone satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/many-now': workitemCountManyNow satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/many-planned': workitemCountManyPlanned satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/now': workitemCountNow satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/planned': workitemCountPlanned satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/planned-on-16': workitemCountPlannedOn16 satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/planned-on-17': workitemCountPlannedOn17 satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/smb-done': workitemCountSmbDone satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/smb-manager-done': workitemCountSmbManagerDone satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/smb-manager-now': workitemCountSmbManagerNow satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/smb-now': workitemCountSmbNow satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/smb-operations-done': workitemCountSmbOperationsDone satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/smb-operations-now': workitemCountSmbOperationsNow satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/smb-operations-planned': workitemCountSmbOperationsPlanned satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/smb-planned': workitemCountSmbPlanned satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/smb-roles-done': workitemCountSmbRolesDone satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/smb-roles-now': workitemCountSmbRolesNow satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/smb-roles-planned': workitemCountSmbRolesPlanned satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/tomas-done': workitemCountTomasDone satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/tomas-now': workitemCountTomasNow satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/tomas-planned': workitemCountTomasPlanned satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.count/unknown-campaign-done': workitemCountUnknownCampaignDone satisfies Exchange<
        WorkItemFilterRequest,
        ErrorEnvelope
    >,
    'workitem.count/unknown-campaign-now': workitemCountUnknownCampaignNow satisfies Exchange<
        WorkItemFilterRequest,
        ErrorEnvelope
    >,
    'workitem.count/unknown-campaign-planned': workitemCountUnknownCampaignPlanned satisfies Exchange<
        WorkItemFilterRequest,
        ErrorEnvelope
    >,
    'workitem.count/zero': workitemCountZero satisfies Exchange<WorkItemFilterRequest, WorkItemCount>,
    'workitem.get/awaiting': workitemGetAwaiting satisfies Exchange<{ work_item_id: string }, WorkItem>,
    'workitem.get/expired': workitemGetExpired satisfies Exchange<{ work_item_id: string }, WorkItem>,
    'workitem.get/failed': workitemGetFailed satisfies Exchange<{ work_item_id: string }, WorkItem>,
    'workitem.get/open': workitemGetOpen satisfies Exchange<{ work_item_id: string }, WorkItem>,
    'workitem.get/succeeded': workitemGetSucceeded satisfies Exchange<{ work_item_id: string }, WorkItem>,
    'workitem.list/ada-now': workitemListAdaNow satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/by-campaign-planned': workitemListByCampaignPlanned satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/by-campaign-running': workitemListByCampaignRunning satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/by-contact': workitemListByContact satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/by-contact-open': workitemListByContactOpen satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/by-contact-open-imported': workitemListByContactOpenImported satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/by-contact-open-none': workitemListByContactOpenNone satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/done': workitemListDone satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/empty': workitemListEmpty satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/failed': workitemListFailed satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/failed-on-16': workitemListFailedOn16 satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/failed-on-17': workitemListFailedOn17 satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/now': workitemListNow satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/oli-open': workitemListOliOpen satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/planned': workitemListPlanned satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/planned-many': workitemListPlannedMany satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/planned-many-next': workitemListPlannedManyNext satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/smb-now': workitemListSmbNow satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/smb-operations-planned': workitemListSmbOperationsPlanned satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/smb-planned': workitemListSmbPlanned satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/smb-roles-planned': workitemListSmbRolesPlanned satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/summer-planned': workitemListSummerPlanned satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/summer-running': workitemListSummerRunning satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/tomas-now': workitemListTomasNow satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/unknown-campaign': workitemListUnknownCampaign satisfies Exchange<
        WorkItemListRequest,
        ErrorEnvelope
    >,
    'workitem.update/dates': workitemUpdateDates satisfies Exchange<WorkItemUpdateRequest, WorkItem>,
    'workitem.update/finished-meanwhile': workitemUpdateFinishedMeanwhile satisfies Exchange<
        WorkItemUpdateRequest,
        ErrorEnvelope
    >,
} as const;

export type FixtureName = keyof typeof fixtures;
