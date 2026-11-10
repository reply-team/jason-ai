import type { DesktopHost, DesktopSetup } from '../platform/desktop';
import { act, argsOf, define } from './actions';
import { html, type Html } from './html';
import { signInSentence } from './live';
import { here, messageOf, setup, setupBusy, setupError } from './state';

// Connect: what the desktop found and set up, and the acts that set the rest up. The desktop does each act; the card
// says where things stand afterwards, from what `setup.status()` answers.

/** Keys and tokens being typed, held only until they are handed to the desktop. */
const typed = new Map<string, string>();

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

function take(key: string): string {
    const value = typed.get(key) ?? '';
    typed.delete(key);
    return value.trim();
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
    const key = take('reply');
    if (key) void doing('Connecting Reply with the key…', () => here().desktop.setup.connectReplyWithKey(key));
});

define('connect.providerKey', ([provider, remove]) => {
    if (typeof provider !== 'string') return;
    const key = remove === true ? '' : take(`provider:${provider}`);
    if (key || remove === true)
        void doing('Keeping the key…', () => here().desktop.setup.setProviderKey(provider, key));
});

define('connect.pluginToken', ([plugin]) => {
    if (typeof plugin !== 'string') return;
    const token = take(`plugin:${plugin}`);
    if (token) void doing('Connecting…', () => here().desktop.setup.setPluginToken(plugin, token));
});

const secret = (key: string, label: string): Html =>
    html`<input
        class="inp"
        type="password"
        autocomplete="off"
        aria-label="${label}"
        placeholder="${label}"
        data-input="connect.type"
        ${argsOf(key)}
    />`;

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
            ${state.accounts.map(
                (account) =>
                    html`<div class="setrow acct">
                        <div class="grow">
                            <b>${account.name}</b> <span class="meta">${account.channel} · ${account.address}</span>
                        </div>
                        <span class="pillc">${account.status}</span>
                    </div>`,
            )}
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
        ${reply(state.reply, disabled)}
        ${
            state.plugins.length
                ? html`<div class="card">
                      <div class="hd"><b>Notifications</b></div>
                      <div class="bd">
                          ${state.plugins.map(
                              (plugin) =>
                                  html`<div class="setrow">
                                      <div class="grow">
                                          <b>${plugin.label}</b> <span class="meta">${plugin.description}</span>
                                      </div>
                                      ${
                                          plugin.connected
                                              ? html`<span class="pillc green">Connected</span>`
                                              : html`${secret(`plugin:${plugin.id}`, plugin.tokenLabel)}
                                                    <button
                                                        class="btn sm"
                                                        type="button"
                                                        ${disabled}
                                                        ${act('connect.pluginToken', plugin.id)}
                                                    >
                                                        Connect
                                                    </button>`
                                      }
                                  </div>`,
                          )}
                      </div>
                  </div>`
                : null
        }
        <div class="meta versions">
            Jason Desktop ${state.bundleVersion} · runtime ${state.runtimeVersion ?? 'not running'}
        </div>
    </div>`;
}
