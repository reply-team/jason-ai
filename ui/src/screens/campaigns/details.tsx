import type { Campaign } from '../../api/types';
import { shortDate } from '../../time/format';

type Props = { campaign: Campaign; now: Date; zone: string };

function value(v: unknown): string {
    return typeof v === 'string' ? v : JSON.stringify(v);
}

/** The campaign's record: its facts, its context as the roles read it, and what each provider calls it. */
export function Details({ campaign, now, zone }: Props) {
    const context = Object.entries(campaign.context ?? {});
    const pins = campaign.external_ids ?? [];
    return (
        <div class="panel-body">
            <dl class="facts">
                <dt>Status</dt>
                <dd>{campaign.status}</dd>
                <dt>Created</dt>
                <dd>{shortDate(new Date(campaign.created_at), zone, now)}</dd>
                <dt>Updated</dt>
                <dd>{shortDate(new Date(campaign.updated_at), zone, now)}</dd>
                {campaign.archived_at && (
                    <>
                        <dt>Archived</dt>
                        <dd>{shortDate(new Date(campaign.archived_at), zone, now)}</dd>
                    </>
                )}
                <dt>Profile</dt>
                <dd>{campaign.execution_profile ?? 'the installation’s'}</dd>
                {campaign.review_seconds !== undefined && (
                    <>
                        <dt>Review</dt>
                        <dd>every {Math.round(campaign.review_seconds / 60)} minutes</dd>
                    </>
                )}
            </dl>
            <div class="section">
                <h3>Context</h3>
                {context.length === 0 ? (
                    <p class="hint">Nothing yet: the roles read this campaign from its context.</p>
                ) : (
                    <dl class="facts">
                        {context.map(([k, v]) => (
                            <>
                                <dt key={`${k}-k`}>{k}</dt>
                                <dd key={`${k}-v`}>{value(v)}</dd>
                            </>
                        ))}
                    </dl>
                )}
            </div>
            {pins.length > 0 && (
                <div class="section">
                    <h3>Provider ids</h3>
                    <dl class="facts">
                        {pins.map((pin) => (
                            <>
                                <dt key={`${pin.plugin_id}-${pin.kind}-k`}>
                                    {pin.plugin_id} {pin.kind}
                                </dt>
                                <dd key={`${pin.plugin_id}-${pin.kind}-v`}>
                                    <span class="mono">{pin.value}</span> · pinned{' '}
                                    {shortDate(new Date(pin.recorded_at), zone, now)}
                                    {pin.recorded_by_attempt_id && (
                                        <span class="muted"> by {pin.recorded_by_attempt_id}</span>
                                    )}
                                    {pin.diverged_value && pin.diverged_at && (
                                        <span class="muted">
                                            {' '}
                                            · later answered {pin.diverged_value} on{' '}
                                            {shortDate(new Date(pin.diverged_at), zone, now)}
                                        </span>
                                    )}
                                </dd>
                            </>
                        ))}
                    </dl>
                </div>
            )}
            <details class="fold">
                <summary>Raw campaign record</summary>
                <pre>{JSON.stringify(campaign, null, 2)}</pre>
            </details>
        </div>
    );
}
