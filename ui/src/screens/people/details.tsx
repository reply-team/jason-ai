import { Fragment, type ReactNode } from 'react';
import type { Channel, Contact } from '../../api/types';
import { place, shortDate } from '../../time/format';
import { channelName } from './labels';

type Props = {
    person: Contact;
    now: Date;
    zone: string;
    /** What a screen adds to a channel's row, after its value. */
    aside?: ((channel: Channel) => ReactNode) | undefined;
};

function value(v: unknown): string {
    return typeof v === 'string' ? v : JSON.stringify(v);
}

/** The person's record: how to reach them, the facts, custom fields and what providers call them, in one grid. */
export function Details({ person, now, zone, aside }: Props) {
    const here = person.time_zone ? place(person.time_zone, now) : null;
    const pins = person.external_ids ?? [];
    const custom = Object.entries(person.custom ?? {});
    return (
        <div className="panel-body">
            <div className="section">
                <h3>Channels</h3>
                <dl className="facts">
                    {person.channels.map((c) => (
                        <Fragment key={`${c.channel}-${c.value}`}>
                            <dt>{channelName(c.channel)}</dt>
                            <dd>
                                {c.value}
                                {c.primary && (
                                    <>
                                        {' '}
                                        <span className="muted">primary</span>
                                    </>
                                )}
                                {aside?.(c)}
                            </dd>
                        </Fragment>
                    ))}
                </dl>
            </div>
            <dl className="facts">
                <dt>Company</dt>
                <dd>{person.company ?? '—'}</dd>
                <dt>Title</dt>
                <dd>{person.title ?? '—'}</dd>
                <dt>Time zone</dt>
                <dd>{person.time_zone ? `${person.time_zone}${here ? ` · ${here.time} there now` : ''}` : '—'}</dd>
                {person.created_at && (
                    <>
                        <dt>Created</dt>
                        <dd>{shortDate(new Date(person.created_at), zone, now)}</dd>
                    </>
                )}
            </dl>
            {custom.length > 0 && (
                <div className="section">
                    <h3>Custom fields</h3>
                    <dl className="facts">
                        {custom.map(([k, v]) => (
                            <>
                                <dt key={`${k}-k`}>{k}</dt>
                                <dd key={`${k}-v`}>{value(v)}</dd>
                            </>
                        ))}
                    </dl>
                </div>
            )}
            {pins.length > 0 && (
                <div className="section">
                    <h3>Provider ids</h3>
                    <dl className="facts">
                        {pins.map((pin) => (
                            <>
                                <dt key={`${pin.plugin_id}-${pin.kind}-k`}>
                                    {pin.plugin_id} {pin.kind}
                                </dt>
                                <dd key={`${pin.plugin_id}-${pin.kind}-v`}>
                                    <span className="mono">{pin.value}</span> · pinned{' '}
                                    {shortDate(new Date(pin.recorded_at), zone, now)}
                                    {pin.diverged_value && pin.diverged_at && (
                                        <span className="muted">
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
            <details className="fold">
                <summary>Raw contact record</summary>
                <pre>{JSON.stringify(person, null, 2)}</pre>
            </details>
        </div>
    );
}
