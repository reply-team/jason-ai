import { can, cannotHere } from '../platform/places';
import { signal } from '@preact/signals-react';
import { useEffect, useRef, useState } from 'react';
import type { Contact } from '../api/types';
import { t } from '../platform/i18n';
import { registry } from '../platform/slots';
import { parseRoute } from '../router';
import { displayName, searchPeople } from '../pickers/search';
import * as campaigns from './campaigns';
import { openConversation } from './chat';
import {
    hintFor,
    isShortcut,
    moved,
    peopleLimit,
    resultLimit,
    search,
    tokens,
    typingPause,
    type Entry,
    type Kind,
} from './palette-model';
import { chats, conversations, feedId, feedUnseen, here } from './state';

// The search palette: one box over the console that finds a page, a setting, an action, an agent or a chat the console
// already holds, and, once something is typed, the people the runtime finds by it. Page only: it reads nothing the
// console did not already read but `contact.list`, and only while a person types in the box.

/** Whether the palette is open. */
const opened = signal(false);

/**
 * The system the window is on, as the desktop reports it; read by the sidebar, which paints before the console is bound,
 * so it is kept here by the palette's own mount and the row says Ctrl K until then.
 */
const platform = signal<string>('');

/** Where the focus was when the palette opened. */
let before: Element | null = null;

/** Closes the palette; Escape and a click beside the box give the focus back, choosing an entry does not. */
function shut(restore: boolean): void {
    opened.value = false;
    if (restore && before instanceof HTMLElement) before.focus({ preventScroll: true });
}

export function openPalette(): void {
    opened.value = true;
}

/** The key's name on this window's system, from the platform the desktop reports: the Command sign on macOS. */
export function paletteHint(): string {
    return t(hintFor(platform.value) === 'mac' ? 'palette.hint.mac' : 'palette.hint.other');
}

/** The key's name for assistive technology, by the same system. */
export function paletteShortcut(): string {
    return hintFor(platform.value) === 'mac' ? 'Meta+K' : 'Control+K';
}

const kindWord = (kind: Kind): string => t(`palette.kind.${kind}`);

/** A screen the browser shell registers, by the id its entry has: its address, or null when this build has none. */
function addressOf(id: string): string | null {
    return (
        registry
            .list('screen')
            .find((each) => each.id === id)
            ?.href() ?? null
    );
}

/** Where each entry the console holds goes, by its key; the rows say nothing but the entry's. */
type Going = Readonly<Record<string, () => void>>;

interface Built {
    entries: Entry[];
    going: Going;
}

/** Pages: what the sidebar and More reach, each opened as its sidebar row or More entry opens it. */
function pages(): Built {
    const entries: Entry[] = [];
    const going: Record<string, () => void> = {};
    const add = (key: string, kind: Kind, label: string, words: string, go: () => void, off?: string) => {
        entries.push({ key, kind, label, words, ...(off === undefined ? {} : { off }) });
        going[key] = go;
    };
    const feed = feedId.value;
    if (feed !== null)
        add('page:feed', 'page', t('palette.page.feed'), '', () => {
            void openConversation(feed).then((held) => {
                if (held === null) return;
                feedUnseen.value = false;
                here().dock.showChat(held);
            });
        });
    add('page:dashboard', 'page', t('dash.title'), '', () => here().dock.show('dashboard'));
    add('page:needs', 'page', t('palette.page.needs'), t('palette.words.needs'), () => here().dock.show('needs'));
    const screen = (id: string, kind: Kind, label: string, words: string) => {
        const href = addressOf(id);
        if (href !== null) add(`${kind}:${id}`, kind, label, words, () => here().dock.page(parseRoute(href)));
    };
    screen('people', 'page', t('nav.people'), t('palette.words.people'));
    add('page:campaigns', 'page', t('nav.campaigns'), t('palette.words.campaigns'), () =>
        here().dock.show('campaigns'),
    );
    screen('accounts', 'page', t('nav.accounts'), '');
    screen('performance', 'page', t('nav.performance'), '');
    add('page:activity', 'page', t('palette.page.activity'), t('palette.words.activity'), () =>
        here().dock.show('activity'),
    );
    screen('workitems', 'page', t('nav.workitems'), t('palette.words.workitems'));
    add('page:connect', 'page', t('palette.page.connect'), t('palette.words.connect'), () =>
        here().dock.show('connect'),
    );
    screen('system', 'page', t('nav.system'), t('palette.words.system'));
    screen('settings', 'settings', t('nav.settings'), '');
    // A place that cannot make a chat lists the ways to one, off, with the sentence.
    const off = can('chat.create') ? undefined : cannotHere();
    add('action:agent', 'action', t('palette.action.agent'), '', () => here().dock.show('start'), off);
    add('action:chat', 'action', t('palette.action.chat'), '', () => here().dock.chat(), off);
    return { entries, going };
}

/**
 * Agents and chats the console already holds: each conversation as a Recent row opens it, found by its title and, for a
 * campaign's, the campaign's name; and each agent of the Agents panel that has no conversation yet, opened as that
 * panel's row opens it.
 */
function held(): Built {
    const entries: Entry[] = [];
    const going: Record<string, () => void> = {};
    const named = campaigns.campaigns.value;
    const talked = new Set<string>();
    for (const conversation of conversations.value) {
        if (conversation.main === true) continue;
        const campaign = named.find((each) => each.id === conversation.campaignId);
        if (conversation.campaignId !== null) talked.add(conversation.campaignId);
        const key = `chat:${conversation.id}`;
        entries.push({
            key,
            kind: conversation.campaignId === null ? 'chat' : 'agent',
            label: chats.value[conversation.id]?.title ?? conversation.title,
            words: campaign?.name ?? '',
        });
        going[key] = () => {
            void openConversation(conversation.id).then((chat) => {
                if (chat !== null) here().dock.showChat(chat);
            });
        };
    }
    for (const campaign of named) {
        if (talked.has(campaign.id)) continue;
        const key = `agent:${campaign.id}`;
        entries.push({ key, kind: 'agent', label: campaign.name, words: '' });
        going[key] = () => here().dock.page(parseRoute(`#/campaigns/${encodeURIComponent(campaign.id)}`));
    }
    return { entries, going };
}

/** "Name, Company" where both are known; the name alone, else. */
function personLabel(contact: Contact): string {
    return [displayName(contact), contact.company].filter((part) => part).join(', ');
}

/**
 * The people the runtime finds by the typed text, asked only once something is typed and after a pause in the typing; an
 * answer to a text that is no longer the one typed is dropped.
 */
function usePeople(text: string, on: boolean): readonly Contact[] {
    const [found, setFound] = useState<{ asked: string; people: readonly Contact[] }>({ asked: '', people: [] });
    const typed = tokens(text).join(' ');
    // What was found belongs to the opening it was found in.
    useEffect(() => {
        if (!on) setFound({ asked: '', people: [] });
    }, [on]);
    useEffect(() => {
        if (!on || typed === '') return;
        let current = true;
        const pause = setTimeout(() => {
            // The text as typed, not as compared: the runtime's own filter decides what it holds.
            void searchPeople(here().api, text.trim()).then(
                (people) => {
                    if (current) setFound({ asked: typed, people: people.slice(0, peopleLimit) });
                },
                () => {
                    if (current) setFound({ asked: typed, people: [] });
                },
            );
        }, typingPause);
        return () => {
            current = false;
            clearTimeout(pause);
        };
    }, [text, typed, on]);
    return typed !== '' && found.asked === typed ? found.people : [];
}

export function Palette({ system }: { system: string }) {
    const open = opened.value;
    const [text, setText] = useState('');
    const [at, setAt] = useState(0);
    const input = useRef<HTMLInputElement>(null);

    useEffect(() => {
        platform.value = system;
    }, [system]);

    useEffect(() => {
        const onKey = (event: KeyboardEvent) => {
            if (isShortcut(event)) {
                event.preventDefault();
                if (!opened.value) openPalette();
                return;
            }
            if (event.key === 'Escape' && opened.value) {
                event.preventDefault();
                shut(true);
            }
        };
        window.addEventListener('keydown', onKey);
        return () => {
            window.removeEventListener('keydown', onKey);
            // A console that goes takes its open palette with it.
            opened.value = false;
        };
    }, []);

    // Each opening starts empty and in the box.
    useEffect(() => {
        if (!open) return;
        before = document.activeElement;
        setText('');
        setAt(0);
        input.current?.focus();
    }, [open]);

    const people = usePeople(text, open);
    const pagesBuilt = open ? pages() : null;
    const heldBuilt = open ? held() : null;
    const local =
        pagesBuilt === null || heldBuilt === null
            ? []
            : search([...heldBuilt.entries, ...pagesBuilt.entries], text, kindWord, resultLimit);
    const found: Entry[] = people.map((contact) => ({
        key: `person:${contact.id}`,
        kind: 'person',
        label: personLabel(contact),
        words: '',
    }));
    const shown = [...local, ...found].slice(0, resultLimit);

    if (!open || pagesBuilt === null || heldBuilt === null) return null;

    const chosen = Math.min(at, Math.max(shown.length - 1, 0));
    const go = (entry: Entry) => {
        if (entry.off !== undefined) return;
        shut(false);
        const contact = people.find((each) => `person:${each.id}` === entry.key);
        if (contact !== undefined) {
            here().dock.page(parseRoute(`#/people/${encodeURIComponent(contact.id)}`));
            return;
        }
        (pagesBuilt.going[entry.key] ?? heldBuilt.going[entry.key])?.();
    };
    const onKey = (event: React.KeyboardEvent) => {
        if (event.nativeEvent.isComposing) return;
        // The box holds one control, so Tab and Shift+Tab stay where they are: the dialog is modal.
        if (event.key === 'Tab') event.preventDefault();
        else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            setAt(moved(chosen, event.key === 'ArrowDown' ? 1 : -1, shown.length));
        } else if (event.key === 'Enter') {
            event.preventDefault();
            const entry = shown[chosen];
            if (entry !== undefined) go(entry);
        }
    };

    return (
        <div className="palette-away" onMouseDown={() => shut(true)}>
            <div
                className="palette"
                role="dialog"
                aria-modal="true"
                aria-label={t('palette.search')}
                onMouseDown={(event) => event.stopPropagation()}
                onKeyDown={onKey}
            >
                <input
                    ref={input}
                    className="palette-input"
                    type="text"
                    role="combobox"
                    aria-expanded="true"
                    aria-controls="palette-results"
                    aria-activedescendant={shown[chosen] === undefined ? undefined : `palette-${chosen}`}
                    aria-label={t('palette.placeholder')}
                    placeholder={t('palette.placeholder')}
                    autoComplete="off"
                    spellCheck={false}
                    value={text}
                    onChange={(event) => {
                        setText(event.target.value);
                        setAt(0);
                    }}
                />
                {shown.length === 0 ? (
                    <div className="palette-none" role="status">
                        {t('palette.none')}
                    </div>
                ) : (
                    <ul id="palette-results" className="palette-list" role="listbox" aria-label={t('palette.results')}>
                        {shown.map((entry, index) => (
                            <li
                                key={entry.key}
                                id={`palette-${index}`}
                                role="option"
                                aria-selected={index === chosen}
                                aria-disabled={entry.off === undefined ? undefined : true}
                                className={index === chosen ? 'on' : undefined}
                                onMouseMove={() => index !== chosen && setAt(index)}
                                onClick={() => go(entry)}
                            >
                                <span className="palette-label">{entry.label}</span>
                                {entry.off !== undefined && <span className="palette-off">{entry.off}</span>}
                                <span className="palette-kind">{kindWord(entry.kind)}</span>
                            </li>
                        ))}
                    </ul>
                )}
            </div>
        </div>
    );
}
