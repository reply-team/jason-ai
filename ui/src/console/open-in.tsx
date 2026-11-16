import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { can } from '../platform/places';
import type {
    DesktopOpenInAnswer,
    DesktopOpenInContinue,
    DesktopOpenInJason,
    DesktopOpenInPlace,
    DesktopRefusal,
    JasonDesktop,
} from '../platform/desktop';
import { t } from '../platform/i18n';
import { mode } from '../platform/mode';
import { shownAgent } from './agent-place';
import { activePanel, chats, currentChat } from './state';

// "Open in": the split button of the console's top bar, in Live and in Demo alike. The main part opens the folder in the
// default place, the arrow lists every place this computer has. The page names a place and, where a campaign's chat is
// in front, that campaign; it never holds or sends a path, and the folder's name it shows comes from the desktop. Where
// a chat is in front and the desktop offers it, the menu's last group continues that chat in a terminal: the page sends
// the chat's id alone, and the host and the session are the desktop's to read from its own record. Where the desktop
// offers it, in the person's own installation and never in the demo, the group above that one is "Change Jason itself",
// which opens the person's own AI on Jason's roles and plugins: the page sends nothing, and names no host, which the
// person's preference chooses as the entry is chosen.

const editorIds: readonly DesktopOpenInPlace[] = ['vscode', 'cursor', 'antigravity', 'windsurf', 'zed'];

/** The places that are a command line: the main part draws a prompt's glyph before their name. */
const terminalPlaces: readonly DesktopOpenInPlace[] = ['claude', 'codex', 'terminal'];

/** The glyph drawn before the main place's name: a prompt for a command line, a pair of brackets for an editor. */
const glyphs = {
    prompt: 'M4 7l5 5-5 5M12 18h8',
    editor: 'M8 6l-6 6 6 6M16 6l6 6-6 6',
    // The menu's own kinds, on the same 24-unit grid and stroke: a terminal window, a folder, a browser's globe, two
    // sheets for a copy, a pair of sliders for changing Jason itself and a bent arrow for continuing a chat.
    terminal: 'M3 5h18v14H3zM7 10l3 2-3 2M12 15h4',
    folder: 'M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z',
    browser: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM3 12h18M12 3c3 3 3 15 0 18M12 3c-3 3-3 15 0 18',
    copy: 'M9 9h10v10H9zM5 15V5h10',
    change: 'M4 7h9M17 7h3M4 17h3M11 17h9M15 5v4M9 15v4',
    again: 'M4 12h12M11 6l6 6-6 6M20 5v14',
} as const;

/** The glyph a menu item draws for its place: one of ours for its kind, never a product's mark. */
function kindOf(place: DesktopOpenInPlace): keyof typeof glyphs {
    switch (place) {
        case 'claude':
        case 'codex':
            return 'prompt';
        case 'terminal':
            return 'terminal';
        case 'files':
            return 'folder';
        case 'browser':
            return 'browser';
        case 'copy':
            return 'copy';
        default:
            return 'editor';
    }
}

function glyphOf(place: DesktopOpenInPlace): keyof typeof glyphs | undefined {
    if (terminalPlaces.includes(place)) return 'prompt';
    return editorIds.includes(place) ? 'editor' : undefined;
}

/** A place's glyph, before its name in the main part. */
function PlaceGlyph({ glyph, size = 16 }: { glyph: keyof typeof glyphs; size?: number }) {
    return (
        <svg
            className="oi-glyph"
            data-glyph={glyph}
            width={size}
            height={size}
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
        >
            <path d={glyphs[glyph]} />
        </svg>
    );
}

/** The arrow's chevron. */
function Chevron() {
    return (
        <svg
            width="10"
            height="10"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2.5"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
        >
            <path d="M6 9l6 6 6-6" />
        </svg>
    );
}

const refused = (answer: unknown): answer is DesktopRefusal =>
    typeof answer === 'object' && answer !== null && 'refused' in answer;

/** The chat whose panel is the one in front, or undefined: with Agents, Needs you or Activity in front there is none. */
function chatInFront(): string | undefined {
    const id = currentChat.value;
    return id === null || activePanel.value !== 'chat:' + id ? undefined : id;
}

/**
 * The campaign in front, or undefined: the agent shown, on any of its tabs, or the campaign of the chat in front; with
 * Agents, Needs you or Activity in front the folder is the harness root.
 */
function campaignOfChat(): string | undefined {
    const agent = shownAgent.value;
    if (agent !== null) return agent.id;
    const id = chatInFront();
    return id === undefined ? undefined : (chats.value[id]?.campaignId ?? undefined);
}

/** The words for a place: the desktop's product name where it sends one, else the page's own word for the place. */
function nameOf(place: { id: DesktopOpenInPlace; label: string }): string {
    if (place.label !== '') return place.label;
    return place.id === 'terminal'
        ? t('openIn.place.terminal')
        : place.id === 'files'
          ? t('openIn.place.files')
          : place.id === 'browser'
            ? t('openIn.place.browser')
            : t('openIn.copy');
}

/**
 * What `jason` typed in an editor's terminal finds, by the PATH the desktop starts an editor with: never more than the
 * desktop could tell. An editor already running keeps the PATH it started with, so the sentences speak of your PATH and
 * never of the editor.
 */
function jasonSaid(jason: DesktopOpenInJason): string {
    const found = t(`openIn.jason.${jason.found}`);
    if (jason.found === 'this' || jason.way === undefined) return found;
    if (jason.way === 'type') return found + ' ' + t('openIn.jason.type');
    const way = jason.way;
    return jason.act === undefined ? found : found + ' ' + t(`openIn.jason.${way}`, { act: jason.act });
}

export function OpenIn({ desktop }: { desktop: JasonDesktop }) {
    const campaignId = campaignOfChat();
    const chatId = chatInFront();
    // Whether the page sees a turn of the chat in front run: the entry is disabled meanwhile, and the places are read
    // again when it ends, since a turn may have named the session the entry continues.
    const turnRuns = chatId !== undefined && chats.value[chatId]?.running === true;
    const [answer, setAnswer] = useState<DesktopOpenInAnswer | null>(null);
    // The chat the answer was read for: its entry is drawn only while that chat is still the one in front.
    const [answerChat, setAnswerChat] = useState<string | undefined>(undefined);
    const [problem, setProblem] = useState<string | null>(null);
    const [menu, setMenu] = useState(false);
    const [toast, setToast] = useState<string | null>(null);
    const root = useRef<HTMLDivElement>(null);
    // Each read is numbered; an answer that lands after a later read was asked for is dropped, so a slow read for one chat
    // never overwrites the answer for the chat in front now.
    const reads = useRef(0);
    // True while a Change Jason itself the person chose is being opened: a second choice meanwhile opens nothing.
    const changing = useRef(false);

    // Answers the places read, or null where the read was refused, failed or was overtaken by a later one.
    const read = useCallback(async (): Promise<DesktopOpenInAnswer | null> => {
        if (!can('openIn.targets')) return null;
        const asked = ++reads.current;
        try {
            const kept = await desktop.openIn.targets(campaignId, chatId);
            if (asked !== reads.current) return null;
            if (refused(kept)) {
                setAnswer(null);
                setProblem(kept.refused);
                return null;
            }
            setAnswer(kept);
            setAnswerChat(chatId);
            setProblem(null);
            return kept;
        } catch (error) {
            if (asked !== reads.current) return null;
            setAnswer(null);
            setProblem(error instanceof Error ? error.message : String(error));
            return null;
        }
    }, [desktop, campaignId, chatId]);
    // The entry is drawn only with a chat in front, and only from the answer read for that chat.
    const continuing =
        chatId !== undefined && answerChat === chatId && answer?.continue !== undefined ? answer.continue : undefined;

    // A turn of the chat in front that starts or ends asks for the places again.
    useEffect(() => {
        void read();
    }, [read, turnRuns]);

    // A refusal is not for good: the window coming back to the front reads the places again.
    useEffect(() => {
        const again = () => void read();
        window.addEventListener('focus', again);
        return () => window.removeEventListener('focus', again);
    }, [read]);

    useEffect(() => {
        // The menu is drawn only with places or a sentence to show, and only a drawn menu listens for its closing.
        if (!menu || (answer === null && problem === null)) return;
        const away = (event: MouseEvent) => {
            if (root.current && !root.current.contains(event.target as Node)) setMenu(false);
        };
        const escape = (event: KeyboardEvent) => {
            if (event.key === 'Escape') setMenu(false);
        };
        document.addEventListener('mousedown', away);
        document.addEventListener('keydown', escape);
        return () => {
            document.removeEventListener('mousedown', away);
            document.removeEventListener('keydown', escape);
        };
    }, [menu, answer, problem]);

    const open = async (place: DesktopOpenInPlace) => {
        setMenu(false);
        if (!can('openIn.open')) return;
        const said = await desktop.openIn.open(place, campaignId).then(
            (result) => (result === null ? null : result.refused),
            (error: unknown) => (error instanceof Error ? error.message : String(error)),
        );
        // The place used last is the next default; a refusal stays on screen after the read, which also says which jason the
        // PATH finds now.
        const fresh = await read();
        if (said !== null) {
            setProblem(said);
            return;
        }
        const chosen = answer?.places.find((each) => each.id === place);
        const folder = answer?.folder ?? '';
        if (place === 'copy') setToast(t('openIn.copied', { folder }));
        else if (place === 'browser') setToast(t('openIn.browserOpened'));
        else {
            // Jason in a terminal works with this window's data: `jason session open` puts this jason first on its PATH. An
            // editor gets the person's own environment, so what it says is which jason that PATH finds, as the desktop tells
            // it, and nothing where it cannot; in the demo it is said to be the other way about. The file manager runs
            // nothing and says nothing of data.
            const terminal = place === 'claude' || place === 'codex' || place === 'terminal';
            const jason = (fresh ?? answer)?.jason;
            const said = terminal
                ? t('openIn.sameData')
                : place === 'files'
                  ? null
                  : mode.value === 'demo'
                    ? t('openIn.editorDemo')
                    : jason === undefined
                      ? null
                      : jasonSaid(jason);
            setToast(
                t('openIn.opened', {
                    folder,
                    place: chosen === undefined ? '' : nameOf(chosen),
                }) + (said === null ? '' : ' ' + said),
            );
        }
    };

    const continueChat = async (offered: DesktopOpenInContinue) => {
        setMenu(false);
        if (chatId === undefined || !can('openIn.continueChat')) return;
        const said = await desktop.openIn.continueChat(chatId).then(
            (result) => (result === null ? null : result.refused),
            (error: unknown) => (error instanceof Error ? error.message : String(error)),
        );
        await read();
        if (said !== null) {
            setProblem(said);
            return;
        }
        setToast(t('openIn.continued', { place: offered.label }) + ' ' + t('openIn.sameData'));
    };

    const changeJason = async () => {
        setMenu(false);
        if (changing.current || !can('openIn.changeJason')) return;
        changing.current = true;
        const said = await desktop.openIn
            .changeJason()
            .then(
                (result) => (result === null ? null : result.refused),
                (error: unknown) => (error instanceof Error ? error.message : String(error)),
            )
            .finally(() => {
                changing.current = false;
            });
        await read();
        if (said !== null) {
            setProblem(said);
            return;
        }
        setToast(t('openIn.changeJasonOpened'));
    };

    useEffect(() => {
        if (toast === null) return;
        const gone = setTimeout(() => setToast(null), 6000);
        return () => clearTimeout(gone);
    }, [toast]);

    // A sentence cut short in a narrow strip is still whole for the keyboard: each part is described by it, and the
    // menu says it first.
    const problemId = useId();
    const menuId = useId();
    const described = problem !== null ? problemId : undefined;
    const places = answer?.places ?? [];
    const main = places.find((each) => each.id === answer?.default);
    const agents = places.filter((each) => each.id === 'claude' || each.id === 'codex');
    const editors = places.filter((each) => (editorIds as readonly string[]).includes(each.id));
    const rest = places.filter((each) => !agents.includes(each) && !editors.includes(each));
    // The main part's words; a narrow window draws its glyph alone, and the words stay its name.
    const mainWords = t('openIn.main', { place: main === undefined ? '' : nameOf(main) });
    // Each item is an icon, then its words. The icon is one element of its own, so an image the desktop reads for a place
    // can take a glyph's place without moving anything. An agent's full sentence is the item's description; the line drawn
    // under its name is the short form of it.
    const item = (
        place: { id: DesktopOpenInPlace; label: string },
        subtitle?: string,
        text?: string,
        description?: string,
    ) => {
        // The name can be cut by its ellipsis, so every item carries it whole as its title; an agent's title adds its
        // full sentence after the name.
        const words = text ?? nameOf(place);
        return (
            <button
                key={place.id}
                type="button"
                role="menuitem"
                className={place.id === answer?.default ? 'on' : undefined}
                aria-labelledby={description === undefined ? undefined : `${menuId}-${place.id}-name`}
                aria-describedby={description === undefined ? undefined : `${menuId}-${place.id}-about`}
                title={description === undefined ? words : `${words}: ${description}`}
                onClick={() => void open(place.id)}
            >
                <span className="oi-icon">
                    <PlaceGlyph glyph={kindOf(place.id)} />
                </span>
                <span className="oi-text">
                    <span className="oi-name" id={`${menuId}-${place.id}-name`}>
                        {words}
                    </span>
                    {subtitle !== undefined && <small>{subtitle}</small>}
                    {description !== undefined && (
                        <span className="oi-about" id={`${menuId}-${place.id}-about`}>
                            {description}
                        </span>
                    )}
                </span>
            </button>
        );
    };
    const rule = <div className="open-in-rule" role="separator" />;
    return (
        <div className="open-in" ref={root}>
            {problem !== null && (
                <span className="open-in-problem" id={problemId} role="alert" title={problem}>
                    {problem}
                </span>
            )}
            <div className="open-in-split" role="group" aria-label={t('openIn.group')}>
                <button
                    type="button"
                    className="open-in-main"
                    disabled={main === undefined}
                    aria-label={mainWords}
                    aria-describedby={described}
                    title={
                        main === undefined
                            ? undefined
                            : t('openIn.mainTitle', { folder: answer?.folder ?? '', place: nameOf(main) })
                    }
                    onClick={() => main !== undefined && void open(main.id)}
                >
                    {main !== undefined && glyphOf(main.id) !== undefined && <PlaceGlyph glyph={glyphOf(main.id)!} />}
                    <span className="oi-l">{mainWords}</span>
                </button>
                <button
                    type="button"
                    className="open-in-arrow"
                    aria-label={t('openIn.choose')}
                    aria-describedby={described}
                    title={t('openIn.title')}
                    aria-haspopup="menu"
                    aria-expanded={menu}
                    onClick={() => {
                        setMenu(!menu);
                        if (!menu) void read();
                    }}
                >
                    <Chevron />
                </button>
            </div>
            {toast !== null && (
                <span className="open-in-toast" role="status">
                    {toast}
                </span>
            )}
            {menu && (answer !== null || problem !== null) && (
                <div className="open-in-menu" role="menu" aria-label={t('openIn.title')}>
                    {problem !== null && <div className="open-in-note open-in-said">{problem}</div>}
                    {agents.length > 0 && <div className="open-in-header">{t('openIn.agents')}</div>}
                    {agents.map((place) => item(place, t('openIn.session'), undefined, t('openIn.sessionFull')))}
                    {agents.length > 0 && editors.length > 0 && rule}
                    {editors.length > 0 && <div className="open-in-header">{t('openIn.editors')}</div>}
                    {editors.map((place) => item(place))}
                    {editors.length > 0 && answer?.jason !== undefined && answer.jason.found !== 'this' && (
                        <div className="open-in-note open-in-jason" title={jasonSaid(answer.jason)}>
                            {t(`openIn.jasonLine.${answer.jason.found}`)}
                        </div>
                    )}
                    {agents.length + editors.length > 0 && rest.length > 0 && rule}
                    {rest.map((place) =>
                        item(
                            place,
                            place.id === 'copy'
                                ? answer?.folder
                                : place.id === 'browser'
                                  ? t('openIn.browser')
                                  : undefined,
                            place.id === 'terminal'
                                ? t('openIn.terminal')
                                : place.id === 'copy'
                                  ? t('openIn.copy')
                                  : undefined,
                        ),
                    )}
                    {mode.value === 'demo' && <div className="open-in-note">{t('openIn.demoNote')}</div>}
                    {answer?.changeJason === true && <ChangeJasonEntry onChoose={() => void changeJason()} />}
                    {continuing !== undefined && (
                        <ContinueEntry
                            offered={continuing}
                            running={continuing.running || turnRuns}
                            onChoose={(offered) => void continueChat(offered)}
                        />
                    )}
                </div>
            )}
        </div>
    );
}

/**
 * The group above the last: "Change Jason itself", with the line that says what it opens. It is never the default, and it
 * names no host, which is the person's preference's to choose.
 */
function ChangeJasonEntry({ onChoose }: { onChoose: () => void }) {
    return (
        <div className="open-in-change" role="group" aria-label={t('openIn.changeJason')}>
            <button type="button" role="menuitem" onClick={onChoose}>
                <span className="oi-icon">
                    <PlaceGlyph glyph="change" />
                </span>
                <span className="oi-text">
                    <span className="oi-name">{t('openIn.changeJason')}</span>
                    <small>{t('openIn.changeJasonNote')}</small>
                </span>
            </button>
        </div>
    );
}

/**
 * The menu's last group: the entry that continues the chat in front in a terminal, with the sentence that says the window's
 * permission cards do not follow it there; while a turn of the chat runs it is disabled, and says so instead.
 */
function ContinueEntry({
    offered,
    running,
    onChoose,
}: {
    offered: DesktopOpenInContinue;
    running: boolean;
    onChoose: (offered: DesktopOpenInContinue) => void;
}) {
    return (
        <div className="open-in-continue" role="group" aria-label={t('openIn.continue', { place: offered.label })}>
            <button type="button" role="menuitem" disabled={running} onClick={() => onChoose(offered)}>
                <span className="oi-icon">
                    <PlaceGlyph glyph="again" />
                </span>
                <span className="oi-text">
                    <span className="oi-name">{t('openIn.continue', { place: offered.label })}</span>
                    <small>
                        {running ? t('openIn.continueRunning') : t('openIn.continueNote', { place: offered.label })}
                    </small>
                </span>
            </button>
        </div>
    );
}
