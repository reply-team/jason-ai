import { useCallback, useEffect, useRef, useState } from 'react';
import { can } from '../platform/places';
import type {
    DesktopOpenInAnswer,
    DesktopOpenInContinue,
    DesktopOpenInPlace,
    DesktopRefusal,
    JasonDesktop,
} from '../platform/desktop';
import { t } from '../platform/i18n';
import { mode } from '../platform/mode';
import { activePanel, chats, currentChat } from './state';

// "Open in": the split button of the console's top bar, in Live and in Demo alike. The main part opens the folder in the
// default place, the arrow lists every place this computer has. The page names a place and, where a campaign's chat is
// in front, that campaign; it never holds or sends a path, and the folder's name it shows comes from the desktop. Where
// a chat is in front and the desktop offers it, the menu's last group continues that chat in a terminal: the page sends
// the chat's id alone, and the host and the session are the desktop's to read from its own record. Where the desktop
// offers it, in the person's own installation and never in the demo, the group above that one is "Change Jason itself",
// which opens the person's own AI on Jason's roles and plugins: the page sends nothing, and names no host, which the
// person's preference chooses as the entry is chosen.

const editorIds = ['vscode', 'cursor', 'antigravity', 'windsurf', 'zed'];

const refused = (answer: unknown): answer is DesktopRefusal =>
    typeof answer === 'object' && answer !== null && 'refused' in answer;

/** The chat whose panel is the one in front, or undefined: with Agents, Needs you or Activity in front there is none. */
function chatInFront(): string | undefined {
    const id = currentChat.value;
    return id === null || activePanel.value !== 'chat:' + id ? undefined : id;
}

/**
 * The campaign of the chat in front, or undefined: only while that chat's panel is the one in front is its campaign's
 * folder the one meant; with Agents, Needs you or Activity in front the folder is the harness root.
 */
function campaignOfChat(): string | undefined {
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

    const read = useCallback(async () => {
        if (!can('openIn.targets')) return;
        const asked = ++reads.current;
        try {
            const kept = await desktop.openIn.targets(campaignId, chatId);
            if (asked !== reads.current) return;
            if (refused(kept)) {
                setAnswer(null);
                setProblem(kept.refused);
            } else {
                setAnswer(kept);
                setAnswerChat(chatId);
                setProblem(null);
            }
        } catch (error) {
            if (asked !== reads.current) return;
            setAnswer(null);
            setProblem(error instanceof Error ? error.message : String(error));
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
        if (!menu || answer === null) return;
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
    }, [menu, answer]);

    const open = async (place: DesktopOpenInPlace) => {
        setMenu(false);
        if (!can('openIn.open')) return;
        const said = await desktop.openIn.open(place, campaignId).then(
            (result) => (result === null ? null : result.refused),
            (error: unknown) => (error instanceof Error ? error.message : String(error)),
        );
        // The place used last is the next default; a refusal stays on screen after the read.
        await read();
        if (said !== null) {
            setProblem(said);
            return;
        }
        const chosen = answer?.places.find((each) => each.id === place);
        const folder = answer?.folder ?? '';
        if (place === 'copy') setToast(t('openIn.copied', { folder }));
        else if (place === 'browser') setToast(t('openIn.browserOpened'));
        else {
            // Jason in a terminal works with this window's data. An editor already running keeps the environment it started
            // with, so in the demo an editor is said to be the other way about, and the file manager says nothing of data.
            const terminal = place === 'claude' || place === 'codex' || place === 'terminal';
            const editor = place !== 'files';
            const same = terminal || mode.value !== 'demo';
            setToast(
                t('openIn.opened', {
                    folder,
                    place: chosen === undefined ? '' : nameOf(chosen),
                }) +
                    (same
                        ? ' ' + t('openIn.sameData')
                        : editor && mode.value === 'demo'
                          ? ' ' + t('openIn.editorDemo')
                          : ''),
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
        if (!can('openIn.changeJason')) return;
        const said = await desktop.openIn.changeJason().then(
            (result) => (result === null ? null : result.refused),
            (error: unknown) => (error instanceof Error ? error.message : String(error)),
        );
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

    const places = answer?.places ?? [];
    const main = places.find((each) => each.id === answer?.default);
    const agents = places.filter((each) => each.id === 'claude' || each.id === 'codex');
    const editors = places.filter((each) => (editorIds as readonly string[]).includes(each.id));
    const rest = places.filter((each) => !agents.includes(each) && !editors.includes(each));
    const item = (place: { id: DesktopOpenInPlace; label: string }, subtitle?: string, text?: string) => (
        <button
            key={place.id}
            type="button"
            role="menuitem"
            className={place.id === answer?.default ? 'on' : undefined}
            onClick={() => void open(place.id)}
        >
            <span>{text ?? nameOf(place)}</span>
            {subtitle !== undefined && <small>{subtitle}</small>}
        </button>
    );
    return (
        <div className="open-in" ref={root}>
            {problem !== null && (
                <span className="open-in-problem" role="alert">
                    {problem}
                </span>
            )}
            <div className="open-in-split" role="group" aria-label={t('openIn.group')}>
                <button
                    type="button"
                    className="open-in-main"
                    disabled={main === undefined}
                    title={
                        main === undefined
                            ? undefined
                            : t('openIn.mainTitle', { folder: answer?.folder ?? '', place: nameOf(main) })
                    }
                    onClick={() => main !== undefined && void open(main.id)}
                >
                    {t('openIn.main', { place: main === undefined ? '' : nameOf(main) })}
                </button>
                <button
                    type="button"
                    className="open-in-arrow"
                    aria-label={t('openIn.choose')}
                    title={t('openIn.title')}
                    aria-haspopup="menu"
                    aria-expanded={menu}
                    onClick={() => {
                        setMenu(!menu);
                        if (!menu) void read();
                    }}
                >
                    ▾
                </button>
            </div>
            {toast !== null && (
                <span className="open-in-toast" role="status">
                    {toast}
                </span>
            )}
            {menu && answer !== null && (
                <div className="open-in-menu" role="menu" aria-label={t('openIn.title')}>
                    {agents.length > 0 && <div className="open-in-header">{t('openIn.agents')}</div>}
                    {agents.map((place) => item(place, t('openIn.session')))}
                    {editors.length > 0 && <div className="open-in-header">{t('openIn.editors')}</div>}
                    {editors.map((place) => item(place))}
                    {rest.map((place) =>
                        item(
                            place,
                            place.id === 'copy'
                                ? answer.folder
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
                    {answer.changeJason === true && <ChangeJasonEntry onChoose={() => void changeJason()} />}
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
                <span>{t('openIn.changeJason')}</span>
                <small>{t('openIn.changeJasonNote')}</small>
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
                <span>{t('openIn.continue', { place: offered.label })}</span>
                <small>
                    {running ? t('openIn.continueRunning') : t('openIn.continueNote', { place: offered.label })}
                </small>
            </button>
        </div>
    );
}
