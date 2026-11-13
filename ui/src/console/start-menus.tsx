import { useEffect, useRef, useState, type ReactNode, type RefObject } from 'react';
import type { DesktopChatMode, DesktopEffort } from '../platform/desktop';
import { fullAccessStanding, readStandingAgain, takeBackWords } from '../platform/full-access';
import { t } from '../platform/i18n';
import { hostOf, heldHostOf } from './chat';
import {
    anotherModel,
    effortChosen,
    hostChosen,
    modeChoices,
    modelChosen,
    modelTyped,
    modelView,
    onHost,
    type Chat,
} from './chat-model';
import { chatHosts } from './live';
import { here, setup } from './state';

// The two buttons under the start screen's box, each opening a menu: what the chat Start makes starts in (its access
// mode), and what it runs on (host, model, effort). They draw the choices the chat's own composer draws and ask the same
// functions what each one offers and refuses; nothing here decides a mode or a model.

/** The effort levels as the menus name them. */
const effortWords: Readonly<Record<DesktopEffort, string>> = {
    low: 'Low',
    medium: 'Medium',
    high: 'High',
    xhigh: 'Extra high',
    max: 'Max',
};

/** A button that opens a menu under it, closed by a choice, by Escape or by a click anywhere else. */
function MenuButton({
    label,
    ariaLabel,
    className,
    buttonRef,
    onOpen,
    children,
}: {
    label: string;
    ariaLabel: string;
    className: string;
    buttonRef?: RefObject<HTMLButtonElement>;
    onOpen?: () => void;
    children: (close: () => void) => ReactNode;
}) {
    const [open, setOpen] = useState(false);
    const root = useRef<HTMLSpanElement>(null);
    useEffect(() => {
        if (!open) return;
        const away = (event: MouseEvent) => {
            if (!root.current?.contains(event.target as Node)) setOpen(false);
        };
        document.addEventListener('mousedown', away);
        return () => document.removeEventListener('mousedown', away);
    }, [open]);
    return (
        <span
            className={`start-menu ${className}`}
            ref={root}
            onKeyDown={(event) => {
                if (event.key === 'Escape') setOpen(false);
            }}
        >
            <button
                ref={buttonRef}
                className="btn sm"
                type="button"
                aria-haspopup="menu"
                aria-expanded={open}
                aria-label={ariaLabel}
                onClick={() => {
                    if (!open) onOpen?.();
                    setOpen(!open);
                }}
            >
                {label} <span aria-hidden="true">▾</span>
            </button>
            {open && (
                <div className="start-menu-panel" role="menu" aria-label={ariaLabel}>
                    {children(() => setOpen(false))}
                </div>
            )}
        </span>
    );
}

function Item({
    label,
    checked,
    disabled,
    reason,
    onChoose,
}: {
    label: string;
    checked: boolean;
    disabled?: boolean;
    reason?: string | null | undefined;
    onChoose: () => void;
}) {
    return (
        <button
            className="start-menu-item"
            type="button"
            role="menuitemradio"
            aria-checked={checked}
            disabled={disabled}
            onClick={onChoose}
        >
            <span>{label}</span>
            {reason ? <span className="start-menu-reason">{reason}</span> : null}
        </button>
    );
}

/** The access button: the current mode's name, and a menu of the modes with what each host refuses said beside it. */
export function AccessButton({
    chat,
    choose,
    takeBack,
    buttonRef,
}: {
    chat: Chat;
    choose: (mode: DesktopChatMode) => void;
    takeBack: () => void;
    buttonRef: RefObject<HTMLButtonElement>;
}) {
    // The menu lists them in the order the design gives; what each offers or refuses is `modeChoices`'s.
    const order: readonly DesktopChatMode[] = ['full_access', 'ask_first', 'auto_edit', 'plan_only'];
    const choices = [...modeChoices(hostOf(chat))].sort((a, b) => order.indexOf(a.mode) - order.indexOf(b.mode));
    const current = choices.find((each) => each.mode === chat.mode)?.label ?? '';
    return (
        <MenuButton
            label={current}
            ariaLabel={`Access mode: ${current}`}
            className="start-access"
            buttonRef={buttonRef}
            onOpen={() => readStandingAgain(here().desktop)}
        >
            {(close) => (
                <>
                    <div className="start-menu-head">{t('start.access.header')}</div>
                    {choices.map((each) => (
                        <Item
                            key={each.mode}
                            label={each.label}
                            checked={each.mode === chat.mode}
                            disabled={each.disabled !== null}
                            reason={each.disabled?.replaceAll('`', '')}
                            onChoose={() => {
                                choose(each.mode);
                                close();
                            }}
                        />
                    ))}
                    {fullAccessStanding.value !== null && (
                        <Item
                            label={takeBackWords}
                            checked={false}
                            onChoose={() => {
                                takeBack();
                                close();
                            }}
                        />
                    )}
                </>
            )}
        </MenuButton>
    );
}

/** The model button: "<host> default", or the model's name, with the host, model and effort in its menu. */
export function ModelButton({
    chat,
    change,
    choose,
}: {
    chat: Chat;
    change: (next: (each: Chat) => Chat) => void;
    choose: (mode: DesktopChatMode) => void;
}) {
    const { drive, held, other } = chatHosts(setup.value?.hosts ?? []);
    const host = hostOf(chat);
    const ownHeld = heldHostOf(chat);
    if (!host) {
        // No host drives the chat: the button says so, and the held and unavailable ones are named in the menu.
        return (
            <MenuButton label="No AI host" ariaLabel="Model: no AI host" className="start-model">
                {() => (
                    <>
                        <div className="start-menu-head">AI host</div>
                        {[...held, ...other].map((each) => (
                            <Item
                                key={each.id}
                                label={each.label}
                                checked={each.id === ownHeld?.id}
                                disabled
                                reason={(each.belowFloor ?? 'runs campaign work, not the chat yet').replaceAll('`', '')}
                                onChoose={() => undefined}
                            />
                        ))}
                    </>
                )}
            </MenuButton>
        );
    }
    const standing = onHost(chat, host.id);
    const picked = modelView(standing, host);
    const onThisHost = (next: (each: Chat) => Chat) => change((each) => next(onHost(each, host.id)));
    const label = picked.typing
        ? standing.model?.trim() || 'Another model…'
        : (picked.offer?.label ?? picked.kept ?? t('start.model.default', { host: host.label }));
    return (
        <MenuButton label={label} ariaLabel={`Model: ${label}`} className="start-model">
            {(close) => (
                <>
                    <div className="start-menu-head">{t('start.model.header')}</div>
                    <Item
                        label={t('start.model.default', { host: host.label })}
                        checked={!picked.typing && picked.offer === null && picked.kept === null}
                        onChoose={() => {
                            onThisHost((each) => modelChosen(each, host, ''));
                            close();
                        }}
                    />
                    {host.models.map((each) => (
                        <Item
                            key={each.id}
                            label={each.label}
                            checked={picked.offer?.id === each.id}
                            onChoose={() => {
                                onThisHost((chosen) => modelChosen(chosen, host, each.id));
                                close();
                            }}
                        />
                    ))}
                    {host.typedModel && (
                        <Item
                            label="Another model…"
                            checked={picked.typing}
                            onChoose={() => onThisHost((each) => modelChosen(each, host, anotherModel))}
                        />
                    )}
                    {picked.typing && (
                        <input
                            className="model-name"
                            type="text"
                            aria-label="Model name"
                            placeholder="Full model name"
                            spellCheck={false}
                            value={standing.model ?? ''}
                            onChange={(event) => onThisHost((each) => modelTyped(each, event.target.value))}
                        />
                    )}
                    {picked.offer && picked.offer.efforts.length > 0 && (
                        <>
                            <div className="start-menu-head">Effort</div>
                            <Item
                                label="Default effort"
                                checked={standing.effort === null || !picked.offer.efforts.includes(standing.effort)}
                                onChoose={() => onThisHost((each) => effortChosen(each, ''))}
                            />
                            {picked.offer.efforts.map((each) => (
                                <Item
                                    key={each}
                                    label={effortWords[each]}
                                    checked={standing.effort === each}
                                    onChoose={() => onThisHost((chosen) => effortChosen(chosen, each))}
                                />
                            ))}
                        </>
                    )}
                    <div className="start-menu-head">AI host</div>
                    {drive.map((each) => (
                        <Item
                            key={each.id}
                            label={each.label}
                            checked={each.id === host.id}
                            onChoose={() => {
                                change((chosen) => hostChosen(chosen, each.id));
                                // A host that cannot take Full access or Auto-edit takes the chat back to Ask first.
                                if (
                                    (chat.mode === 'full_access' && each.fullAccess !== null) ||
                                    (chat.mode === 'auto_edit' && each.autoEdit !== null)
                                )
                                    choose('ask_first');
                                close();
                            }}
                        />
                    ))}
                    {[...held, ...other].map((each) => (
                        <Item
                            key={each.id}
                            label={each.label}
                            checked={false}
                            disabled
                            reason={(each.belowFloor ?? 'runs campaign work, not the chat yet').replaceAll('`', '')}
                            onChoose={() => undefined}
                        />
                    ))}
                </>
            )}
        </MenuButton>
    );
}
