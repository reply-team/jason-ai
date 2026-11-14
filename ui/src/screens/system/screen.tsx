import { useEffect, useState } from 'react';
import { useApi } from '../../api/context';
import { Toolbar } from '../../layout/toolbar';
import { session } from '../../api/session';
import type { UiToken } from '../../api/types';
import { clock, relative, when } from '../../time/format';
import { followStanding, fullAccessStanding, takeBackStanding } from '../../platform/full-access';
import { can, cannotHere, currentDesktop, earlier, placeKind } from '../../platform/places';
import * as store from './store';
import { routeCounts } from './route-counts';
import { Notice } from '../../layout/notice';
import { t } from '../../platform/i18n';
import { modeWords } from '../send-modes/model';
import { sourceWords } from '../send-modes/agent-modes';

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

/**
 * The standing choice of Full access, in the desktop alone: what stands and its take-back, which asks the box again at
 * the next entry and starts new chats in Ask first. The page never gives the choice; only the desktop's own box does. A
 * desktop of an earlier version shows no card, and a tab draws it off, with the sentence of its place.
 */
function FullAccessCard() {
    const desktop = currentDesktop();
    const offered = desktop !== undefined && earlier(desktop).fullAccess !== undefined;
    const [refused, setRefused] = useState<string | null>(null);
    useEffect(() => (desktop !== undefined && offered ? followStanding(desktop) : undefined), [desktop, offered]);
    if (desktop === undefined || !offered) return null;
    const standing = fullAccessStanding.value;
    const takeBack = () =>
        void takeBackStanding(desktop)
            .then(setRefused)
            .catch((error: unknown) => setRefused(error instanceof Error ? error.message : String(error)));
    return (
        <section className="card">
            <h2>Full access</h2>
            {refused && <Notice danger>{refused}</Notice>}
            {!can('fullAccess.standing') ? (
                <>
                    <p>{cannotHere()}</p>
                    <div className="btn-row">
                        <button className="btn" type="button" disabled title={cannotHere()}>
                            Ask each time again
                        </button>
                    </div>
                </>
            ) : standing === null ? (
                <p>Full access asks you each time a chat enters it.</p>
            ) : (
                <>
                    <p>
                        {Number.isNaN(new Date(standing.givenAt).getTime())
                            ? 'Chats on this computer enter Full access without asking.'
                            : `Chats on this computer enter Full access without asking, since ${when(new Date(standing.givenAt), new Date(), zone)}.`}
                    </p>
                    {standing.newChats && <p>New chats start in Full access.</p>}
                    <div className="btn-row">
                        <button
                            className="btn"
                            type="button"
                            onClick={takeBack}
                            disabled={!can('fullAccess.takeBack')}
                            title={can('fullAccess.takeBack') ? undefined : cannotHere()}
                        >
                            Ask each time again
                        </button>
                    </div>
                </>
            )}
        </section>
    );
}

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
        <div className="main single">
            <div className="list" style={{ borderRight: 0 }}>
                <Toolbar
                    title="System"
                    refresh={{ busy: store.refreshing.value, onRefresh: () => void store.refresh(api) }}
                />
                {store.failure.value && <Notice danger>{store.failure.value}</Notice>}
                {info?.update?.available && (
                    <Notice>
                        Jason {info.update.version} is available.{' '}
                        {placeKind() === 'window' && currentDesktop() ? (
                            <>
                                The desktop updates itself and the jason it carries. Choose Check for Updates… in the
                                desktop&apos;s Jason menu, or in the menu of its icon in the tray.
                            </>
                        ) : (
                            <>
                                Update from a terminal with <code>jason update apply</code>, or ask your AI.
                            </>
                        )}
                        {info.update.release_notes_url && (
                            <>
                                {' '}
                                <a href={info.update.release_notes_url} target="_blank" rel="noreferrer">
                                    What changed
                                </a>
                            </>
                        )}
                    </Notice>
                )}
                {info && (
                    <div className="sys">
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

                        <FullAccessCard />

                        <section className="card">
                            <h2>
                                Dispatcher{' '}
                                <span className={`badge ${info.dispatcher.state === 'running' ? 'success' : ''}`}>
                                    {info.dispatcher.state}
                                </span>
                            </h2>
                            <dl className="facts">
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

                        <section className="card wide">
                            <h2>
                                Plugins <span className="badge">{info.plugins.active_count} active</span>
                            </h2>
                            {store.plugins.value.length > 0 ? (
                                <table className="tbl">
                                    <thead>
                                        <tr>
                                            <th>Plugin</th>
                                            <th>Kind</th>
                                            <th>Operations</th>
                                            <th>Status</th>
                                            <th className="r">Digest</th>
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
                                                <td className="r mono">{p.digest.slice(7, 19)}</td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            ) : (
                                <p className="empty">
                                    No plugin is installed; nothing reaches a provider until one is, and a route names
                                    it.
                                </p>
                            )}
                        </section>

                        <section className="card">
                            <h2>Routes</h2>
                            <dl className="facts">
                                <dt>Default provider</dt>
                                <dd>{store.routes.value?.global.default?.plugin_id ?? 'none'}</dd>
                                <dt>Overrides</dt>
                                <dd>{routeCounts(info.routes)}</dd>
                                <dt>Activated</dt>
                                <dd>
                                    {relative(new Date(info.routes.activated_at), now)} · snapshot{' '}
                                    <span className="mono">{info.routes.snapshot_id}</span>
                                </dd>
                            </dl>
                        </section>

                        {info.send_modes && (
                            <section className="card">
                                <h2>{t('modes.system.heading')}</h2>
                                <dl className="facts">
                                    <dt>{t('modes.system.cold')}</dt>
                                    <dd>
                                        {modeWords(info.send_modes.cold.mode).name} ·{' '}
                                        {sourceWords(info.send_modes.cold.source)}
                                    </dd>
                                    <dt>{t('modes.system.warm')}</dt>
                                    <dd>
                                        {modeWords(info.send_modes.warm.mode).name} ·{' '}
                                        {sourceWords(info.send_modes.warm.source)}
                                    </dd>
                                </dl>
                                <p className="hint">{t('modes.system.note')}</p>
                            </section>
                        )}

                        <section className="card">
                            <h2>This UI {ui && <span className="badge accent">{ui.source}</span>}</h2>
                            {ui ? (
                                <dl className="facts">
                                    <dt>Served from</dt>
                                    <dd>
                                        <span className="mono">{ui.directory}</span>
                                    </dd>
                                    <dt>Version</dt>
                                    <dd>
                                        {ui.version ?? 'unknown'}, built for API {ui.api_version ?? '?'} ·{' '}
                                        {ui.api_version_matches ? '✓ matches the runtime' : "✕ not this runtime's API"}
                                    </dd>
                                    <dt>Digest</dt>
                                    <dd>
                                        <span className="mono">{ui.digest ?? 'none'}</span>
                                    </dd>
                                    {ui.problem && (
                                        <>
                                            <dt>Problem</dt>
                                            <dd>{ui.problem}</dd>
                                        </>
                                    )}
                                </dl>
                            ) : (
                                <p className="empty">
                                    The runtime reports no UI module (not reported); this page is served from somewhere
                                    it does not know.
                                </p>
                            )}
                        </section>

                        <section className="card wide">
                            <h2>
                                Paired browsers <span className="badge">{store.tokens.value?.items.length ?? 0}</span>
                                <span className="hint">
                                    {' '}
                                    · the same list as <code>jason ui tokens</code>; pair another with{' '}
                                    <code>jason ui open</code>
                                </span>
                            </h2>
                            <table className="tbl">
                                <thead>
                                    <tr>
                                        <th>Paired</th>
                                        <th>By</th>
                                        <th>Last used</th>
                                        <th />
                                    </tr>
                                </thead>
                                <tbody>
                                    {byLastUse(store.tokens.value?.items ?? []).map((t) => {
                                        const mine = t.id === session.tokenId.value;
                                        const stale = now.getTime() - lastSeen(t) > staleAfterMs;
                                        return (
                                            <tr key={t.id} className={stale ? 'stale' : ''}>
                                                <td>
                                                    {relative(new Date(t.issued_at), now)}
                                                    {mine && ' · this browser'}
                                                </td>
                                                <td>{t.issued_to}</td>
                                                <td>
                                                    {t.last_used_at ? relative(new Date(t.last_used_at), now) : 'never'}
                                                    {stale && ' · not used for a month'}
                                                </td>
                                                <td className="r">
                                                    <button
                                                        className="btn"
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
                            {(store.tokens.value?.items ?? []).some((t) => t.id !== session.tokenId.value) && (
                                <div className="btn-row">
                                    <button
                                        className="btn"
                                        type="button"
                                        onClick={() => void store.revokeOthers(api, session.tokenId.value)}
                                    >
                                        Revoke all other browsers
                                    </button>
                                </div>
                            )}
                        </section>
                    </div>
                )}
            </div>
        </div>
    );
}

/** A browser unused for a month is most likely gone: a private window closed, its data cleared, another machine. */
const staleAfterMs = 30 * 24 * 60 * 60 * 1000;

/** When a browser was last seen: its last use, or its pairing if it was never used since. */
function lastSeen(token: UiToken): number {
    return new Date(token.last_used_at ?? token.issued_at).getTime();
}

/** The browsers seen most recently first, so the ones still in use lead and the forgotten ones sink. */
function byLastUse(tokens: UiToken[]): UiToken[] {
    return [...tokens].sort((a, b) => lastSeen(b) - lastSeen(a));
}
