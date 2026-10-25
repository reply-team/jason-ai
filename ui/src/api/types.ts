// Narrow on purpose: only what a screen reads. Statuses are strings because the recorded fixtures are JSON, and
// `satisfies` holds these shapes to them (src/test/fixtures.ts).
export type ActorRef = { type: string; id: string | null };
export type Page<T> = { items: T[]; next_cursor: string | null };
export type ErrorDetail = { field: string; code: string; message: string };
export type ErrorBody = { code: string; message: string; retryable: boolean; details?: ErrorDetail[] };
export type ErrorEnvelope = { error: ErrorBody };

export type ApprovalSummary = {
    id: string;
    work_item_id: string;
    campaign_id: string;
    operation: string;
    status: string;
    reason: string;
    plugin_id: string;
    requested_at: string;
    decided_at: string | null;
    decided_by: ActorRef | null;
    campaign_name: string;
    contact_name: string | null;
};
export type Approval = ApprovalSummary & {
    subject: Record<string, unknown>;
    preview: Record<string, unknown>;
    decision_reason: string | null;
};
export type ApprovalListRequest = {
    status?: string;
    statuses?: string[];
    campaign_id?: string;
    limit?: number;
    cursor?: string;
};
export type ApprovalDecisionRequest = { approval_id: string; actor: ActorRef; reason?: string };

export type DecisionOption = { label: string; detail: string | null };
export type DecisionReference = { kind: string | null; id: string };
export type DecisionSummary = {
    id: string;
    campaign_id: string;
    work_item_id: string;
    status: string;
    question: string;
    raised_at: string;
    answered_at: string | null;
    answered_by: ActorRef | null;
    campaign_name: string;
    contact_name: string | null;
};
export type Decision = DecisionSummary & {
    attempt_id: string;
    options: DecisionOption[] | null;
    references: DecisionReference[] | null;
    answer: string | null;
    chosen_option: string | null;
};
export type DecisionListRequest = {
    status?: string;
    statuses?: string[];
    campaign_id?: string;
    limit?: number;
    cursor?: string;
};
export type DecisionAnswerRequest = {
    decision_id: string;
    actor: ActorRef;
    answer: string;
    option?: string;
    reason?: string;
};

export type SystemInfo = {
    runtime_version: string;
    api_version: string;
    instance_id: string;
    pid: number;
    started_at: string;
    data_dir: string;
    database: { applied_migrations: string[]; newly_applied: string[]; backup_file: string | null };
    dispatcher: {
        state: string;
        tick_seconds: number;
        max_parallel: number;
        running_attempts: number;
        last_scan_at: string | null;
        scans: number;
        summons: number;
    };
    plugins: { active_count: number; snapshot_id: string; loaded_at: string; last_reload_activated: boolean | null };
    routes: {
        snapshot_id: string;
        activated_at: string;
        global_default_plugin: string | null;
        global_override_count: number;
        campaign_route_count: number;
    };
    update?: { available: boolean; version: string; checked_at: string; release_notes_url: string | null } | null;
    listening?: { port: number; configured_port: number } | null;
    ui?: {
        source: string;
        directory: string;
        version: string | null;
        api_version: string | null;
        api_version_matches: boolean;
        digest: string | null;
        problem: string | null;
    } | null;
    harness?: {
        root: string;
        root_state: string;
    } | null;
};
export type Plugin = {
    id: string;
    version: string;
    kind: string;
    operations: string[];
    status: string;
    digest: string;
};
export type Routes = {
    routing_snapshot_id: string;
    plugin_snapshot_id: string;
    global: { default: { plugin_id: string } | null; operations: Record<string, { plugin_id: string }> };
    campaigns: { campaign_id: string }[];
};
export type UiToken = { id: string; issued_to: string; issued_at: string; last_used_at: string | null };
export type UiTokens = { items: UiToken[] };
export type UiRevokeRequest = { token_id?: string; all?: boolean };
export type UiRevoke = { revoked: number };
export type UiSession = { token: string; token_id: string; actor_id: string; api_version: string; instance_id: string };

export type AttemptError = {
    code: string;
    message: string;
    retriable: boolean;
    class?: string;
    details?: ErrorDetail[];
};
export type WorkItemSummary = {
    id: string;
    campaign_id: string;
    contact_id: string | null;
    kind: string;
    role: string | null;
    operation: string | null;
    execution_profile: string | null;
    status: string;
    eligible: boolean;
    priority: number;
    not_before: string | null;
    due_at: string | null;
    retry_after: string | null;
    created_by: ActorRef;
    attempt_count: number;
    current_attempt_id: string | null;
    last_error: AttemptError | null;
    created_at: string;
    updated_at: string;
    finished_at: string | null;
    campaign_name: string;
    contact_name: string | null;
    running_since?: string | null;
};
export type AttemptProvenance = { plugin_id: string | null; plugin_version: string | null; operation: string | null };
export type Attempt = {
    id: string;
    number: number;
    status: string;
    command: string;
    execution_profile: string | null;
    error: AttemptError | null;
    launch: { exit_code: number | null } | null;
    claimed_at: string;
    started_at: string | null;
    finished_at: string | null;
    provenance?: AttemptProvenance;
};
export type ReportSummary = { id: string; received_at: string; reporter: ActorRef; effect: string; summary: string };
export type WorkItem = WorkItemSummary & {
    max_attempts: number | null;
    context: Record<string, unknown>;
    attempts?: Attempt[];
    external_reports?: ReportSummary[];
};
export type WorkItemFilterRequest = {
    campaign_id?: string;
    contact_id?: string;
    status?: string[];
    kind?: string;
    role?: string;
    order?: string;
    since?: string;
    until?: string;
};
export type WorkItemListRequest = WorkItemFilterRequest & { limit?: number; cursor?: string };
export type WorkItemCount = { total: number };
export type WorkItemUpdateRequest = {
    work_item_id: string;
    priority?: number;
    not_before?: string | null;
    due_at?: string | null;
    actor?: ActorRef;
};
export type WorkItemCancelRequest = { work_item_id: string; reason?: string; actor?: ActorRef };
export type JournalEntry = {
    id: string;
    ts: string;
    actor: ActorRef;
    kind: string;
    key: string | null;
    old: unknown;
    new: unknown;
    reason: string | null;
    campaign_id: string | null;
    work_item_id: string | null;
    contact_id: string | null;
    campaign_name: string | null;
    contact_name: string | null;
    work_item_kind: string | null;
    work_item_role: string | null;
    work_item_operation: string | null;
    actor_role: string | null;
};
export type JournalListRequest = {
    campaign_id?: string;
    work_item_id?: string;
    contact_id?: string;
    kinds?: string[];
    limit?: number;
    cursor?: string;
};
export type CampaignSummary = { id: string; name: string; status: string };
export type CampaignListRequest = { query?: string; include_archived?: boolean; limit?: number; cursor?: string };
export type Campaign = CampaignSummary & {
    context: Record<string, unknown>;
    external_ids: ExternalId[];
    execution_profile: string | null;
    review_seconds?: number;
    created_at: string;
    updated_at: string;
    archived_at: string | null;
};
export type CampaignTransitionRequest = { campaign_id: string; reason?: string; actor?: ActorRef };
/** The most campaigns one read names in `campaign_ids`: the runtime refuses more. */
export const campaignIdsLimit = 100;
export type CampaignSummaryRequest = { campaign_ids?: string[] };
export type CampaignSummaryRow = {
    campaign_id: string;
    people: number;
    waiting: number;
    failed: number;
    working: number;
    new: number;
    idle: number;
    sequence: number;
    out: number;
    removed: number;
    sequence_linked: boolean;
};
/** The same counts across every campaign asked, each person counted once however many of them count the person. */
export type CampaignSummaryTotal = Omit<CampaignSummaryRow, 'campaign_id' | 'sequence_linked'>;
export type CampaignSummaryResponse = { campaigns: CampaignSummaryRow[]; total: CampaignSummaryTotal };
export type CampaignActivityRequest = { campaign_ids?: string[]; since: string; until: string };
export type CampaignActivityRow = {
    campaign_id: string;
    people: number;
    added: number;
    worked_on: number;
    contacted: number;
    replied: number;
    opted_out: number;
    removed: number;
};
export type CampaignActivityTotal = Omit<CampaignActivityRow, 'campaign_id'>;
export type CampaignActivityResponse = { campaigns: CampaignActivityRow[]; total: CampaignActivityTotal };
/** What each bucket of a series is: an hour, a day or a Monday-to-Sunday week of the zone. */
export type ActivityPeriod = 'hour' | 'day' | 'week';
export type CampaignActivityByPeriodRequest = {
    campaign_ids?: string[];
    count: string;
    period: string;
    since: string;
    until: string;
    zone?: string;
};
/** One count with the bucket as its period: people once within it, so buckets may add up to more than campaign.activity's total over the window. */
export type ActivityBucket = { start: string; end: string; value: number };
export type CampaignActivityByPeriodResponse = { period: string; zone: string; buckets: ActivityBucket[] };
/** What a listed person is counted for: an approval or a question waiting, a failure, or what happened in the period. */
export type MembershipWhy = {
    kind: string;
    text: string | null;
    code: string | null;
    at: string | null;
    approval_id: string | null;
    decision_id: string | null;
};
/** A campaign that lists the person, and what it counts them for when a count was asked. */
export type CountedIn = { campaign_id: string; why?: MembershipWhy };
/** A person listed once: the first of their memberships' state and why, and every campaign that lists them. */
export type MembershipItem = {
    contact: Contact;
    state: string;
    added_at: string;
    updated_at: string;
    why?: MembershipWhy;
    counted: CountedIn[];
};
export type ListContactsRequest = {
    campaign_id?: string;
    campaign_ids?: string[];
    state?: string;
    summary?: string;
    activity?: string;
    since?: string;
    until?: string;
    query?: string;
    limit?: number;
    cursor?: string;
};
export type Role = { id: string; name: string; builtin: boolean };
export type Channel = { channel: string; value: string; primary: boolean };
export type ExternalId = {
    plugin_id: string;
    kind: string;
    value: string;
    recorded_at: string;
    recorded_by_attempt_id?: string;
    diverged_value?: string | null;
    diverged_at?: string | null;
};
export type ContactCampaign = {
    campaign_id: string;
    campaign_name: string;
    state: string;
    added_at: string;
    updated_at: string;
};
export type Contact = {
    id: string;
    first_name: string | null;
    last_name: string | null;
    company: string | null;
    title: string | null;
    channels: Channel[];
    time_zone?: string | null;
    custom?: Record<string, unknown>;
    external_ids?: ExternalId[];
    created_at?: string;
    archived_at?: string | null;
    /** Read beside the record by contact.get, contact.list and campaign.list_contacts only. */
    campaigns?: ContactCampaign[] | null;
    last_activity?: JournalEntry | null;
};
export type ContactListRequest = {
    query?: string;
    campaign_id?: string;
    order?: string;
    include_archived?: boolean;
    limit?: number;
    cursor?: string;
};
/** A thread as the provider lists it; the runtime passes it through untouched. */
export type Thread = {
    external_id: string;
    channel: string | null;
    subject: string | null;
    preview?: string | null;
    last_activity_at?: string | null;
    unread: boolean | null;
    has_meeting_intent?: boolean | null;
};
export type Message = {
    external_id: string | null;
    direction: string;
    sent_at: string | null;
    from: string | null;
    to: string[] | null;
    subject: string | null;
    body: string | null;
    truncated: boolean;
};
export type ContactThreadsRequest = { contact_id: string; plugin_id?: string; limit?: number; offset?: number };
export type ContactThreads = { plugin_id: string; items: Thread[]; has_more: boolean };
export type ContactThreadRequest = {
    contact_id: string;
    plugin_id?: string;
    thread_id: string;
    limit?: number;
    offset?: number;
};
export type ContactThread = { plugin_id: string; thread: Thread; messages: Message[]; has_more: boolean };
export type AddContactsRequest = {
    campaign_id: string;
    contacts: { contact_id: string }[];
    reason?: string;
    actor?: ActorRef;
};
export type AddContactsResult = {
    campaign_id: string;
    summary: { added: number; already_member: number; rejected: number };
    items: {
        index: number;
        status: string;
        contact_id: string | null;
        state: string | null;
        error: { code: string; message: string } | null;
    }[];
};
