import { effect, signal } from '@preact/signals-react';
import type { DesktopHost, DesktopPlugin, DesktopReplyAccount, DesktopSetup } from '../platform/desktop';
import { act, argsOf, define } from './actions';
import { html, type Html } from './html';
import { signInSentence } from './live';
import { here, messageOf, setup, setupBusy, setupError } from './state';

// Connect: what the desktop found and set up, and the acts that set the rest up. The desktop does each act; the card
// says where things stand afterwards, from what `setup.status()` answers.

/** Keys and tokens being typed, held only until they are handed to the desktop. */
const typed = new Map<string, string>();

/** The plugin whose Disconnect waits on the person's confirmation; null when none does. */
const confirming = signal<string | null>(null);

// A confirmation is forgotten once the plugin it names is no longer configured, so configuring it again asks afresh.
effect(() => {
    const id = confirming.value;
    if (id !== null && !setup.value?.plugins.some((each) => each.id === id && each.configured)) confirming.value = null;
});

/**
 * Reply's web app, where an account's limits, signature, pauses and connection are managed. The desktop manages none of
 * them; the card links there instead.
 */
export const replyAccountsAddress = 'https://run.reply.io/';

/** The channels the Reply CLI lists accounts for, in the order the card shows them, with their headings. */
const channels: readonly (readonly [string, string])[] = [
    ['email', 'Email'],
    ['linkedin', 'LinkedIn'],
];

type Tone = 'green' | 'amber' | 'red' | 'gray';

/**
 * The state Reply reports for an account (`reply_status` in `reply account list --json`, see the Reply CLI's
 * `src/operations/accounts.ts`), in words, by channel. A word not here is shown as Reply wrote it, in a neutral pill.
 */
const states: Record<string, Record<string, readonly [string, Tone]>> = {
    email: {
        connected: ['Connected', 'green'],
        disconnected: ['Disconnected', 'red'],
        unknown: ['Unknown', 'gray'],
    },
    linkedin: {
        enabled: ['Enabled', 'green'],
        disabled: ['Disabled', 'gray'],
        cookieInvalid: ['Signed out', 'red'],
        dailyLimitReached: ['Daily limit reached', 'amber'],
    },
};

/** Text the card did not write, cut to forty characters so a long one cannot crowd the row; never inside one. */
const cap = (text: string): string => {
    const characters = [...text];
    return characters.length > 40 ? `${characters.slice(0, 40).join('')}…` : text;
};

function stateOf(account: DesktopReplyAccount): readonly [string, Tone] {
    if (!account.status) return ['State unknown', 'gray'];
    const known = Object.hasOwn(states, account.channel) ? states[account.channel]! : {};
    return Object.hasOwn(known, account.status) ? known[account.status]! : [cap(account.status), 'gray'];
}

/** The accounts by channel: the known channels in their order, then any other under its own name, as it first comes. */
function grouped(accounts: readonly DesktopReplyAccount[]): [string, DesktopReplyAccount[]][] {
    const others = [...new Set(accounts.map((each) => each.channel))].filter(
        (channel) => !channels.some(([known]) => known === channel),
    );
    return [...channels, ...others.map((channel) => [channel, cap(channel)] as const)]
        .map(([channel, heading]): [string, DesktopReplyAccount[]] => [
            heading,
            accounts.filter((each) => each.channel === channel),
        ])
        .filter(([, members]) => members.length > 0);
}

export async function reload(): Promise<void> {
    try {
        setup.value = await here().desktop.setup.status();
    } catch (error) {
        setupError.value = messageOf(error);
    }
}

async function doing(what: string, run: () => Promise<void>): Promise<void> {
    if (setupBusy.value !== null) return;
    setupBusy.value = what;
    setupError.value = null;
    try {
        await run();
    } catch (error) {
        setupError.value = messageOf(error);
    } finally {
        setupBusy.value = null;
    }
    await reload();
}

/** What is typed under `key`, trimmed. It stays typed until the desktop accepts the act, so a refusal is corrected. */
const typedOf = (key: string): string => (typed.get(key) ?? '').trim();

/** Forgets what was typed under `keys`, once the desktop has accepted what it was typed for. */
function forget(...keys: string[]): void {
    for (const key of keys) typed.delete(key);
}

define('connect.type', (args, element) => {
    if (typeof args[0] === 'string' && element instanceof HTMLInputElement) typed.set(args[0], element.value);
});

define('connect.install', ([program]) => {
    if (typeof program === 'string')
        void doing(`Installing ${program}…`, () => here().desktop.setup.installProgram(program));
});

define('connect.reply', () => void doing('Signing in to Reply…', () => here().desktop.setup.connectReply()));

define('connect.replyKey', () => {
    const key = typedOf('reply');
    if (key)
        void doing('Connecting Reply with the key…', async () => {
            await here().desktop.setup.connectReplyWithKey(key);
            forget('reply');
        });
});

define('connect.providerKey', ([provider, remove]) => {
    if (typeof provider !== 'string') return;
    const key = remove === true ? '' : typedOf(`provider:${provider}`);
    if (key || remove === true)
        void doing('Keeping the key…', async () => {
            await here().desktop.setup.setProviderKey(provider, key);
            forget(`provider:${provider}`);
        });
});

define('connect.plugin', ([id]) => {
    const plugin = setup.value?.plugins.find((each) => each.id === id);
    if (!plugin) return;
    const keys = ['token', 'target', 'server'].map((name) => `plugin:${plugin.id}:${name}`);
    const [token, target, typedServer] = keys.map(typedOf);
    const server = plugin.serverLabel ? typedServer : '';
    // A blank field is left out, which keeps what the file holds; removing a server is a button of its own.
    const values = { ...(token ? { token } : {}), ...(target ? { target } : {}), ...(server ? { server } : {}) };
    if (Object.keys(values).length === 0) return;
    void doing(`Configuring ${plugin.label}…`, async () => {
        await here().desktop.setup.configurePlugin(plugin.id, values);
        forget(...keys);
    });
});

define('connect.pluginServerClear', ([id]) => {
    const plugin = setup.value?.plugins.find((each) => each.id === id);
    if (plugin?.serverLabel)
        void doing(`Configuring ${plugin.label}…`, () =>
            here().desktop.setup.configurePlugin(plugin.id, { server: '' }),
        );
});

define('connect.disconnect', ([id]) => {
    if (typeof id === 'string') confirming.value = id;
});

define('connect.disconnectCancel', () => {
    confirming.value = null;
});

define('connect.disconnectConfirm', ([id]) => {
    const plugin = setup.value?.plugins.find((each) => each.id === id);
    confirming.value = null;
    if (plugin) void doing(`Disconnecting ${plugin.label}…`, () => here().desktop.setup.disconnectPlugin(plugin.id));
});

// Reload is the configure call with no values: it writes nothing, has the runtime read its plugins again, and reads
// them back. The reload is the installation's, so any of the plugins names it.
define('connect.reload', () => {
    const first = setup.value?.plugins[0];
    if (first) void doing('Reading the settings again…', () => here().desktop.setup.configurePlugin(first.id, {}));
});

const field = (key: string, label: string, kind: 'text' | 'password'): Html =>
    html`<input
        class="inp"
        type="${kind}"
        autocomplete="off"
        aria-label="${label}"
        placeholder="${label}"
        value="${typed.get(key) ?? ''}"
        data-input="connect.type"
        ${argsOf(key)}
    />`;

const secret = (key: string, label: string): Html => field(key, label, 'password');

function program(host: DesktopHost, busy: Html | null): Html {
    const sentence = signInSentence(host);
    return html`<div class="setrow">
        <div class="grow">
            <b>${host.label}</b>
            <span class="meta">${host.found ? (host.version ?? 'Found') : 'Not found'}</span>
            ${
                host.found
                    ? html`<div class="meta">
                          ${host.drivesChat ? 'Drives the chat' : 'Runs campaign work, not the chat yet'}
                      </div>`
                    : null
            }
            ${sentence ? html`<div class="meta sign-in">${sentence.split('`').map((part, at) => (at % 2 ? html`<code>${part}</code>` : html`${part}`))}</div>` : null}
        </div>
        ${
            !host.found && host.install
                ? html`<button class="btn sm" type="button" ${busy} ${act('connect.install', host.install)}>
                      Install
                  </button>`
                : null
        }
    </div>`;
}

function provider(host: DesktopHost, busy: Html | null): Html {
    return html`<div class="setrow">
        <div class="grow"><b>${host.label}</b> <span class="meta">${host.found ? 'Key set' : 'No key'}</span></div>
        ${
            host.found
                ? html`<button class="btn sm" type="button" ${busy} ${act('connect.providerKey', host.id, true)}>
                      Remove the key
                  </button>`
                : html`${secret(`provider:${host.id}`, `${host.label} key`)}
                      <button class="btn sm" type="button" ${busy} ${act('connect.providerKey', host.id)}>Keep</button>`
        }
    </div>`;
}

function reply(state: DesktopSetup['reply'], busy: Html | null): Html {
    const cli = state.cli.found
        ? html`<span class="meta">Reply CLI ${state.cli.version ?? ''}</span>`
        : html`<span class="meta">The Reply CLI was not found.</span> ${
                  state.cli.install
                      ? html`<button class="btn sm" type="button" ${busy} ${act('connect.install', state.cli.install)}>
                            Install
                        </button>`
                      : null
              }`;
    return html`<div class="card">
        <div class="hd"><b>Reply</b></div>
        <div class="bd">
            <div class="setrow"><div class="grow">${cli}</div></div>
            <div class="setrow">
                <div class="grow">
                    ${state.signedIn ? html`Signed in as <b>${state.user ?? ''}</b>` : 'Not signed in'}
                    <span class="meta">${state.wired ? 'Jason uses this sign-in' : 'Jason does not use it yet'}</span>
                </div>
                ${state.cli.found ? html`<button class="btn sm p" type="button" ${busy} ${act('connect.reply')}>Sign in</button>` : null}
            </div>
            ${
                state.cli.found
                    ? html`<div class="setrow">
                          <div class="grow meta">Or an API key, for an account the browser sign-in does not reach:</div>
                          ${secret('reply', 'Reply API key')}
                          <button class="btn sm" type="button" ${busy} ${act('connect.replyKey')}>Connect</button>
                      </div>`
                    : null
            }
            ${grouped(state.accounts).map(
                ([heading, members]) =>
                    html`<div class="acct-group">
                        <div class="setrow meta acct-hd">${heading}</div>
                        ${members.map((account) => {
                            const [words, tone] = stateOf(account);
                            return html`<div class="setrow acct">
                                <div class="grow">
                                    <b>${account.name}</b> <span class="meta">${account.address}</span>
                                </div>
                                <span class="pillc ${tone}">${words}</span>
                            </div>`;
                        })}
                    </div>`,
            )}
            <div class="setrow">
                <div class="grow meta">
                    Limits, signatures, pauses and an account's connection are set in Reply.
                    <a href="${replyAccountsAddress}" target="_blank" rel="noreferrer">Manage accounts in Reply</a>
                </div>
            </div>
        </div>
    </div>`;
}

function runtimeWords(plugin: DesktopPlugin): string {
    if (plugin.runtime === null) return 'The runtime has not read it.';
    if (!plugin.runtime.valid) return 'The runtime does not take its package or configuration as they stand.';
    const grants = plugin.runtime.grants === 1 ? '1 grant' : `${plugin.runtime.grants} grants`;
    return `The runtime reads it as valid, with ${grants}.`;
}

function plugin(each: DesktopPlugin, busy: Html | null, refused: boolean): Html {
    return html`<div class="setrow plugin" data-id="${each.id}">
        <div class="grow">
            <b>${each.label}</b> <span class="meta">${each.description}</span>
            <div class="meta">
                ${runtimeWords(each)}
                ${each.binding === false ? html` It has no binding yet: set its ${each.target.label.toLowerCase()}.` : null}
            </div>
            ${each.note ? html`<div class="meta">${each.note}</div>` : null}
        </div>
        ${
            each.configured
                ? html`<span class="pillc green">Configured</span>`
                : html`<span class="pillc gray">Not configured</span>`
        }
        ${
            refused
                ? null
                : html`${field(`plugin:${each.id}:target`, each.target.label, 'text')}
                      ${each.serverLabel ? field(`plugin:${each.id}:server`, each.serverLabel, 'text') : null}
                      ${each.tokenLabel ? secret(`plugin:${each.id}:token`, each.tokenLabel) : null}
                      ${
                          each.serverLabel
                              ? html`<button
                                    class="btn sm"
                                    type="button"
                                    ${busy}
                                    ${act('connect.pluginServerClear', each.id)}
                                >
                                    Use the public server
                                </button>`
                              : null
                      }
                      <button class="btn sm" type="button" ${busy} ${act('connect.plugin', each.id)}>Configure</button>
                      ${disconnect(each, busy)}`
        }
    </div>`;
}

/**
 * Disconnect, on a configured notification plugin only: the forwarder (the plugin with no binding) has none, since
 * without its receiver it would fail every journal line it is still granted. It asks once before the desktop acts.
 */
function disconnect(each: DesktopPlugin, busy: Html | null): Html | null {
    if (each.binding === null || !each.configured) return null;
    if (confirming.value !== each.id)
        return html`<button class="btn sm" type="button" ${busy} ${act('connect.disconnect', each.id)}>
            Disconnect
        </button>`;
    return html`<div class="note am" role="alertdialog" aria-label="Disconnect ${each.label}">
        Disconnect ${each.label}? Every value under its configuration in the settings file and its notification target
        are removed. Jason stops sending notifications there. The plugin stays installed.
        <button class="btn sm" type="button" ${busy} ${act('connect.disconnectConfirm', each.id)}>
            Disconnect ${each.label}
        </button>
        <button class="btn sm" type="button" ${act('connect.disconnectCancel')}>Cancel</button>
    </div>`;
}

function plugins(state: DesktopSetup, busy: Html | null): Html {
    const refused = state.settingsRefused !== null;
    return html`<div class="card">
        <div class="hd">
            <b>Plugins</b>
            <span class="meta">Where Jason tells you something waits, and where the journal goes.</span>
        </div>
        <div class="bd">
            ${
                refused
                    ? html`<div class="note am" role="alert">
                          ${state.settingsRefused} Edit it by hand, then press Reload.
                          <button class="btn sm" type="button" ${busy} ${act('connect.reload')}>Reload</button>
                      </div>`
                    : null
            }
            ${state.plugins.map((each) => plugin(each, busy, refused))}
        </div>
    </div>`;
}

export function connectView(): Html {
    const state = setup.value;
    const busy = setupBusy.value;
    const failure = setupError.value;
    const disabled = busy === null ? null : html`disabled`;
    const header = html`<div class="ph">
        <div>
            <h1>Connect</h1>
            <p>The AI that does the work, and the accounts it works through.</p>
        </div>
    </div>`;
    if (!state)
        return html`<div class="panel-body">
            ${header}${failure ? html`<div class="note am" role="alert">${failure}</div>` : html`<div class="empty">Reading what is set up…</div>`}
        </div>`;
    const programs = state.hosts.filter((each) => each.kind === 'program');
    const providers = state.hosts.filter((each) => each.kind === 'provider');
    return html`<div class="panel-body">
        ${header} ${busy ? html`<div class="note" role="status"><span class="spin"></span>${busy}</div>` : null}
        ${failure ? html`<div class="note am" role="alert">${failure}</div>` : null}
        <div class="card">
            <div class="hd"><b>AI</b></div>
            <div class="bd">${programs.map((each) => program(each, disabled))}</div>
        </div>
        ${
            providers.length
                ? html`<div class="card">
                      <div class="hd">
                          <b>Model keys</b>
                          <span class="meta">Run through Claude Code; kept in the system keychain.</span>
                      </div>
                      <div class="bd">${providers.map((each) => provider(each, disabled))}</div>
                  </div>`
                : null
        }
        ${reply(state.reply, disabled)} ${state.plugins.length ? plugins(state, disabled) : null}
        <div class="meta versions">
            Jason Desktop ${state.bundleVersion} · runtime ${state.runtimeVersion ?? 'not running'}
        </div>
        ${state.versionsDiffer === null ? null : html`<div class="meta versions-differ">${state.versionsDiffer}</div>`}
    </div>`;
}
