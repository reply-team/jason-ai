import { useCallback, useEffect, useRef, useState } from 'react';
import type { DesktopOpenInAnswer, DesktopOpenInPlace, DesktopRefusal, JasonDesktop } from '../platform/desktop';
import { t } from '../platform/i18n';
import { chats, currentChat } from './state';

// "Open in": the split button of the console's top bar, in Live and in Demo alike. The main part opens the folder in the
// default place, the arrow lists every place this computer has. The page names a place and, where a campaign's chat is
// in front, that campaign; it never holds or sends a path, and the folder's name it shows comes from the desktop.

const refused = (answer: unknown): answer is DesktopRefusal =>
    typeof answer === 'object' && answer !== null && 'refused' in answer;

/** The campaign of the chat in front, or undefined when none is, or the one in front is not about a campaign. */
function campaignOfChat(): string | undefined {
    const id = currentChat.value;
    if (id === null) return undefined;
    return chats.value[id]?.campaignId ?? undefined;
}

export function OpenIn({ desktop }: { desktop: JasonDesktop }) {
    const campaignId = campaignOfChat();
    const [answer, setAnswer] = useState<DesktopOpenInAnswer | null>(null);
    const [problem, setProblem] = useState<string | null>(null);
    const [menu, setMenu] = useState(false);
    const root = useRef<HTMLDivElement>(null);

    const read = useCallback(async () => {
        try {
            const kept = await desktop.openIn.targets(campaignId);
            if (refused(kept)) {
                setAnswer(null);
                setProblem(kept.refused);
            } else {
                setAnswer(kept);
                setProblem(null);
            }
        } catch (error) {
            setAnswer(null);
            setProblem(error instanceof Error ? error.message : String(error));
        }
    }, [desktop, campaignId]);

    useEffect(() => {
        void read();
    }, [read]);

    useEffect(() => {
        if (!menu) return;
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
    }, [menu]);

    const open = async (place: DesktopOpenInPlace) => {
        setMenu(false);
        const said = await desktop.openIn.open(place, campaignId).then(
            (result) => (result === null ? null : result.refused),
            (error: unknown) => (error instanceof Error ? error.message : String(error)),
        );
        // The place used last is the next default; a refusal stays on screen after the read.
        await read();
        if (said !== null) setProblem(said);
    };

    const places = answer?.places ?? [];
    const main = places.find((each) => each.id === answer?.default);
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
                    onClick={() => main !== undefined && void open(main.id)}
                >
                    {t('openIn.main', { place: main?.label ?? '' })}
                </button>
                <button
                    type="button"
                    className="open-in-arrow"
                    aria-label={t('openIn.choose')}
                    aria-haspopup="menu"
                    aria-expanded={menu}
                    disabled={answer === null}
                    onClick={() => {
                        setMenu(!menu);
                        if (!menu) void read();
                    }}
                >
                    ▾
                </button>
            </div>
            {menu && answer !== null && (
                <div className="open-in-menu" role="menu" aria-label={t('openIn.menu', { folder: answer.folder })}>
                    <div className="open-in-folder">{t('openIn.folder', { folder: answer.folder })}</div>
                    {places.map((place) => (
                        <button
                            key={place.id}
                            type="button"
                            role="menuitem"
                            className={place.id === answer.default ? 'on' : undefined}
                            onClick={() => void open(place.id)}
                        >
                            {place.label}
                        </button>
                    ))}
                </div>
            )}
        </div>
    );
}
