import { useEffect, useState } from 'react';
import { useApi } from '../../api/context';
import { Toolbar } from '../../layout/toolbar';
import { session } from '../../api/session';
import type { UiToken } from '../../api/types';
import { clock, relative } from '../../time/format';
import { can, currentDesktop, earlier, placeKind } from '../../platform/places';
import * as store from './store';
import { routeCounts } from './route-counts';
import { Notice } from '../../layout/notice';
import { t } from '../../platform/i18n';
import { modeWords } from '../send-modes/model';
import { sourceWords } from '../send-modes/agent-modes';
import { RuntimeCard } from './runtime-card';

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

import { ScrollBox } from '../../layout/scroll-box';

/**
 * The key of the desktop's update item as the desktop spells it, where the item can act; null where it cannot (a build
 * that is not installed, or one whose updates come through the package), in a tab, and from a desktop of an earlier
 * version, which has no such key.
 */
function useUpdateKey(): string | null {
    const [key, setKey] = useState<string | null>(null);
    useEffect(() => {
        const desktop = currentDesktop();
        if (desktop === undefined || !can('updates.key') || !earlier(desktop).updates) return;
        let live = true;
        desktop.updates
            .key()
            .then((words) => {
                if (live) setKey(words);
            })
            .catch(() => undefined);
        return () => {
            live = false;
        };
    }, []);
    return key;
}

/** The labels of the deb's update item, whose updates come with the system's and which has no Check for Updates. */
const packageItemLabel = 'Updates Come Through the Package';
const packageRestartLabel = 'Restart Jason and Its Runtime to Finish the Update to ';

/** Whether this page can read the desktop's update item: in its own window, from a desktop that has one. */
function itemReadable(): boolean {
    const desktop = currentDesktop();
    return desktop !== undefined && can('updates.item') && typeof earlier(desktop).updates?.item === 'function';
}

/**
 * Whether the desktop's update item is the deb's, read from the item it shows (`updates.item`): the system updates that
 * desktop and the jason it carries. Null until the item has answered, so neither sentence is drawn before it is known.
 * False in a tab, where the item cannot be read, where it cannot be read at all, and from a desktop of an earlier
 * version, which has no such item, so the sentence stays as it was.
 */
function usePackagedDesktop(): boolean | null {
    const [packaged, setPackaged] = useState<boolean | null>(() => (itemReadable() ? null : false));
    useEffect(() => {
        const desktop = currentDesktop();
        if (desktop === undefined || !can('updates.item') || typeof earlier(desktop).updates?.item !== 'function')
            return;
        let live = true;
        desktop.updates
            .item()
            .then((answer) => {
                const label = answer.item?.label ?? '';
                if (live) setPackaged(label === packageItemLabel || label.startsWith(packageRestartLabel));
            })
            .catch(() => {
                if (live) setPackaged(false);
            });
        return () => {
            live = false;
        };
    }, []);
    return packaged;
}

export function SystemScreen() {
    const api = useApi();
    const updateKey = useUpdateKey();
    const packaged = usePackagedDesktop();
    useEffect(() => {
        void store.load(api);
    }, [api]);
    const now = new Date();
    const info = store.info.value;
    const ui = info?.ui;
    const inWindow = placeKind() === 'window' && currentDesktop() !== undefined;
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
                        {inWindow && packaged === null ? null : inWindow && packaged ? (
                            <>
                                Your system updates Jason Desktop and the jason it carries: Software Updater offers it,
                                as <code>sudo apt update && sudo apt upgrade</code> does in a terminal.
                            </>
                        ) : inWindow ? (
                            <>
                                The desktop updates itself and the jason it carries. Choose Check for Updates… in the
                                desktop&apos;s Jason menu, or in the menu of its icon in the tray
                                {updateKey === null ? '.' : `, or press ${updateKey}.`}
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
                        <RuntimeCard info={info} now={now} />

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
                                <ScrollBox label="Plugins">
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
                                </ScrollBox>
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
                            <ScrollBox label="Paired browsers">
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
                                                        {t.last_used_at
                                                            ? relative(new Date(t.last_used_at), now)
                                                            : 'never'}
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
                            </ScrollBox>
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
