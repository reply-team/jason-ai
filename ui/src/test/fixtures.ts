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
import uiTokensNone from '../../../docs/api/fixtures/ui.tokens/none.json';
import uiTokensTwo from '../../../docs/api/fixtures/ui.tokens/two.json';

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
    'ui.tokens/none': uiTokensNone satisfies Exchange<object, UiTokens>,
    'ui.tokens/two': uiTokensTwo satisfies Exchange<object, UiTokens>,
} as const;

export type FixtureName = keyof typeof fixtures;
