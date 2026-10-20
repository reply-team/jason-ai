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
} as const;

export type FixtureName = keyof typeof fixtures;
