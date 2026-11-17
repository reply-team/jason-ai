// Guard 2, the UI's half: every recorded exchange imported here is held to the UI's own types by `satisfies`.
// A field renamed in the runtime fails `dotnet test` first (the recording), then the typecheck here.
import type {
    ListAddRequest,
    Approval,
    ApprovalDecisionRequest,
    ApprovalListRequest,
    ApprovalSummary,
    Decision,
    DecisionAnswerRequest,
    DecisionListRequest,
    DecisionSummary,
    ErrorEnvelope,
    NotificationSwitchRequest,
    NotificationsInfo,
    Page,
    Project,
    RouteResolution,
    RouteResolveRequest,
    Routes,
    SenderListRequest,
    SenderListResponse,
    SystemInfo,
    UiRevoke,
    UiRevokeRequest,
    UiTokens,
    CampaignListRequest,
    CampaignSummary,
    Campaign,
    CampaignTransitionRequest,
    CampaignSetSendModeRequest,
    CampaignSummaryRequest,
    CampaignSummaryResponse,
    CampaignActivityRequest,
    CampaignActivityResponse,
    ReplyCountRequest,
    ReplyCountResponse,
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
    ContactUpdateRequest,
    Suppression,
    SuppressionListRequest,
    SuppressionRequest,
    SuppressionRemoved,
    RemoveContactsRequest,
    RemoveContactsResult,
    WorkItemCreateRequest,
    WorkItemGetRequest,
    WorkItemWithResult,
} from '../api/types';

import approvalApproveAlreadyDecided from '../../../docs/api/fixtures/approval.approve/already-decided.json';
import approvalApproveApproved from '../../../docs/api/fixtures/approval.approve/approved.json';
import approvalApproveFromCampaignRow from '../../../docs/api/fixtures/approval.approve/from-campaign-row.json';
import approvalApproveFromCampaignRowAgain from '../../../docs/api/fixtures/approval.approve/from-campaign-row-again.json';
import approvalApproveNeedsPageTwoApproved from '../../../docs/api/fixtures/approval.approve/needs-page-two-approved.json';
import approvalApproveNeedsTwoFirstApproved from '../../../docs/api/fixtures/approval.approve/needs-two-first-approved.json';
import approvalGetCardInvitation from '../../../docs/api/fixtures/approval.get/card-invitation.json';
import approvalGetCardMessage from '../../../docs/api/fixtures/approval.get/card-message.json';
import approvalGetCardMessageReasked from '../../../docs/api/fixtures/approval.get/card-message-reasked.json';
import approvalGetEmailMessage from '../../../docs/api/fixtures/approval.get/email-message.json';
import approvalGetForCampaignRow from '../../../docs/api/fixtures/approval.get/for-campaign-row.json';
import approvalGetLinkedinMessage from '../../../docs/api/fixtures/approval.get/linkedin-message.json';
import approvalGetNeedsMessage from '../../../docs/api/fixtures/approval.get/needs-message.json';
import approvalGetNeedsPageTwo from '../../../docs/api/fixtures/approval.get/needs-page-two.json';
import approvalGetNeedsReply from '../../../docs/api/fixtures/approval.get/needs-reply.json';
import approvalGetNeedsTwoFirst from '../../../docs/api/fixtures/approval.get/needs-two-first.json';
import approvalGetNeedsTwoSecond from '../../../docs/api/fixtures/approval.get/needs-two-second.json';
import approvalGetModesChosen from '../../../docs/api/fixtures/approval.get/modes-chosen.json';
import approvalGetParkAnswersNoVerdict from '../../../docs/api/fixtures/approval.get/park-answers-no-verdict.json';
import approvalGetParkAwaitingJudgement from '../../../docs/api/fixtures/approval.get/park-awaiting-judgement.json';
import approvalGetParkAwaitingNature from '../../../docs/api/fixtures/approval.get/park-awaiting-nature.json';
import approvalGetParkDailyCeiling from '../../../docs/api/fixtures/approval.get/park-daily-ceiling.json';
import approvalGetParkDoubted from '../../../docs/api/fixtures/approval.get/park-doubted.json';
import approvalGetParkEditedAfterReview from '../../../docs/api/fixtures/approval.get/park-edited-after-review.json';
import approvalGetParkEscalated from '../../../docs/api/fixtures/approval.get/park-escalated.json';
import approvalGetParkNotReviewed from '../../../docs/api/fixtures/approval.get/park-not-reviewed.json';
import approvalGetParkReplyNotFromAPerson from '../../../docs/api/fixtures/approval.get/park-reply-not-from-a-person.json';
import approvalGetParkSentFromChat from '../../../docs/api/fixtures/approval.get/park-sent-from-chat.json';
import approvalGetReleasedAutomatically from '../../../docs/api/fixtures/approval.get/released-automatically.json';
import approvalGetWithDoubts from '../../../docs/api/fixtures/approval.get/with-doubts.json';
import approvalGetOtherPending from '../../../docs/api/fixtures/approval.get/other-pending.json';
import approvalGetPendingWithPreview from '../../../docs/api/fixtures/approval.get/pending-with-preview.json';
import approvalListCardInvitationPending from '../../../docs/api/fixtures/approval.list/card-invitation-pending.json';
import approvalListCardMessageReasked from '../../../docs/api/fixtures/approval.list/card-message-reasked.json';
import approvalListCardMessageAfterEdit from '../../../docs/api/fixtures/approval.list/card-message-after-edit.json';
import approvalApproveCardMessageReasked from '../../../docs/api/fixtures/approval.approve/card-message-reasked.json';
import approvalListCardMessageAllPending from '../../../docs/api/fixtures/approval.list/card-message-all-pending.json';
import approvalListCardMessageAllAfterEdit from '../../../docs/api/fixtures/approval.list/card-message-all-after-edit.json';
import approvalListCardMessageAllReasked from '../../../docs/api/fixtures/approval.list/card-message-all-reasked.json';
import approvalListCardMessageItemAfterEdit from '../../../docs/api/fixtures/approval.list/card-message-item-after-edit.json';
import approvalListCardMessageItemReasked from '../../../docs/api/fixtures/approval.list/card-message-item-reasked.json';
import approvalListCardMessageItemAllAfterEdit from '../../../docs/api/fixtures/approval.list/card-message-item-all-after-edit.json';
import approvalListCardMessageItemAllReasked from '../../../docs/api/fixtures/approval.list/card-message-item-all-reasked.json';
import approvalListCardMessageItemAllApproved from '../../../docs/api/fixtures/approval.list/card-message-item-all-approved.json';
import workitemGetCardMessageApproved from '../../../docs/api/fixtures/workitem.get/card-message-approved.json';
import approvalListNeedsMessageAllAfterEdit from '../../../docs/api/fixtures/approval.list/needs-message-all-after-edit.json';
import approvalListNeedsMessageAllCancelled from '../../../docs/api/fixtures/approval.list/needs-message-all-cancelled.json';
import workitemGetNeedsMessageCancelled from '../../../docs/api/fixtures/workitem.get/needs-message-cancelled.json';
import approvalListCardMessageAllApproved from '../../../docs/api/fixtures/approval.list/card-message-all-approved.json';
import approvalListCardMessageItemApproved from '../../../docs/api/fixtures/approval.list/card-message-item-approved.json';
import workitemGetCardMessageRequeued from '../../../docs/api/fixtures/workitem.get/card-message-requeued.json';
import workitemGetNeedsMessageRequeued from '../../../docs/api/fixtures/workitem.get/needs-message-requeued.json';
import workitemGetCardMessageReparked from '../../../docs/api/fixtures/workitem.get/card-message-reparked.json';
import approvalListCardMessagePending from '../../../docs/api/fixtures/approval.list/card-message-pending.json';
import approvalListByCampaignPending from '../../../docs/api/fixtures/approval.list/by-campaign-pending.json';
import approvalListByWorkItem from '../../../docs/api/fixtures/approval.list/by-work-item.json';
import approvalListDecided from '../../../docs/api/fixtures/approval.list/decided.json';
import approvalListEmpty from '../../../docs/api/fixtures/approval.list/empty.json';
import approvalListMessagesPending from '../../../docs/api/fixtures/approval.list/messages-pending.json';
import approvalListNeedsAfterEdit from '../../../docs/api/fixtures/approval.list/needs-after-edit.json';
import approvalListNeedsFirstPage from '../../../docs/api/fixtures/approval.list/needs-first-page.json';
import approvalListNeedsInbox from '../../../docs/api/fixtures/approval.list/needs-inbox.json';
import approvalListNeedsNextPage from '../../../docs/api/fixtures/approval.list/needs-next-page.json';
import approvalListNeedsNextPageAfter from '../../../docs/api/fixtures/approval.list/needs-next-page-after.json';
import approvalListNeedsTwo from '../../../docs/api/fixtures/approval.list/needs-two.json';
import approvalListNeedsTwoAfter from '../../../docs/api/fixtures/approval.list/needs-two-after.json';
import approvalListOneModeUnset from '../../../docs/api/fixtures/approval.list/one-mode-unset.json';
import approvalListPending from '../../../docs/api/fixtures/approval.list/pending.json';
import approvalListSummerPending from '../../../docs/api/fixtures/approval.list/summer-pending.json';
import approvalListUnauthorized from '../../../docs/api/fixtures/approval.list/unauthorized.json';
import approvalRejectFromWorkItem from '../../../docs/api/fixtures/approval.reject/from-work-item.json';
import approvalRejectRejected from '../../../docs/api/fixtures/approval.reject/rejected.json';
import campaignActivityAll30Days from '../../../docs/api/fixtures/campaign.activity/all-30-days.json';
import campaignActivityAll7Days from '../../../docs/api/fixtures/campaign.activity/all-7-days.json';
import campaignActivityAllToday from '../../../docs/api/fixtures/campaign.activity/all-today.json';
import campaignActivityLast7Days from '../../../docs/api/fixtures/campaign.activity/last-7-days.json';
import campaignActivityMany from '../../../docs/api/fixtures/campaign.activity/many.json';
import campaignActivityManyOne from '../../../docs/api/fixtures/campaign.activity/many-one.json';
import campaignActivityOne from '../../../docs/api/fixtures/campaign.activity/one.json';
import campaignActivitySummer from '../../../docs/api/fixtures/campaign.activity/summer.json';
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
import campaignAddContactsOwnList from '../../../docs/api/fixtures/campaign.add_contacts/own-list.json';
import campaignAddContactsOwnListAgain from '../../../docs/api/fixtures/campaign.add_contacts/own-list-again.json';
import campaignAddContactsOwnListArchived from '../../../docs/api/fixtures/campaign.add_contacts/own-list-archived.json';
import campaignGetOwnList from '../../../docs/api/fixtures/campaign.get/own-list.json';
import campaignGetOwnListRunning from '../../../docs/api/fixtures/campaign.get/own-list-running.json';
import campaignGetOwnListAutomatic from '../../../docs/api/fixtures/campaign.get/own-list-automatic.json';
import campaignGetOwnListArchived from '../../../docs/api/fixtures/campaign.get/own-list-archived.json';
import campaignGetOwnListPaused from '../../../docs/api/fixtures/campaign.get/own-list-paused.json';
import campaignGetArchived from '../../../docs/api/fixtures/campaign.get/archived.json';
import campaignGetDetails from '../../../docs/api/fixtures/campaign.get/details.json';
import campaignGetGone from '../../../docs/api/fixtures/campaign.get/gone.json';
import campaignGetGoneHere from '../../../docs/api/fixtures/campaign.get/gone-here.json';
import campaignGetInProjectWithFolder from '../../../docs/api/fixtures/campaign.get/in-project-with-folder.json';
import campaignGetInbox from '../../../docs/api/fixtures/campaign.get/inbox.json';
import campaignGetInboxQuiet from '../../../docs/api/fixtures/campaign.get/inbox-quiet.json';
import campaignGetMaking from '../../../docs/api/fixtures/campaign.get/making.json';
import campaignGetMany from '../../../docs/api/fixtures/campaign.get/many.json';
import campaignGetOne from '../../../docs/api/fixtures/campaign.get/one.json';
import campaignGetSendModeOverride from '../../../docs/api/fixtures/campaign.get/send-mode-override.json';
import campaignGetStarted from '../../../docs/api/fixtures/campaign.get/started.json';
import campaignSetSendModeCleared from '../../../docs/api/fixtures/campaign.set_send_mode/cleared.json';
import campaignSetSendModeNotHuman from '../../../docs/api/fixtures/campaign.set_send_mode/not-human.json';
import campaignSetSendModeSet from '../../../docs/api/fixtures/campaign.set_send_mode/set.json';
import campaignSetSendModeWarmAutomatic from '../../../docs/api/fixtures/campaign.set_send_mode/warm-automatic.json';
import campaignGetSummer from '../../../docs/api/fixtures/campaign.get/summer.json';
import campaignGetUnknown from '../../../docs/api/fixtures/campaign.get/unknown.json';
import campaignGetUnlinked from '../../../docs/api/fixtures/campaign.get/unlinked.json';
import campaignListCardChips from '../../../docs/api/fixtures/campaign.list/card-chips.json';
import campaignListCampaignsForPicker from '../../../docs/api/fixtures/campaign.list/campaigns-for-picker.json';
import campaignListFirstPage from '../../../docs/api/fixtures/campaign.list/first-page.json';
import campaignListForPicker from '../../../docs/api/fixtures/campaign.list/for-picker.json';
import campaignListInbox from '../../../docs/api/fixtures/campaign.list/inbox.json';
import campaignListIncludeArchived from '../../../docs/api/fixtures/campaign.list/include-archived.json';
import campaignListQuery from '../../../docs/api/fixtures/campaign.list/query.json';
import campaignListScreen from '../../../docs/api/fixtures/campaign.list/screen.json';
import campaignListScreenEmpty from '../../../docs/api/fixtures/campaign.list/screen-empty.json';
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
import campaignSummaryCardChips from '../../../docs/api/fixtures/campaign.summary/card-chips.json';
import campaignSummaryAll from '../../../docs/api/fixtures/campaign.summary/all.json';
import campaignSummaryEveryone from '../../../docs/api/fixtures/campaign.summary/everyone.json';
import campaignSummaryEveryoneWithoutArchived from '../../../docs/api/fixtures/campaign.summary/everyone-without-archived.json';
import campaignSummaryEveryoneWithoutArchivedEmpty from '../../../docs/api/fixtures/campaign.summary/everyone-without-archived-empty.json';
import campaignSummaryAllAfter from '../../../docs/api/fixtures/campaign.summary/all-after.json';
import campaignSummaryArchived from '../../../docs/api/fixtures/campaign.summary/archived.json';
import campaignSummaryInboxAll from '../../../docs/api/fixtures/campaign.summary/inbox-all.json';
import campaignSummaryInboxOne from '../../../docs/api/fixtures/campaign.summary/inbox-one.json';
import campaignSummaryInboxQuiet from '../../../docs/api/fixtures/campaign.summary/inbox-quiet.json';
import campaignSummaryMany from '../../../docs/api/fixtures/campaign.summary/many.json';
import campaignSummaryManyNext from '../../../docs/api/fixtures/campaign.summary/many-next.json';
import campaignSummarySome from '../../../docs/api/fixtures/campaign.summary/some.json';
import campaignSummarySomeAfter from '../../../docs/api/fixtures/campaign.summary/some-after.json';
import campaignSummarySomeWithoutArchived from '../../../docs/api/fixtures/campaign.summary/some-without-archived.json';
import campaignSummarySummer from '../../../docs/api/fixtures/campaign.summary/summer.json';
import campaignSummaryUnlinked from '../../../docs/api/fixtures/campaign.summary/unlinked.json';
import campaignSummaryUnlinkedArchivedNotLeftOut from '../../../docs/api/fixtures/campaign.summary/unlinked-archived-not-left-out.json';
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
import contactListByActivityEmpty from '../../../docs/api/fixtures/contact.list/by-activity-empty.json';
import contactListByActivityQuery from '../../../docs/api/fixtures/contact.list/by-activity-query.json';
import contactListByActivityQueryAguilar from '../../../docs/api/fixtures/contact.list/by-activity-query-aguilar.json';
import contactListByActivitySentFromTheAccount from '../../../docs/api/fixtures/contact.list/by-activity-sent-from-the-account.json';
import contactListByActivitySeveral from '../../../docs/api/fixtures/contact.list/by-activity-several.json';
import contactListByActivitySummaryNewWithoutArchived from '../../../docs/api/fixtures/contact.list/by-activity-summary-new-without-archived.json';
import contactListByActivitySummaryQuery from '../../../docs/api/fixtures/contact.list/by-activity-summary-query.json';
import contactListByActivitySummaryWaiting from '../../../docs/api/fixtures/contact.list/by-activity-summary-waiting.json';
import contactListByCampaign from '../../../docs/api/fixtures/contact.list/by-campaign.json';
import contactListByCampaignArchived from '../../../docs/api/fixtures/contact.list/by-campaign-archived.json';
import contactListByCampaignQuery from '../../../docs/api/fixtures/contact.list/by-campaign-query.json';
import contactListByCampaignQueryArchived from '../../../docs/api/fixtures/contact.list/by-campaign-query-archived.json';
import contactListByCampaignQueryNone from '../../../docs/api/fixtures/contact.list/by-campaign-query-none.json';
import contactListByCampaignSummary from '../../../docs/api/fixtures/contact.list/by-campaign-summary.json';
import contactListByCampaignWithoutArchived from '../../../docs/api/fixtures/contact.list/by-campaign-without-archived.json';
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
import contactThreadsInboxMateo from '../../../docs/api/fixtures/contact.threads/inbox-mateo.json';
import contactThreadsInboxPaula from '../../../docs/api/fixtures/contact.threads/inbox-paula.json';
import contactThreadsNext from '../../../docs/api/fixtures/contact.threads/next.json';
import contactThreadsNoRoute from '../../../docs/api/fixtures/contact.threads/no-route.json';
import contactThreadsNotLinked from '../../../docs/api/fixtures/contact.threads/not-linked.json';
import contactThreadsProviderFailed from '../../../docs/api/fixtures/contact.threads/provider-failed.json';
import contactThreadsTwoThreads from '../../../docs/api/fixtures/contact.threads/two-threads.json';
import decisionAnswerAnswered from '../../../docs/api/fixtures/decision.answer/answered.json';
import decisionGetForCampaignRow from '../../../docs/api/fixtures/decision.get/for-campaign-row.json';
import decisionGetNeedsInboxQuestion from '../../../docs/api/fixtures/decision.get/needs-inbox-question.json';
import decisionGetNeedsQuestion from '../../../docs/api/fixtures/decision.get/needs-question.json';
import decisionGetPendingWithOptions from '../../../docs/api/fixtures/decision.get/pending-with-options.json';
import decisionListByCampaignPending from '../../../docs/api/fixtures/decision.list/by-campaign-pending.json';
import decisionListDecided from '../../../docs/api/fixtures/decision.list/decided.json';
import decisionListEmpty from '../../../docs/api/fixtures/decision.list/empty.json';
import decisionListNeedsInboxQuestions from '../../../docs/api/fixtures/decision.list/needs-inbox-questions.json';
import decisionListNeedsQuestions from '../../../docs/api/fixtures/decision.list/needs-questions.json';
import decisionListPending from '../../../docs/api/fixtures/decision.list/pending.json';
import decisionListSummerPending from '../../../docs/api/fixtures/decision.list/summer-pending.json';
import journalListByAwaitingItem from '../../../docs/api/fixtures/journal.list/by-awaiting-item.json';
import journalListByCampaign from '../../../docs/api/fixtures/journal.list/by-campaign.json';
import journalListByCampaignChanges from '../../../docs/api/fixtures/journal.list/by-campaign-changes.json';
import journalListByCampaignReplies from '../../../docs/api/fixtures/journal.list/by-campaign-replies.json';
import journalListByCampaignRepliesNone from '../../../docs/api/fixtures/journal.list/by-campaign-replies-none.json';
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
// A person's acts on campaign A since the moment a choice was asked in its chat: a pause, and none.
import journalListCardChoiceActs from '../../../docs/api/fixtures/journal.list/card-choice-acts.json';
import journalListCardChoiceActsNone from '../../../docs/api/fixtures/journal.list/card-choice-acts-none.json';
// What a plan card reads under "On record now", each campaign's files from one story: a draft agent, its people and the
// plugin a send takes on each channel; an agent no route sends for; an agent whose LinkedIn messages go to another
// plugin by a channel route of its own; and for a chat about no agent yet the installation's send modes and routes, with
// and without a global channel route.
import campaignGetCardPlanDraft from '../../../docs/api/fixtures/campaign.get/card-plan-draft.json';
import campaignSummaryCardPlanDraft from '../../../docs/api/fixtures/campaign.summary/card-plan-draft.json';
import routeResolveCardPlanEmail from '../../../docs/api/fixtures/route.resolve/card-plan-email.json';
import routeResolveCardPlanLinkedin from '../../../docs/api/fixtures/route.resolve/card-plan-linkedin.json';
import campaignGetCardPlanUnrouted from '../../../docs/api/fixtures/campaign.get/card-plan-unrouted.json';
import campaignSummaryCardPlanUnrouted from '../../../docs/api/fixtures/campaign.summary/card-plan-unrouted.json';
import routeResolveCardPlanNoRouteEmail from '../../../docs/api/fixtures/route.resolve/card-plan-no-route-email.json';
import campaignGetCardPlanChannels from '../../../docs/api/fixtures/campaign.get/card-plan-channels.json';
import campaignSummaryCardPlanChannels from '../../../docs/api/fixtures/campaign.summary/card-plan-channels.json';
import routeResolveOnAChannel from '../../../docs/api/fixtures/route.resolve/on-a-channel.json';
import routeListCardAccounts from '../../../docs/api/fixtures/route.list/card-accounts.json';
import routeListCardPlanGlobalChannels from '../../../docs/api/fixtures/route.list/card-plan-global-channels.json';
import systemInfoSendModesSet from '../../../docs/api/fixtures/system.info/send-modes-set.json';
// What the chat's data cards read: an agent's status, its people and what waits, in the cards' story.
import campaignGetCardStatus from '../../../docs/api/fixtures/campaign.get/card-status.json';
import campaignGetCardStatusDraft from '../../../docs/api/fixtures/campaign.get/card-status-draft.json';
import campaignGetCardArchived from '../../../docs/api/fixtures/campaign.get/card-archived.json';
import campaignGetCardUnknownCampaign from '../../../docs/api/fixtures/campaign.get/card-unknown-campaign.json';
import campaignSummaryCardStatus from '../../../docs/api/fixtures/campaign.summary/card-status.json';
import campaignSummaryCardStatusLater from '../../../docs/api/fixtures/campaign.summary/card-status-later.json';
import campaignSummaryCardStatusDraft from '../../../docs/api/fixtures/campaign.summary/card-status-draft.json';
import campaignSummaryCardArchived from '../../../docs/api/fixtures/campaign.summary/card-archived.json';
import campaignActivityCardStatus30Days from '../../../docs/api/fixtures/campaign.activity/card-status-30-days.json';
import campaignActivityCardArchived30Days from '../../../docs/api/fixtures/campaign.activity/card-archived-30-days.json';
import campaignListContactsCardPeopleFirst from '../../../docs/api/fixtures/campaign.list_contacts/card-people-first.json';
import campaignListContactsCardPeopleQuery from '../../../docs/api/fixtures/campaign.list_contacts/card-people-query.json';
import campaignListContactsCardPeopleWaiting from '../../../docs/api/fixtures/campaign.list_contacts/card-people-waiting.json';
import campaignListContactsCardPeopleEmpty from '../../../docs/api/fixtures/campaign.list_contacts/card-people-empty.json';
import campaignListContactsCardArchived from '../../../docs/api/fixtures/campaign.list_contacts/card-archived.json';
import contactListCardPeopleEveryone from '../../../docs/api/fixtures/contact.list/card-people-everyone.json';
import contactListCardPeopleQuery from '../../../docs/api/fixtures/contact.list/card-people-query.json';
import contactListCardPeopleNobody from '../../../docs/api/fixtures/contact.list/card-people-nobody.json';
import approvalListCardStatusWaiting from '../../../docs/api/fixtures/approval.list/card-status-waiting.json';
import approvalListCardNeedsAll from '../../../docs/api/fixtures/approval.list/card-needs-all.json';
import approvalListCardArchived from '../../../docs/api/fixtures/approval.list/card-archived.json';
import decisionListCardStatusWaiting from '../../../docs/api/fixtures/decision.list/card-status-waiting.json';
import decisionListCardNeedsAll from '../../../docs/api/fixtures/decision.list/card-needs-all.json';
import decisionListCardArchived from '../../../docs/api/fixtures/decision.list/card-archived.json';
import approvalGetCardNeedsPending from '../../../docs/api/fixtures/approval.get/card-needs-pending.json';
import approvalGetCardNeedsDecided from '../../../docs/api/fixtures/approval.get/card-needs-decided.json';
import decisionGetCardNeedsQuestion from '../../../docs/api/fixtures/decision.get/card-needs-question.json';
// What the accounts card reads of the runtime: the official Reply package as it lists it, and the global route to it.
import pluginListCardAccountsReply from '../../../docs/api/fixtures/plugin.list/card-accounts-reply.json';
import pluginListNone from '../../../docs/api/fixtures/plugin.list/none.json';
import replyCountArchivedNamed from '../../../docs/api/fixtures/reply.count/archived-named.json';
import replyCountEveryCampaign from '../../../docs/api/fixtures/reply.count/every-campaign.json';
import replyCountPeriodNone from '../../../docs/api/fixtures/reply.count/period-none.json';
import replyCountPeriodSpring from '../../../docs/api/fixtures/reply.count/period-spring.json';
import replyCountUntilBeforeSince from '../../../docs/api/fixtures/reply.count/until-before-since.json';
import roleListAll from '../../../docs/api/fixtures/role.list/all.json';
import routeListChannelRoutes from '../../../docs/api/fixtures/route.list/channel-routes.json';
import routeListCardAccountsReply from '../../../docs/api/fixtures/route.list/card-accounts-reply.json';
import routeListEmpty from '../../../docs/api/fixtures/route.list/empty.json';
import routeResolveElsewhere from '../../../docs/api/fixtures/route.resolve/elsewhere.json';
import routeResolveElsewhereFirst from '../../../docs/api/fixtures/route.resolve/elsewhere-first.json';
import routeResolveGlobalDefault from '../../../docs/api/fixtures/route.resolve/global-default.json';
import routeResolveNoRoute from '../../../docs/api/fixtures/route.resolve/no-route.json';
import routeResolveOwnRoute from '../../../docs/api/fixtures/route.resolve/own-route.json';
import routeResolveUnroutedFirst from '../../../docs/api/fixtures/route.resolve/unrouted-first.json';
import preferenceListModeAutomaticVoiceUnset from '../../../docs/api/fixtures/preference.list/mode-automatic-voice-unset.json';
import preferenceListModesEveryMessage from '../../../docs/api/fixtures/preference.list/modes-every-message.json';
import preferenceListNone from '../../../docs/api/fixtures/preference.list/none.json';
import preferenceListSet from '../../../docs/api/fixtures/preference.list/set.json';
import preferenceSetSendModeCold from '../../../docs/api/fixtures/preference.set/send-mode-cold.json';
import preferenceSetSendModeColdEveryMessage from '../../../docs/api/fixtures/preference.set/send-mode-cold-every-message.json';
import preferenceSetSendModeWarm from '../../../docs/api/fixtures/preference.set/send-mode-warm.json';
import preferenceSetSender from '../../../docs/api/fixtures/preference.set/sender.json';
import preferenceSetTone from '../../../docs/api/fixtures/preference.set/tone.json';
import notificationDisablePlugin from '../../../docs/api/fixtures/notification.disable/plugin.json';
import notificationDisableToast from '../../../docs/api/fixtures/notification.disable/toast.json';
import notificationEnableNotConfigured from '../../../docs/api/fixtures/notification.enable/not-configured.json';
import notificationEnableNotNamed from '../../../docs/api/fixtures/notification.enable/not-named.json';
import notificationEnableNotificationsOff from '../../../docs/api/fixtures/notification.enable/notifications-off.json';
import notificationEnableToast from '../../../docs/api/fixtures/notification.enable/toast.json';
import notificationEnableUnknownChannel from '../../../docs/api/fixtures/notification.enable/unknown-channel.json';
import preferenceUnsetSendModeCold from '../../../docs/api/fixtures/preference.unset/send-mode-cold.json';
import projectGetById from '../../../docs/api/fixtures/project.get/by-id.json';
import projectGetBySlug from '../../../docs/api/fixtures/project.get/by-slug.json';
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
import workitemGetNeedsMessageParked from '../../../docs/api/fixtures/workitem.get/needs-message-parked.json';
import workitemGetNeedsPageTwoParked from '../../../docs/api/fixtures/workitem.get/needs-page-two-parked.json';
import workitemGetNeedsTwoFirstParked from '../../../docs/api/fixtures/workitem.get/needs-two-first-parked.json';
import workitemGetParkedEditedSend from '../../../docs/api/fixtures/workitem.get/parked-edited-send.json';
import senderListCardMessageSender from '../../../docs/api/fixtures/sender.list/card-message-sender.json';
import senderListCardSenderOther from '../../../docs/api/fixtures/sender.list/card-sender-other.json';
import senderListCardSenderUnnamed from '../../../docs/api/fixtures/sender.list/card-sender-unnamed.json';
import senderListCardSenderUnread from '../../../docs/api/fixtures/sender.list/card-sender-unread.json';
import workitemGetCardMessageParked from '../../../docs/api/fixtures/workitem.get/card-message-parked.json';
import workitemGetParkedSend from '../../../docs/api/fixtures/workitem.get/parked-send.json';
import workitemGetOpen from '../../../docs/api/fixtures/workitem.get/open.json';
import workitemGetSucceeded from '../../../docs/api/fixtures/workitem.get/succeeded.json';
import workitemListAdaNow from '../../../docs/api/fixtures/workitem.list/ada-now.json';
import workitemListByCampaignPlanned from '../../../docs/api/fixtures/workitem.list/by-campaign-planned.json';
import workitemListByCampaignRunning from '../../../docs/api/fixtures/workitem.list/by-campaign-running.json';
import workitemListByContact from '../../../docs/api/fixtures/workitem.list/by-contact.json';
import workitemListByContactOpen from '../../../docs/api/fixtures/workitem.list/by-contact-open.json';
import workitemListByContactOpenImported from '../../../docs/api/fixtures/workitem.list/by-contact-open-imported.json';
import workitemListByContactOpenNone from '../../../docs/api/fixtures/workitem.list/by-contact-open-none.json';
import workitemListCreatingNone from '../../../docs/api/fixtures/workitem.list/creating-none.json';
import workitemListCreatingOpen from '../../../docs/api/fixtures/workitem.list/creating-open.json';
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
import workitemListReviewLatest from '../../../docs/api/fixtures/workitem.list/review-latest.json';
import workitemListReviewNone from '../../../docs/api/fixtures/workitem.list/review-none.json';
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
import workitemUpdateCardMessageEdited from '../../../docs/api/fixtures/workitem.update/card-message-edited.json';
import workitemUpdateNeedsMessageEdited from '../../../docs/api/fixtures/workitem.update/needs-message-edited.json';
// The person panel's recordings.
import personCampaignAddContactsMoveIn from '../../../docs/api/fixtures/campaign.add_contacts/move-in.json';
import personCampaignAddContactsMoveInRejected from '../../../docs/api/fixtures/campaign.add_contacts/move-in-rejected.json';
import personCampaignListMoveTargets from '../../../docs/api/fixtures/campaign.list/move-targets.json';
import personCampaignRemoveContactsExcludeArchived from '../../../docs/api/fixtures/campaign.remove_contacts/exclude-archived.json';
import personCampaignRemoveContactsExcludeOne from '../../../docs/api/fixtures/campaign.remove_contacts/exclude-one.json';
import personCampaignRemoveContactsMoveOut from '../../../docs/api/fixtures/campaign.remove_contacts/move-out.json';
import personCampaignRemoveContactsMoveOutArchived from '../../../docs/api/fixtures/campaign.remove_contacts/move-out-archived.json';
import personContactGetPersonAfterExclude from '../../../docs/api/fixtures/contact.get/person-after-exclude.json';
import personContactGetPersonAfterHalfMove from '../../../docs/api/fixtures/contact.get/person-after-half-move.json';
import personContactGetPersonAfterMove from '../../../docs/api/fixtures/contact.get/person-after-move.json';
import personContactGetPersonFinished from '../../../docs/api/fixtures/contact.get/person-finished.json';
import personContactGetPersonPanel from '../../../docs/api/fixtures/contact.get/person-panel.json';
import personContactUpdatePersonChannels from '../../../docs/api/fixtures/contact.update/person-channels.json';
import personContactUpdatePersonCleared from '../../../docs/api/fixtures/contact.update/person-cleared.json';
import personContactUpdatePersonPrimaries from '../../../docs/api/fixtures/contact.update/person-primaries.json';
import personContactUpdatePersonRefused from '../../../docs/api/fixtures/contact.update/person-refused.json';
import personContactUpdatePersonSaved from '../../../docs/api/fixtures/contact.update/person-saved.json';
import personSuppressionAddPersonEmail from '../../../docs/api/fixtures/suppression.add/person-email.json';
import personSuppressionAddPersonLinkedin from '../../../docs/api/fixtures/suppression.add/person-linkedin.json';
import personSuppressionListAguilarEmailClear from '../../../docs/api/fixtures/suppression.list/aguilar-email-clear.json';
import personSuppressionListGraceEmailClear from '../../../docs/api/fixtures/suppression.list/grace-email-clear.json';
import personSuppressionListMateoEmailClear from '../../../docs/api/fixtures/suppression.list/mateo-email-clear.json';
import personSuppressionListMateoLinkedinClear from '../../../docs/api/fixtures/suppression.list/mateo-linkedin-clear.json';
import personSuppressionListMateoPhoneClear from '../../../docs/api/fixtures/suppression.list/mateo-phone-clear.json';
import personSuppressionListPersonEmailClear from '../../../docs/api/fixtures/suppression.list/person-email-clear.json';
import personSuppressionListPersonEmailListed from '../../../docs/api/fixtures/suppression.list/person-email-listed.json';
import personSuppressionListPersonLinkedinClear from '../../../docs/api/fixtures/suppression.list/person-linkedin-clear.json';
import personSuppressionListPersonLinkedinListed from '../../../docs/api/fixtures/suppression.list/person-linkedin-listed.json';
import personSuppressionListPersonPhoneClear from '../../../docs/api/fixtures/suppression.list/person-phone-clear.json';
import personSuppressionListPersonSecondEmailClear from '../../../docs/api/fixtures/suppression.list/person-second-email-clear.json';
import personSuppressionRemovePersonEmail from '../../../docs/api/fixtures/suppression.remove/person-email.json';
import personSuppressionRemovePersonLinkedin from '../../../docs/api/fixtures/suppression.remove/person-linkedin.json';
import personWorkitemCreateProviderArchived from '../../../docs/api/fixtures/workitem.create/provider-archived.json';
import personWorkitemCreateProviderContactUpdate from '../../../docs/api/fixtures/workitem.create/provider-contact-update.json';
import personWorkitemCreateResearcherArchived from '../../../docs/api/fixtures/workitem.create/researcher-archived.json';
import personWorkitemCreateResearcherAsked from '../../../docs/api/fixtures/workitem.create/researcher-asked.json';
import personWorkitemCreateResearcherAskedPlain from '../../../docs/api/fixtures/workitem.create/researcher-asked-plain.json';
import personWorkitemGetResearcherFinding from '../../../docs/api/fixtures/workitem.get/researcher-finding.json';
import personWorkitemGetResearcherFindingSecond from '../../../docs/api/fixtures/workitem.get/researcher-finding-second.json';
import personWorkitemListResearcherFindings from '../../../docs/api/fixtures/workitem.list/researcher-findings.json';
import personWorkitemListResearcherFindingsTwo from '../../../docs/api/fixtures/workitem.list/researcher-findings-two.json';
import approvalApprovePanelDoubted from '../../../docs/api/fixtures/approval.approve/panel-doubted.json';
import approvalGetPanelDoubted from '../../../docs/api/fixtures/approval.get/panel-doubted.json';
import approvalGetPanelMoved from '../../../docs/api/fixtures/approval.get/panel-moved.json';
import approvalListPanelDoubted from '../../../docs/api/fixtures/approval.list/panel-doubted.json';
import approvalListPanelMoved from '../../../docs/api/fixtures/approval.list/panel-moved.json';
import journalListPanelDoubted from '../../../docs/api/fixtures/journal.list/panel-doubted.json';
import journalListPanelDoubtedApproved from '../../../docs/api/fixtures/journal.list/panel-doubted-approved.json';
import journalListPanelMoved from '../../../docs/api/fixtures/journal.list/panel-moved.json';
import journalListPanelMovedEdited from '../../../docs/api/fixtures/journal.list/panel-moved-edited.json';
import journalListPersonSentFromTheAccount from '../../../docs/api/fixtures/journal.list/person-sent-from-the-account.json';
import workitemCountPanelDoubtedApprovedDone from '../../../docs/api/fixtures/workitem.count/panel-doubted-approved-done.json';
import workitemCountPanelDoubtedApprovedNow from '../../../docs/api/fixtures/workitem.count/panel-doubted-approved-now.json';
import workitemCountPanelDoubtedApprovedPlanned from '../../../docs/api/fixtures/workitem.count/panel-doubted-approved-planned.json';
import workitemCountPanelDoubtedDone from '../../../docs/api/fixtures/workitem.count/panel-doubted-done.json';
import workitemCountPanelDoubtedNow from '../../../docs/api/fixtures/workitem.count/panel-doubted-now.json';
import workitemCountPanelDoubtedPlanned from '../../../docs/api/fixtures/workitem.count/panel-doubted-planned.json';
import workitemCountPanelMovedDone from '../../../docs/api/fixtures/workitem.count/panel-moved-done.json';
import workitemCountPanelMovedNow from '../../../docs/api/fixtures/workitem.count/panel-moved-now.json';
import workitemCountPanelMovedPlanned from '../../../docs/api/fixtures/workitem.count/panel-moved-planned.json';
import workitemGetPanelDoubted from '../../../docs/api/fixtures/workitem.get/panel-doubted.json';
import workitemGetPanelDoubtedApproved from '../../../docs/api/fixtures/workitem.get/panel-doubted-approved.json';
import workitemGetPanelMoved from '../../../docs/api/fixtures/workitem.get/panel-moved.json';
import workitemGetPanelMovedEdited from '../../../docs/api/fixtures/workitem.get/panel-moved-edited.json';
import workitemListPanelDoubtedApprovedNow from '../../../docs/api/fixtures/workitem.list/panel-doubted-approved-now.json';
import workitemListPanelDoubtedNow from '../../../docs/api/fixtures/workitem.list/panel-doubted-now.json';
import workitemListPanelMovedNow from '../../../docs/api/fixtures/workitem.list/panel-moved-now.json';
import workitemCountPanelMovedEditedDone from '../../../docs/api/fixtures/workitem.count/panel-moved-edited-done.json';
import workitemCountPanelMovedEditedNow from '../../../docs/api/fixtures/workitem.count/panel-moved-edited-now.json';
import workitemCountPanelMovedEditedPlanned from '../../../docs/api/fixtures/workitem.count/panel-moved-edited-planned.json';
import workitemListPanelMovedEditedNow from '../../../docs/api/fixtures/workitem.list/panel-moved-edited-now.json';
import approvalGetByWorkItem from '../../../docs/api/fixtures/approval.get/by-work-item.json';
import approvalListArchiveAskedAndGiven from '../../../docs/api/fixtures/approval.list/archive-asked-and-given.json';
import campaignArchiveArchiveSpring from '../../../docs/api/fixtures/campaign.archive/archive-spring.json';
import campaignArchiveArchiveSpringAgain from '../../../docs/api/fixtures/campaign.archive/archive-spring-again.json';
import campaignGetArchiveSpring from '../../../docs/api/fixtures/campaign.get/archive-spring.json';
import campaignGetArchiveSpringArchived from '../../../docs/api/fixtures/campaign.get/archive-spring-archived.json';
import campaignListArchiveAfter from '../../../docs/api/fixtures/campaign.list/archive-after.json';
import campaignListArchiveBefore from '../../../docs/api/fixtures/campaign.list/archive-before.json';
import campaignSummaryArchiveAfter from '../../../docs/api/fixtures/campaign.summary/archive-after.json';
import campaignSummaryArchiveBefore from '../../../docs/api/fixtures/campaign.summary/archive-before.json';
import campaignSummaryArchiveSpring from '../../../docs/api/fixtures/campaign.summary/archive-spring.json';
import campaignSummaryArchiveSpringArchived from '../../../docs/api/fixtures/campaign.summary/archive-spring-archived.json';
import decisionListArchivePending from '../../../docs/api/fixtures/decision.list/archive-pending.json';
import routeResolveArchivePause from '../../../docs/api/fixtures/route.resolve/archive-pause.json';
import routeResolveArchivePauseUnusable from '../../../docs/api/fixtures/route.resolve/archive-pause-unusable.json';
import workitemListArchiveOpen from '../../../docs/api/fixtures/workitem.list/archive-open.json';

import type { PreferenceItem, PreferenceSetRequest, PreferenceUnsetRequest } from '../screens/settings/voice-model';

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
    'approval.approve/needs-page-two-approved': approvalApproveNeedsPageTwoApproved satisfies Exchange<
        ApprovalDecisionRequest,
        Approval
    >,
    'approval.approve/needs-two-first-approved': approvalApproveNeedsTwoFirstApproved satisfies Exchange<
        ApprovalDecisionRequest,
        Approval
    >,
    'approval.get/card-invitation': approvalGetCardInvitation satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/card-message': approvalGetCardMessage satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/card-message-reasked': approvalGetCardMessageReasked satisfies Exchange<
        { approval_id: string },
        Approval
    >,
    'approval.get/email-message': approvalGetEmailMessage satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/for-campaign-row': approvalGetForCampaignRow satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/linkedin-message': approvalGetLinkedinMessage satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/needs-message': approvalGetNeedsMessage satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/needs-page-two': approvalGetNeedsPageTwo satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/needs-reply': approvalGetNeedsReply satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/needs-two-first': approvalGetNeedsTwoFirst satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/needs-two-second': approvalGetNeedsTwoSecond satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/modes-chosen': approvalGetModesChosen satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/park-answers-no-verdict': approvalGetParkAnswersNoVerdict satisfies Exchange<
        { approval_id: string },
        Approval
    >,
    'approval.get/park-awaiting-judgement': approvalGetParkAwaitingJudgement satisfies Exchange<
        { approval_id: string },
        Approval
    >,
    'approval.get/park-awaiting-nature': approvalGetParkAwaitingNature satisfies Exchange<
        { approval_id: string },
        Approval
    >,
    'approval.get/park-daily-ceiling': approvalGetParkDailyCeiling satisfies Exchange<
        { approval_id: string },
        Approval
    >,
    'approval.get/park-doubted': approvalGetParkDoubted satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/park-edited-after-review': approvalGetParkEditedAfterReview satisfies Exchange<
        { approval_id: string },
        Approval
    >,
    'approval.get/park-escalated': approvalGetParkEscalated satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/park-not-reviewed': approvalGetParkNotReviewed satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/park-reply-not-from-a-person': approvalGetParkReplyNotFromAPerson satisfies Exchange<
        { approval_id: string },
        Approval
    >,
    'approval.get/park-sent-from-chat': approvalGetParkSentFromChat satisfies Exchange<
        { approval_id: string },
        Approval
    >,
    'approval.get/released-automatically': approvalGetReleasedAutomatically satisfies Exchange<
        { approval_id: string },
        Approval
    >,
    'approval.get/with-doubts': approvalGetWithDoubts satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/other-pending': approvalGetOtherPending satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/pending-with-preview': approvalGetPendingWithPreview satisfies Exchange<
        { approval_id: string },
        Approval
    >,
    'approval.list/card-invitation-pending': approvalListCardInvitationPending satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/card-message-reasked': approvalListCardMessageReasked satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.approve/card-message-reasked': approvalApproveCardMessageReasked satisfies Exchange<
        ApprovalDecisionRequest,
        Approval
    >,
    'approval.list/card-message-all-pending': approvalListCardMessageAllPending satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/card-message-all-after-edit': approvalListCardMessageAllAfterEdit satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/card-message-all-reasked': approvalListCardMessageAllReasked satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/card-message-item-after-edit': approvalListCardMessageItemAfterEdit satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/card-message-item-reasked': approvalListCardMessageItemReasked satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/card-message-item-all-after-edit': approvalListCardMessageItemAllAfterEdit satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/card-message-item-all-reasked': approvalListCardMessageItemAllReasked satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/card-message-item-all-approved': approvalListCardMessageItemAllApproved satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'workitem.get/card-message-approved': workitemGetCardMessageApproved satisfies Exchange<
        { work_item_id: string },
        WorkItem
    >,
    'approval.list/needs-message-all-after-edit': approvalListNeedsMessageAllAfterEdit satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/needs-message-all-cancelled': approvalListNeedsMessageAllCancelled satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'workitem.get/needs-message-cancelled': workitemGetNeedsMessageCancelled satisfies Exchange<
        { work_item_id: string },
        WorkItem
    >,
    'approval.list/card-message-all-approved': approvalListCardMessageAllApproved satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/card-message-item-approved': approvalListCardMessageItemApproved satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'workitem.get/needs-message-requeued': workitemGetNeedsMessageRequeued satisfies Exchange<
        { work_item_id: string },
        WorkItem
    >,
    'workitem.get/card-message-requeued': workitemGetCardMessageRequeued satisfies Exchange<
        { work_item_id: string },
        WorkItem
    >,
    'workitem.get/card-message-reparked': workitemGetCardMessageReparked satisfies Exchange<
        { work_item_id: string },
        WorkItem
    >,
    'approval.list/card-message-after-edit': approvalListCardMessageAfterEdit satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/card-message-pending': approvalListCardMessagePending satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/by-campaign-pending': approvalListByCampaignPending satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/by-work-item': approvalListByWorkItem satisfies Exchange<ApprovalListRequest, Page<ApprovalSummary>>,
    'approval.list/decided': approvalListDecided satisfies Exchange<ApprovalListRequest, Page<ApprovalSummary>>,
    'approval.list/empty': approvalListEmpty satisfies Exchange<ApprovalListRequest, Page<ApprovalSummary>>,
    'approval.list/messages-pending': approvalListMessagesPending satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/needs-after-edit': approvalListNeedsAfterEdit satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/needs-first-page': approvalListNeedsFirstPage satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/needs-inbox': approvalListNeedsInbox satisfies Exchange<ApprovalListRequest, Page<ApprovalSummary>>,
    'approval.list/needs-next-page': approvalListNeedsNextPage satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/needs-next-page-after': approvalListNeedsNextPageAfter satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/needs-two': approvalListNeedsTwo satisfies Exchange<ApprovalListRequest, Page<ApprovalSummary>>,
    'approval.list/needs-two-after': approvalListNeedsTwoAfter satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/one-mode-unset': approvalListOneModeUnset satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/pending': approvalListPending satisfies Exchange<ApprovalListRequest, Page<ApprovalSummary>>,
    'approval.list/summer-pending': approvalListSummerPending satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/unauthorized': approvalListUnauthorized satisfies Exchange<ApprovalListRequest, ErrorEnvelope>,
    'approval.reject/from-work-item': approvalRejectFromWorkItem satisfies Exchange<ApprovalDecisionRequest, Approval>,
    'approval.reject/rejected': approvalRejectRejected satisfies Exchange<ApprovalDecisionRequest, Approval>,
    'campaign.activity/all-30-days': campaignActivityAll30Days satisfies Exchange<
        CampaignActivityRequest,
        CampaignActivityResponse
    >,
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
    'campaign.activity/summer': campaignActivitySummer satisfies Exchange<
        CampaignActivityRequest,
        CampaignActivityResponse
    >,
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
    // The list a person gave in an agent's chat, one story: the agent read as a draft, the list added and added again,
    // the agent started, its send mode for people who have not replied set to automatic, paused, then archived.
    'campaign.add_contacts/own-list': campaignAddContactsOwnList satisfies Exchange<ListAddRequest, AddContactsResult>,
    'campaign.add_contacts/own-list-again': campaignAddContactsOwnListAgain satisfies Exchange<
        ListAddRequest,
        AddContactsResult
    >,
    'campaign.add_contacts/own-list-archived': campaignAddContactsOwnListArchived satisfies Exchange<
        ListAddRequest,
        ErrorEnvelope
    >,
    'campaign.get/own-list': campaignGetOwnList satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/own-list-running': campaignGetOwnListRunning satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/own-list-automatic': campaignGetOwnListAutomatic satisfies Exchange<
        { campaign_id: string },
        Campaign
    >,
    'campaign.get/own-list-archived': campaignGetOwnListArchived satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/own-list-paused': campaignGetOwnListPaused satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/archived': campaignGetArchived satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/details': campaignGetDetails satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/gone': campaignGetGone satisfies Exchange<{ campaign_id: string }, ErrorEnvelope>,
    'campaign.get/gone-here': campaignGetGoneHere satisfies Exchange<{ campaign_id: string }, ErrorEnvelope>,
    'campaign.get/in-project-with-folder': campaignGetInProjectWithFolder satisfies Exchange<
        { campaign_id: string },
        Campaign
    >,
    'campaign.get/inbox': campaignGetInbox satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/inbox-quiet': campaignGetInboxQuiet satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/making': campaignGetMaking satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/many': campaignGetMany satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/one': campaignGetOne satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/send-mode-override': campaignGetSendModeOverride satisfies Exchange<
        { campaign_id: string },
        Campaign
    >,
    'campaign.set_send_mode/cleared': campaignSetSendModeCleared satisfies Exchange<
        CampaignSetSendModeRequest,
        Campaign
    >,
    'campaign.set_send_mode/not-human': campaignSetSendModeNotHuman satisfies Exchange<
        CampaignSetSendModeRequest,
        ErrorEnvelope
    >,
    'campaign.set_send_mode/set': campaignSetSendModeSet satisfies Exchange<CampaignSetSendModeRequest, Campaign>,
    'campaign.set_send_mode/warm-automatic': campaignSetSendModeWarmAutomatic satisfies Exchange<
        CampaignSetSendModeRequest,
        Campaign
    >,
    'campaign.get/started': campaignGetStarted satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/summer': campaignGetSummer satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/unknown': campaignGetUnknown satisfies Exchange<{ campaign_id: string }, ErrorEnvelope>,
    'campaign.get/unlinked': campaignGetUnlinked satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.list/card-chips': campaignListCardChips satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'campaign.list/campaigns-for-picker': campaignListCampaignsForPicker satisfies Exchange<
        CampaignListRequest,
        Page<CampaignSummary>
    >,
    'campaign.list/first-page': campaignListFirstPage satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'campaign.list/for-picker': campaignListForPicker satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'campaign.list/inbox': campaignListInbox satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'campaign.list/include-archived': campaignListIncludeArchived satisfies Exchange<
        CampaignListRequest,
        Page<CampaignSummary>
    >,
    'campaign.list/query': campaignListQuery satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'campaign.list/screen': campaignListScreen satisfies Exchange<CampaignListRequest, Page<CampaignSummary>>,
    'campaign.list/screen-empty': campaignListScreenEmpty satisfies Exchange<
        CampaignListRequest,
        Page<CampaignSummary>
    >,
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
    'campaign.summary/card-chips': campaignSummaryCardChips satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/all': campaignSummaryAll satisfies Exchange<CampaignSummaryRequest, CampaignSummaryResponse>,
    'campaign.summary/everyone': campaignSummaryEveryone satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/everyone-without-archived-empty': campaignSummaryEveryoneWithoutArchivedEmpty satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/everyone-without-archived': campaignSummaryEveryoneWithoutArchived satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/all-after': campaignSummaryAllAfter satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/archived': campaignSummaryArchived satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/inbox-all': campaignSummaryInboxAll satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/inbox-one': campaignSummaryInboxOne satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/inbox-quiet': campaignSummaryInboxQuiet satisfies Exchange<
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
    'campaign.summary/some-without-archived': campaignSummarySomeWithoutArchived satisfies Exchange<
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
    'campaign.summary/unlinked-archived-not-left-out': campaignSummaryUnlinkedArchivedNotLeftOut satisfies Exchange<
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
    'contact.list/by-activity-empty': contactListByActivityEmpty satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/by-activity-query': contactListByActivityQuery satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/by-activity-query-aguilar': contactListByActivityQueryAguilar satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'contact.list/by-activity-sent-from-the-account': contactListByActivitySentFromTheAccount satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'contact.list/by-activity-several': contactListByActivitySeveral satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'contact.list/by-activity-summary-new-without-archived':
        contactListByActivitySummaryNewWithoutArchived satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/by-activity-summary-query': contactListByActivitySummaryQuery satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'contact.list/by-activity-summary-waiting': contactListByActivitySummaryWaiting satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'contact.list/by-campaign': contactListByCampaign satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/by-campaign-archived': contactListByCampaignArchived satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'contact.list/by-campaign-query': contactListByCampaignQuery satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/by-campaign-query-archived': contactListByCampaignQueryArchived satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'contact.list/by-campaign-query-none': contactListByCampaignQueryNone satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'contact.list/by-campaign-summary': contactListByCampaignSummary satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'contact.list/by-campaign-without-archived': contactListByCampaignWithoutArchived satisfies Exchange<
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
    'contact.threads/inbox-mateo': contactThreadsInboxMateo satisfies Exchange<ContactThreadsRequest, ContactThreads>,
    'contact.threads/inbox-paula': contactThreadsInboxPaula satisfies Exchange<ContactThreadsRequest, ErrorEnvelope>,
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
    'decision.get/needs-inbox-question': decisionGetNeedsInboxQuestion satisfies Exchange<
        { decision_id: string },
        Decision
    >,
    'decision.get/needs-question': decisionGetNeedsQuestion satisfies Exchange<{ decision_id: string }, Decision>,
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
    'decision.list/needs-inbox-questions': decisionListNeedsInboxQuestions satisfies Exchange<
        DecisionListRequest,
        Page<DecisionSummary>
    >,
    'decision.list/needs-questions': decisionListNeedsQuestions satisfies Exchange<
        DecisionListRequest,
        Page<DecisionSummary>
    >,
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
    'journal.list/by-campaign-replies': journalListByCampaignReplies satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/by-campaign-replies-none': journalListByCampaignRepliesNone satisfies Exchange<
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
    'journal.list/card-choice-acts': journalListCardChoiceActs satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/card-choice-acts-none': journalListCardChoiceActsNone satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'campaign.get/card-plan-draft': campaignGetCardPlanDraft satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.summary/card-plan-draft': campaignSummaryCardPlanDraft satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'route.resolve/card-plan-email': routeResolveCardPlanEmail satisfies Exchange<RouteResolveRequest, RouteResolution>,
    'route.resolve/card-plan-linkedin': routeResolveCardPlanLinkedin satisfies Exchange<
        RouteResolveRequest,
        RouteResolution
    >,
    'campaign.get/card-plan-unrouted': campaignGetCardPlanUnrouted satisfies Exchange<
        { campaign_id: string },
        Campaign
    >,
    'campaign.summary/card-plan-unrouted': campaignSummaryCardPlanUnrouted satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'route.resolve/card-plan-no-route-email': routeResolveCardPlanNoRouteEmail satisfies Exchange<
        RouteResolveRequest,
        ErrorEnvelope
    >,
    'campaign.get/card-plan-channels': campaignGetCardPlanChannels satisfies Exchange<
        { campaign_id: string },
        Campaign
    >,
    'campaign.summary/card-plan-channels': campaignSummaryCardPlanChannels satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'route.resolve/on-a-channel': routeResolveOnAChannel satisfies Exchange<RouteResolveRequest, RouteResolution>,
    'route.list/card-plan-global-channels': routeListCardPlanGlobalChannels satisfies Exchange<object, Routes>,
    'route.list/card-accounts': routeListCardAccounts satisfies Exchange<object, Routes>,
    'system.info/send-modes-set': systemInfoSendModesSet satisfies Exchange<object, SystemInfo>,
    'campaign.get/card-status': campaignGetCardStatus satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/card-status-draft': campaignGetCardStatusDraft satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/card-archived': campaignGetCardArchived satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/card-unknown-campaign': campaignGetCardUnknownCampaign satisfies Exchange<
        { campaign_id: string },
        ErrorEnvelope
    >,
    'campaign.summary/card-status': campaignSummaryCardStatus satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/card-status-later': campaignSummaryCardStatusLater satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/card-status-draft': campaignSummaryCardStatusDraft satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/card-archived': campaignSummaryCardArchived satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.activity/card-status-30-days': campaignActivityCardStatus30Days satisfies Exchange<
        CampaignActivityRequest,
        CampaignActivityResponse
    >,
    'campaign.activity/card-archived-30-days': campaignActivityCardArchived30Days satisfies Exchange<
        CampaignActivityRequest,
        CampaignActivityResponse
    >,
    'campaign.list_contacts/card-people-first': campaignListContactsCardPeopleFirst satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/card-people-query': campaignListContactsCardPeopleQuery satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/card-people-waiting': campaignListContactsCardPeopleWaiting satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/card-people-empty': campaignListContactsCardPeopleEmpty satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'campaign.list_contacts/card-archived': campaignListContactsCardArchived satisfies Exchange<
        ListContactsRequest,
        Page<MembershipItem>
    >,
    'contact.list/card-people-everyone': contactListCardPeopleEveryone satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'contact.list/card-people-query': contactListCardPeopleQuery satisfies Exchange<ContactListRequest, Page<Contact>>,
    'contact.list/card-people-nobody': contactListCardPeopleNobody satisfies Exchange<
        ContactListRequest,
        Page<Contact>
    >,
    'approval.list/card-status-waiting': approvalListCardStatusWaiting satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/card-needs-all': approvalListCardNeedsAll satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/card-archived': approvalListCardArchived satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'decision.list/card-status-waiting': decisionListCardStatusWaiting satisfies Exchange<
        DecisionListRequest,
        Page<DecisionSummary>
    >,
    'decision.list/card-needs-all': decisionListCardNeedsAll satisfies Exchange<
        DecisionListRequest,
        Page<DecisionSummary>
    >,
    'decision.list/card-archived': decisionListCardArchived satisfies Exchange<
        DecisionListRequest,
        Page<DecisionSummary>
    >,
    'approval.get/card-needs-pending': approvalGetCardNeedsPending satisfies Exchange<
        { approval_id: string },
        Approval
    >,
    'approval.get/card-needs-decided': approvalGetCardNeedsDecided satisfies Exchange<
        { approval_id: string },
        Approval
    >,
    'decision.get/card-needs-question': decisionGetCardNeedsQuestion satisfies Exchange<
        { decision_id: string },
        Decision
    >,
    'plugin.list/card-accounts-reply': pluginListCardAccountsReply satisfies Exchange<object, object>,
    'plugin.list/none': pluginListNone satisfies Exchange<object, object>,
    'reply.count/archived-named': replyCountArchivedNamed satisfies Exchange<ReplyCountRequest, ReplyCountResponse>,
    'reply.count/every-campaign': replyCountEveryCampaign satisfies Exchange<ReplyCountRequest, ReplyCountResponse>,
    'reply.count/period-none': replyCountPeriodNone satisfies Exchange<ReplyCountRequest, ReplyCountResponse>,
    'reply.count/period-spring': replyCountPeriodSpring satisfies Exchange<ReplyCountRequest, ReplyCountResponse>,
    'reply.count/until-before-since': replyCountUntilBeforeSince satisfies Exchange<ReplyCountRequest, ErrorEnvelope>,
    'role.list/all': roleListAll satisfies Exchange<object, Page<Role>>,
    'route.list/channel-routes': routeListChannelRoutes satisfies Exchange<object, Routes>,
    'route.list/card-accounts-reply': routeListCardAccountsReply satisfies Exchange<object, Routes>,
    'route.list/empty': routeListEmpty satisfies Exchange<object, Routes>,
    'route.resolve/elsewhere': routeResolveElsewhere satisfies Exchange<RouteResolveRequest, RouteResolution>,
    'route.resolve/elsewhere-first': routeResolveElsewhereFirst satisfies Exchange<
        RouteResolveRequest,
        RouteResolution
    >,
    'route.resolve/global-default': routeResolveGlobalDefault satisfies Exchange<RouteResolveRequest, RouteResolution>,
    'route.resolve/no-route': routeResolveNoRoute satisfies Exchange<RouteResolveRequest, ErrorEnvelope>,
    'route.resolve/own-route': routeResolveOwnRoute satisfies Exchange<RouteResolveRequest, RouteResolution>,
    'route.resolve/unrouted-first': routeResolveUnroutedFirst satisfies Exchange<RouteResolveRequest, ErrorEnvelope>,
    'preference.list/mode-automatic-voice-unset': preferenceListModeAutomaticVoiceUnset satisfies Exchange<
        object,
        Page<PreferenceItem>
    >,
    'preference.list/modes-every-message': preferenceListModesEveryMessage satisfies Exchange<
        object,
        Page<PreferenceItem>
    >,
    'preference.list/none': preferenceListNone satisfies Exchange<object, Page<PreferenceItem>>,
    'preference.list/set': preferenceListSet satisfies Exchange<object, Page<PreferenceItem>>,
    'preference.set/send-mode-cold': preferenceSetSendModeCold satisfies Exchange<PreferenceSetRequest, PreferenceItem>,
    'preference.set/send-mode-cold-every-message': preferenceSetSendModeColdEveryMessage satisfies Exchange<
        PreferenceSetRequest,
        PreferenceItem
    >,
    'preference.set/send-mode-warm': preferenceSetSendModeWarm satisfies Exchange<PreferenceSetRequest, PreferenceItem>,
    'notification.disable/plugin': notificationDisablePlugin satisfies Exchange<
        NotificationSwitchRequest,
        NotificationsInfo
    >,
    'notification.disable/toast': notificationDisableToast satisfies Exchange<
        NotificationSwitchRequest,
        NotificationsInfo
    >,
    'notification.enable/not-configured': notificationEnableNotConfigured satisfies Exchange<
        NotificationSwitchRequest,
        ErrorEnvelope
    >,
    'notification.enable/not-named': notificationEnableNotNamed satisfies Exchange<
        NotificationSwitchRequest,
        ErrorEnvelope
    >,
    'notification.enable/notifications-off': notificationEnableNotificationsOff satisfies Exchange<
        NotificationSwitchRequest,
        ErrorEnvelope
    >,
    'notification.enable/toast': notificationEnableToast satisfies Exchange<
        NotificationSwitchRequest,
        NotificationsInfo
    >,
    'notification.enable/unknown-channel': notificationEnableUnknownChannel satisfies Exchange<
        NotificationSwitchRequest,
        ErrorEnvelope
    >,
    'preference.set/sender': preferenceSetSender satisfies Exchange<PreferenceSetRequest, PreferenceItem>,
    'preference.set/tone': preferenceSetTone satisfies Exchange<PreferenceSetRequest, PreferenceItem>,
    'preference.unset/send-mode-cold': preferenceUnsetSendModeCold satisfies Exchange<
        PreferenceUnsetRequest,
        PreferenceItem
    >,
    'project.get/by-id': projectGetById satisfies Exchange<{ project_id: string }, Project>,
    'project.get/by-slug': projectGetBySlug satisfies Exchange<{ slug: string }, Project>,
    'sender.list/card-message-sender': senderListCardMessageSender satisfies Exchange<
        SenderListRequest,
        SenderListResponse
    >,
    'sender.list/card-sender-other': senderListCardSenderOther satisfies Exchange<
        SenderListRequest,
        SenderListResponse
    >,
    'sender.list/card-sender-unnamed': senderListCardSenderUnnamed satisfies Exchange<
        SenderListRequest,
        SenderListResponse
    >,
    'sender.list/card-sender-unread': senderListCardSenderUnread satisfies Exchange<SenderListRequest, ErrorEnvelope>,
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
    'workitem.get/needs-message-parked': workitemGetNeedsMessageParked satisfies Exchange<
        { work_item_id: string },
        WorkItem
    >,
    'workitem.get/needs-page-two-parked': workitemGetNeedsPageTwoParked satisfies Exchange<
        { work_item_id: string },
        WorkItem
    >,
    'workitem.get/needs-two-first-parked': workitemGetNeedsTwoFirstParked satisfies Exchange<
        { work_item_id: string },
        WorkItem
    >,
    'workitem.get/parked-edited-send': workitemGetParkedEditedSend satisfies Exchange<
        { work_item_id: string },
        WorkItem
    >,
    'workitem.get/card-message-parked': workitemGetCardMessageParked satisfies Exchange<
        { work_item_id: string },
        WorkItem
    >,
    'workitem.get/parked-send': workitemGetParkedSend satisfies Exchange<{ work_item_id: string }, WorkItem>,
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
    'approval.list/archive-asked-and-given': approvalListArchiveAskedAndGiven satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'campaign.archive/archive-spring': campaignArchiveArchiveSpring satisfies Exchange<
        CampaignTransitionRequest,
        Campaign
    >,
    'campaign.archive/archive-spring-again': campaignArchiveArchiveSpringAgain satisfies Exchange<
        CampaignTransitionRequest,
        Campaign
    >,
    'campaign.get/archive-spring': campaignGetArchiveSpring satisfies Exchange<{ campaign_id: string }, Campaign>,
    'campaign.get/archive-spring-archived': campaignGetArchiveSpringArchived satisfies Exchange<
        { campaign_id: string },
        Campaign
    >,
    'campaign.list/archive-after': campaignListArchiveAfter satisfies Exchange<
        CampaignListRequest,
        Page<CampaignSummary>
    >,
    'campaign.list/archive-before': campaignListArchiveBefore satisfies Exchange<
        CampaignListRequest,
        Page<CampaignSummary>
    >,
    'campaign.summary/archive-after': campaignSummaryArchiveAfter satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/archive-before': campaignSummaryArchiveBefore satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/archive-spring': campaignSummaryArchiveSpring satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'campaign.summary/archive-spring-archived': campaignSummaryArchiveSpringArchived satisfies Exchange<
        CampaignSummaryRequest,
        CampaignSummaryResponse
    >,
    'decision.list/archive-pending': decisionListArchivePending satisfies Exchange<
        DecisionListRequest,
        Page<DecisionSummary>
    >,
    'route.resolve/archive-pause': routeResolveArchivePause satisfies Exchange<RouteResolveRequest, RouteResolution>,
    'route.resolve/archive-pause-unusable': routeResolveArchivePauseUnusable satisfies Exchange<
        RouteResolveRequest,
        RouteResolution
    >,
    'workitem.list/archive-open': workitemListArchiveOpen satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/creating-none': workitemListCreatingNone satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/creating-open': workitemListCreatingOpen satisfies Exchange<
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
    'workitem.update/card-message-edited': workitemUpdateCardMessageEdited satisfies Exchange<
        WorkItemUpdateRequest,
        WorkItem
    >,
    'workitem.update/needs-message-edited': workitemUpdateNeedsMessageEdited satisfies Exchange<
        WorkItemUpdateRequest,
        WorkItem
    >,
    // The person panel's recordings.
    'campaign.add_contacts/move-in': personCampaignAddContactsMoveIn satisfies Exchange<
        AddContactsRequest,
        AddContactsResult
    >,
    'campaign.add_contacts/move-in-rejected': personCampaignAddContactsMoveInRejected satisfies Exchange<
        AddContactsRequest,
        AddContactsResult
    >,
    'campaign.list/move-targets': personCampaignListMoveTargets satisfies Exchange<
        CampaignListRequest,
        Page<CampaignSummary>
    >,
    'campaign.remove_contacts/exclude-archived': personCampaignRemoveContactsExcludeArchived satisfies Exchange<
        RemoveContactsRequest,
        ErrorEnvelope
    >,
    'campaign.remove_contacts/exclude-one': personCampaignRemoveContactsExcludeOne satisfies Exchange<
        RemoveContactsRequest,
        RemoveContactsResult
    >,
    'campaign.remove_contacts/move-out': personCampaignRemoveContactsMoveOut satisfies Exchange<
        RemoveContactsRequest,
        RemoveContactsResult
    >,
    'campaign.remove_contacts/move-out-archived': personCampaignRemoveContactsMoveOutArchived satisfies Exchange<
        RemoveContactsRequest,
        ErrorEnvelope
    >,
    'contact.get/person-after-exclude': personContactGetPersonAfterExclude satisfies Exchange<
        { contact_id: string },
        Contact
    >,
    'contact.get/person-after-half-move': personContactGetPersonAfterHalfMove satisfies Exchange<
        { contact_id: string },
        Contact
    >,
    'contact.get/person-after-move': personContactGetPersonAfterMove satisfies Exchange<
        { contact_id: string },
        Contact
    >,
    'contact.get/person-finished': personContactGetPersonFinished satisfies Exchange<{ contact_id: string }, Contact>,
    'contact.get/person-panel': personContactGetPersonPanel satisfies Exchange<{ contact_id: string }, Contact>,
    'contact.update/person-channels': personContactUpdatePersonChannels satisfies Exchange<
        ContactUpdateRequest,
        Contact
    >,
    'contact.update/person-cleared': personContactUpdatePersonCleared satisfies Exchange<ContactUpdateRequest, Contact>,
    'contact.update/person-primaries': personContactUpdatePersonPrimaries satisfies Exchange<
        ContactUpdateRequest,
        Contact
    >,
    'contact.update/person-refused': personContactUpdatePersonRefused satisfies Exchange<
        ContactUpdateRequest,
        ErrorEnvelope
    >,
    'contact.update/person-saved': personContactUpdatePersonSaved satisfies Exchange<ContactUpdateRequest, Contact>,
    'suppression.add/person-email': personSuppressionAddPersonEmail satisfies Exchange<SuppressionRequest, Suppression>,
    'suppression.add/person-linkedin': personSuppressionAddPersonLinkedin satisfies Exchange<
        SuppressionRequest,
        Suppression
    >,
    'suppression.list/aguilar-email-clear': personSuppressionListAguilarEmailClear satisfies Exchange<
        SuppressionListRequest,
        Page<Suppression>
    >,
    'suppression.list/grace-email-clear': personSuppressionListGraceEmailClear satisfies Exchange<
        SuppressionListRequest,
        Page<Suppression>
    >,
    'suppression.list/mateo-email-clear': personSuppressionListMateoEmailClear satisfies Exchange<
        SuppressionListRequest,
        Page<Suppression>
    >,
    'suppression.list/mateo-linkedin-clear': personSuppressionListMateoLinkedinClear satisfies Exchange<
        SuppressionListRequest,
        Page<Suppression>
    >,
    'suppression.list/mateo-phone-clear': personSuppressionListMateoPhoneClear satisfies Exchange<
        SuppressionListRequest,
        Page<Suppression>
    >,
    'suppression.list/person-email-clear': personSuppressionListPersonEmailClear satisfies Exchange<
        SuppressionListRequest,
        Page<Suppression>
    >,
    'suppression.list/person-email-listed': personSuppressionListPersonEmailListed satisfies Exchange<
        SuppressionListRequest,
        Page<Suppression>
    >,
    'suppression.list/person-linkedin-clear': personSuppressionListPersonLinkedinClear satisfies Exchange<
        SuppressionListRequest,
        Page<Suppression>
    >,
    'suppression.list/person-linkedin-listed': personSuppressionListPersonLinkedinListed satisfies Exchange<
        SuppressionListRequest,
        Page<Suppression>
    >,
    'suppression.list/person-phone-clear': personSuppressionListPersonPhoneClear satisfies Exchange<
        SuppressionListRequest,
        Page<Suppression>
    >,
    'suppression.list/person-second-email-clear': personSuppressionListPersonSecondEmailClear satisfies Exchange<
        SuppressionListRequest,
        Page<Suppression>
    >,
    'suppression.remove/person-email': personSuppressionRemovePersonEmail satisfies Exchange<
        SuppressionRequest,
        SuppressionRemoved
    >,
    'suppression.remove/person-linkedin': personSuppressionRemovePersonLinkedin satisfies Exchange<
        SuppressionRequest,
        SuppressionRemoved
    >,
    'workitem.create/provider-archived': personWorkitemCreateProviderArchived satisfies Exchange<
        WorkItemCreateRequest,
        ErrorEnvelope
    >,
    'workitem.create/provider-contact-update': personWorkitemCreateProviderContactUpdate satisfies Exchange<
        WorkItemCreateRequest,
        WorkItem
    >,
    'workitem.create/researcher-archived': personWorkitemCreateResearcherArchived satisfies Exchange<
        WorkItemCreateRequest,
        ErrorEnvelope
    >,
    'workitem.create/researcher-asked': personWorkitemCreateResearcherAsked satisfies Exchange<
        WorkItemCreateRequest,
        WorkItem
    >,
    'workitem.create/researcher-asked-plain': personWorkitemCreateResearcherAskedPlain satisfies Exchange<
        WorkItemCreateRequest,
        WorkItem
    >,
    'workitem.get/researcher-finding': personWorkitemGetResearcherFinding satisfies Exchange<
        WorkItemGetRequest,
        WorkItemWithResult
    >,
    'workitem.get/researcher-finding-second': personWorkitemGetResearcherFindingSecond satisfies Exchange<
        WorkItemGetRequest,
        WorkItemWithResult
    >,
    'workitem.list/researcher-findings': personWorkitemListResearcherFindings satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/review-latest': workitemListReviewLatest satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/review-none': workitemListReviewNone satisfies Exchange<WorkItemListRequest, Page<WorkItemSummary>>,
    'workitem.list/researcher-findings-two': personWorkitemListResearcherFindingsTwo satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'approval.approve/panel-doubted': approvalApprovePanelDoubted satisfies Exchange<ApprovalDecisionRequest, Approval>,
    'approval.get/panel-doubted': approvalGetPanelDoubted satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.get/panel-moved': approvalGetPanelMoved satisfies Exchange<{ approval_id: string }, Approval>,
    'approval.list/panel-doubted': approvalListPanelDoubted satisfies Exchange<
        ApprovalListRequest,
        Page<ApprovalSummary>
    >,
    'approval.list/panel-moved': approvalListPanelMoved satisfies Exchange<ApprovalListRequest, Page<ApprovalSummary>>,
    'journal.list/panel-doubted': journalListPanelDoubted satisfies Exchange<JournalListRequest, Page<JournalEntry>>,
    'journal.list/panel-doubted-approved': journalListPanelDoubtedApproved satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/panel-moved': journalListPanelMoved satisfies Exchange<JournalListRequest, Page<JournalEntry>>,
    'journal.list/panel-moved-edited': journalListPanelMovedEdited satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'journal.list/person-sent-from-the-account': journalListPersonSentFromTheAccount satisfies Exchange<
        JournalListRequest,
        Page<JournalEntry>
    >,
    'workitem.count/panel-doubted-approved-done': workitemCountPanelDoubtedApprovedDone satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/panel-doubted-approved-now': workitemCountPanelDoubtedApprovedNow satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/panel-doubted-approved-planned': workitemCountPanelDoubtedApprovedPlanned satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/panel-doubted-done': workitemCountPanelDoubtedDone satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/panel-doubted-now': workitemCountPanelDoubtedNow satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/panel-doubted-planned': workitemCountPanelDoubtedPlanned satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/panel-moved-done': workitemCountPanelMovedDone satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/panel-moved-now': workitemCountPanelMovedNow satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/panel-moved-planned': workitemCountPanelMovedPlanned satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.get/panel-doubted': workitemGetPanelDoubted satisfies Exchange<{ work_item_id: string }, WorkItem>,
    'workitem.get/panel-doubted-approved': workitemGetPanelDoubtedApproved satisfies Exchange<
        { work_item_id: string },
        WorkItem
    >,
    'workitem.get/panel-moved': workitemGetPanelMoved satisfies Exchange<{ work_item_id: string }, WorkItem>,
    'workitem.get/panel-moved-edited': workitemGetPanelMovedEdited satisfies Exchange<
        { work_item_id: string },
        WorkItem
    >,
    'workitem.list/panel-doubted-approved-now': workitemListPanelDoubtedApprovedNow satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/panel-doubted-now': workitemListPanelDoubtedNow satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.list/panel-moved-now': workitemListPanelMovedNow satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'workitem.count/panel-moved-edited-done': workitemCountPanelMovedEditedDone satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/panel-moved-edited-now': workitemCountPanelMovedEditedNow satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.count/panel-moved-edited-planned': workitemCountPanelMovedEditedPlanned satisfies Exchange<
        WorkItemFilterRequest,
        WorkItemCount
    >,
    'workitem.list/panel-moved-edited-now': workitemListPanelMovedEditedNow satisfies Exchange<
        WorkItemListRequest,
        Page<WorkItemSummary>
    >,
    'approval.get/by-work-item': approvalGetByWorkItem satisfies Exchange<{ approval_id: string }, Approval>,
} as const;

export type FixtureName = keyof typeof fixtures;
