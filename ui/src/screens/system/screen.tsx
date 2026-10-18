import { useEffect } from 'preact/hooks';
import { useApi } from '../../api/context';
import { session } from '../../api/session';
import { clock, relative } from '../../time/format';
import * as store from './store';

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

export function SystemScreen() {
    const api = useApi();
    useEffect(() => {
        void store.load(api);
    }, [api]);
    const now = new Date();
    const info = store.info.value;
    const ui = info?.ui;
    const listening = info?.listening;
    return (
        <div class="main single">
            <div class="list" style="border-right:0">
                <div class="toolbar">
                    <h1>System</h1>
                    <span class="spacer" />
                    <button class="iconbtn" type="button" title="Refresh" onClick={() => void store.load(api)}>
                        ⟳
                    </button>
                </div>
                {store.failure.value && (
                    <div class="notice danger" role="alert">
                        {store.failure.value}
                    </div>
                )}
                {info?.update?.available && (
                    <div class="notice">
                        Jason {info.update.version} is available. Update from a terminal with{' '}
                        <code>jason update apply</code>, or ask your agent.
                        {info.update.release_notes_url && (
                            <>
                                {' '}
                                <a href={info.update.release_notes_url} target="_blank" rel="noreferrer">
                                    What changed
                                </a>
                            </>
                        )}
                    </div>
                )}
                {info && (
                    <div class="sys">
                        <section class="card">
                            <h2>
                                Runtime <span class="badge success">running</span>
                            </h2>
                            <dl class="facts">
                                <dt>Version</dt>
                                <dd>
                                    {info.runtime_version} · API {info.api_version}
                                </dd>
                                <dt>Started</dt>
                                <dd>
                                    {relative(new Date(info.started_at), now)} · instance{' '}
                                    <span class="mono">{info.instance_id}</span>
                                </dd>
                                <dt>Address</dt>
                                <dd>
                                    <span class="mono">http://127.0.0.1:{listening?.port ?? '?'}</span>
                                    {listening &&
                                        listening.configured_port !== 0 &&
                                        listening.configured_port !== listening.port &&
                                        ` (${listening.configured_port} was taken)`}
                                    {listening && listening.configured_port === 0 && ' (chosen by the OS)'}
                                </dd>
                                <dt>Data directory</dt>
                                <dd>
                                    <span class="mono">{info.data_dir}</span>
                                </dd>
                                <dt>Database</dt>
                                <dd>{info.database.applied_migrations.length} migrations applied</dd>
                            </dl>
                        </section>

                        <section class="card">
                            <h2>
                                Dispatcher{' '}
                                <span class={`badge ${info.dispatcher.state === 'running' ? 'success' : ''}`}>
                                    {info.dispatcher.state}
                                </span>
                            </h2>
                            <dl class="facts">
                                <dt>Running now</dt>
                                <dd>
                                    {info.dispatcher.running_attempts} attempts of {info.dispatcher.max_parallel}{' '}
                                    allowed in parallel
                                </dd>
                                <dt>Last scan</dt>
                                <dd>
                                    {info.dispatcher.last_scan_at
                                        ? clock(new Date(info.dispatcher.last_scan_at), zone)
                                        : 'none yet'}{' '}
                                    · every {info.dispatcher.tick_seconds} seconds · {info.dispatcher.scans} scans since
                                    start
                                </dd>
                                <dt>Summons</dt>
                                <dd>{info.dispatcher.summons}</dd>
                            </dl>
                        </section>

                        <section class="card wide">
                            <h2>
                                Plugins <span class="badge">{info.plugins.active_count} active</span>
                            </h2>
                            {store.plugins.value.length > 0 ? (
                                <table class="tbl">
                                    <thead>
                                        <tr>
                                            <th>Plugin</th>
                                            <th>Kind</th>
                                            <th>Operations</th>
                                            <th>Status</th>
                                            <th class="r">Digest</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {store.plugins.value.map((p) => (
                                            <tr key={p.id}>
                                                <td>
                                                    <strong>{p.id}</strong> {p.version}
                                                </td>
                                                <td>{p.kind}</td>
                                                <td>{p.operations.join(', ')}</td>
                                                <td>{p.status}</td>
                                                <td class="r mono">{p.digest.slice(7, 19)}</td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            ) : (
                                <p class="empty">
                                    No plugin is installed; nothing reaches a provider until one is, and a route names
                                    it.
                                </p>
                            )}
                        </section>

                        <section class="card">
                            <h2>Routes</h2>
                            <dl class="facts">
                                <dt>Default provider</dt>
                                <dd>{store.routes.value?.global.default?.plugin_id ?? 'none'}</dd>
                                <dt>Overrides</dt>
                                <dd>
                                    {info.routes.global_override_count} global · {info.routes.campaign_route_count} per
                                    campaign
                                </dd>
                                <dt>Activated</dt>
                                <dd>
                                    {relative(new Date(info.routes.activated_at), now)} · snapshot{' '}
                                    <span class="mono">{info.routes.snapshot_id}</span>
                                </dd>
                            </dl>
                        </section>

                        <section class="card">
                            <h2>This UI {ui && <span class="badge accent">{ui.source}</span>}</h2>
                            {ui ? (
                                <dl class="facts">
                                    <dt>Served from</dt>
                                    <dd>
                                        <span class="mono">{ui.directory}</span>
                                    </dd>
                                    <dt>Version</dt>
                                    <dd>
                                        {ui.version ?? 'unknown'}, built for API {ui.api_version ?? '?'} ·{' '}
                                        {ui.api_version_matches ? '✓ matches the runtime' : "✕ not this runtime's API"}
                                    </dd>
                                    <dt>Digest</dt>
                                    <dd>
                                        <span class="mono">{ui.digest ?? 'none'}</span>
                                    </dd>
                                    {ui.problem && (
                                        <>
                                            <dt>Problem</dt>
                                            <dd>{ui.problem}</dd>
                                        </>
                                    )}
                                </dl>
                            ) : (
                                <p class="empty">
                                    The runtime reports no UI module (not reported); this page is served from somewhere
                                    it does not know.
                                </p>
                            )}
                        </section>

                        <section class="card wide">
                            <h2>
                                Paired browsers <span class="badge">{store.tokens.value?.items.length ?? 0}</span>
                                <span class="hint">
                                    {' '}
                                    · the same list as <code>jason ui tokens</code>; pair another with{' '}
                                    <code>jason ui open</code>
                                </span>
                            </h2>
                            <table class="tbl">
                                <thead>
                                    <tr>
                                        <th>Paired</th>
                                        <th>By</th>
                                        <th>Last used</th>
                                        <th />
                                    </tr>
                                </thead>
                                <tbody>
                                    {(store.tokens.value?.items ?? []).map((t) => {
                                        const mine = t.id === session.tokenId.value;
                                        return (
                                            <tr key={t.id}>
                                                <td>
                                                    {relative(new Date(t.issued_at), now)}
                                                    {mine && ' · this browser'}
                                                </td>
                                                <td>{t.issued_to}</td>
                                                <td>
                                                    {t.last_used_at ? relative(new Date(t.last_used_at), now) : 'never'}
                                                </td>
                                                <td class="r">
                                                    <button
                                                        class="btn"
                                                        type="button"
                                                        disabled={mine}
                                                        onClick={() => void store.revoke(api, t.id)}
                                                    >
                                                        Revoke
                                                    </button>
                                                </td>
                                            </tr>
                                        );
                                    })}
                                </tbody>
                            </table>
                        </section>
                    </div>
                )}
            </div>
        </div>
    );
}
