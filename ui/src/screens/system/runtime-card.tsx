import type { SystemInfo } from '../../api/types';
import { relative } from '../../time/format';

/**
 * What the runtime says of itself: its version, when it started, where it listens, and its data directory and
 * database. One card, drawn by the System screen and, in Live, by Settings, from the same `system.info` answer.
 */
export function RuntimeCard({ info, now }: { info: SystemInfo; now: Date }) {
    const listening = info.listening;
    return (
        <section className="card">
            <h2>
                Runtime <span className="badge success">running</span>
            </h2>
            <dl className="facts">
                <dt>Version</dt>
                <dd>
                    {info.runtime_version} · API {info.api_version}
                </dd>
                <dt>Started</dt>
                <dd>
                    {relative(new Date(info.started_at), now)} · instance{' '}
                    <span className="mono">{info.instance_id}</span>
                </dd>
                <dt>Address</dt>
                <dd>
                    <span className="mono">http://127.0.0.1:{listening?.port ?? '?'}</span>
                    {listening &&
                        listening.configured_port !== 0 &&
                        listening.configured_port !== listening.port &&
                        ` (${listening.configured_port} was taken)`}
                    {listening && listening.configured_port === 0 && ' (chosen by the OS)'}
                </dd>
                <dt>Data directory</dt>
                <dd>
                    <span className="mono">{info.data_dir}</span>
                </dd>
                <dt>Database</dt>
                <dd>{info.database.applied_migrations.length} migrations applied</dd>
            </dl>
        </section>
    );
}
