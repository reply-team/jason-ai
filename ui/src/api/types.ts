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
export type ApprovalListRequest = { status?: string; statuses?: string[]; limit?: number; cursor?: string };
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
export type DecisionListRequest = { status?: string; statuses?: string[]; limit?: number; cursor?: string };
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
};
export type JournalListRequest = { work_item_id?: string; limit?: number; cursor?: string };
export type CampaignSummary = { id: string; name: string; status: string };
export type CampaignListRequest = { query?: string; limit?: number; cursor?: string };
export type Role = { id: string; name: string; builtin: boolean };
export type Channel = { channel: string; value: string; primary: boolean };
export type Contact = {
    id: string;
    first_name: string | null;
    last_name: string | null;
    company: string | null;
    title: string | null;
    channels: Channel[];
};
export type ContactListRequest = { query?: string; limit?: number; cursor?: string };
