import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode, type RefObject } from 'react';
import { can, cannotHere } from '../platform/places';
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

// The two buttons in a composer's row, each opening a menu: the access mode a chat works in, and what it runs on (host,
// model, effort). The start screen's box and a chat's own composer draw the same two; `change` and `choose` say where a
// choice goes. They ask the same functions what each one offers and refuses; nothing here decides a mode or a model.
//
// A menu here keeps what a native select gave: it opens from the keyboard (Enter, Space, the arrows), the arrows,
// Home and End move through its items, Enter or Space chooses, Escape closes it, and focus goes back to the button;
// the button says the current value in its name, and every item says whether it is the one chosen.

/** The effort levels as the menus name them. */
const effortWords: Readonly<Record<DesktopEffort, string>> = {
    low: 'Low',
    medium: 'Medium',
    high: 'High',
    xhigh: 'Extra high',
    max: 'Max',
};

/** What each access mode does, in one line under its name in the menu and under the box. */
export const modeSays: Readonly<Record<DesktopChatMode, string>> = {
    ask_first: 'Asks on a card before anything Jason does not settle itself.',
    plan_only: 'Reads and proposes, and changes nothing.',
    auto_edit: "Works on files in the chat's own folder without asking; everything else is as in Ask first.",
    full_access: 'Runs commands on this computer without asking, except those Jason cannot read.',
};

/** The line icons the composer's row draws, on the console's 24-unit grid, stroked in the current colour. */
export function Glyph({ name, size = 14 }: { name: 'shield' | 'chev' | 'up' | 'stop'; size?: number }) {
    return (
        <svg
            className={`glyph-${name}`}
            width={size}
            height={size}
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth={name === 'up' ? 2.2 : 2}
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
        >
            {name === 'shield' && <path d="M12 3l8 4v5c0 5-3.5 8-8 9-4.5-1-8-4-8-9V7z" />}
            {name === 'chev' && <path d="M6 9l6 6 6-6" />}
            {name === 'up' && <path d="M12 19V5M5 12l7-7 7 7" />}
            {name === 'stop' && <rect x="7" y="7" width="10" height="10" rx="2" fill="currentColor" stroke="none" />}
        </svg>
    );
}

/** The items of a menu a key moves through, in the order they are drawn. */
const itemsOf = (menu: HTMLElement | null): HTMLElement[] =>
    menu ? [...menu.querySelectorAll<HTMLElement>('[role="menuitemradio"], [role="menuitem"]')] : [];

/**
 * A button that opens a menu over it. The menu takes the focus on the item chosen (else the first) when it opens; a
 * choice, Escape, Tab or a click anywhere else closes it, and a choice or Escape gives the focus back to the button.
 */
export function MenuButton({
    label,
    ariaLabel,
    className,
    menuClassName,
    buttonRef,
    disabled,
    onOpen,
    children,
}: {
    label: ReactNode;
    ariaLabel: string;
    className: string;
    menuClassName: string;
    buttonRef?: RefObject<HTMLButtonElement> | undefined;
    disabled?: boolean;
    onOpen?: () => void;
    children: (close: () => void) => ReactNode;
}) {
    const [open, setOpen] = useState(false);
    const root = useRef<HTMLSpanElement>(null);
    const ownButton = useRef<HTMLButtonElement>(null);
    const button = buttonRef ?? ownButton;
    const menu = useRef<HTMLDivElement>(null);
    const id = useId();
    useEffect(() => {
        if (!open) return;
        const away = (event: MouseEvent) => {
            if (!root.current?.contains(event.target as Node)) setOpen(false);
        };
        document.addEventListener('mousedown', away);
        return () => document.removeEventListener('mousedown', away);
    }, [open]);
    // On opening, the focus goes into the menu: to the mode or model chosen, else to any item chosen, else the first.
    useEffect(() => {
        if (!open) return;
        const items = itemsOf(menu.current);
        const chosen = items.filter((each) => each.getAttribute('aria-checked') === 'true');
        (
            chosen.find((each) => each.dataset.kind === 'mode' || each.dataset.kind === 'model') ??
            chosen[0] ??
            items[0]
        )?.focus();
    }, [open]);
    useEffect(() => {
        if (open && disabled) setOpen(false);
    }, [open, disabled]);
    const show = () => {
        onOpen?.();
        setOpen(true);
    };
    const close = () => {
        setOpen(false);
        button.current?.focus();
    };
    const move = (event: KeyboardEvent<HTMLDivElement>) => {
        if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            close();
            return;
        }
        if (event.key === 'Tab') {
            setOpen(false);
            return;
        }
        // A typed model name keeps its own keys, except Enter, which closes the menu instead of sending the message.
        if ((event.target as HTMLElement).tagName === 'INPUT') {
            if (event.key === 'Enter') {
                event.preventDefault();
                close();
            }
            return;
        }
        const items = itemsOf(menu.current);
        if (items.length === 0) return;
        const at = items.indexOf(document.activeElement as HTMLElement);
        const to =
            event.key === 'ArrowDown'
                ? (at + 1) % items.length
                : event.key === 'ArrowUp'
                  ? (at - 1 + items.length) % items.length
                  : event.key === 'Home'
                    ? 0
                    : event.key === 'End'
                      ? items.length - 1
                      : null;
        if (to === null) return;
        event.preventDefault();
        items[to]!.focus();
    };
    return (
        <span className={`cmenu ${className}`} ref={root}>
            <button
                ref={button}
                className="cpick"
                type="button"
                aria-haspopup="menu"
                aria-expanded={open}
                aria-controls={open ? id : undefined}
                aria-label={ariaLabel}
                disabled={disabled}
                onClick={() => (open ? setOpen(false) : show())}
                onKeyDown={(event) => {
                    if (!open && (event.key === 'ArrowDown' || event.key === 'ArrowUp')) {
                        event.preventDefault();
                        show();
                    }
                }}
            >
                {label}
                <Glyph name="chev" size={13} />
            </button>
            {open && (
                <div
                    id={id}
                    ref={menu}
                    className={`menu cmenu-panel ${menuClassName}`}
                    role="menu"
                    aria-label={ariaLabel}
                    onKeyDown={move}
                >
                    {children(close)}
                </div>
            )}
        </span>
    );
}

/**
 * One choice of a menu: its name, a line under it (what it does, or why it cannot be chosen) and a check when it is
 * the one chosen. A choice that cannot be made stays in the menu and in the keys' way, says so, and does nothing.
 */
function Item({
    label,
    value,
    kind,
    checked,
    disabled,
    small,
    className = 'mi',
    onChoose,
}: {
    label: string;
    value: string;
    /** What the item chooses, so a reader of the menu tells a host from a model: `mode`, `host`, `model`. */
    kind: 'mode' | 'host' | 'model';
    checked: boolean;
    disabled?: boolean | undefined;
    small?: string | null | undefined;
    className?: string;
    onChoose: () => void;
}) {
    const said = useId();
    return (
        <button
            className={`${className}${checked ? ' on' : ''}`}
            type="button"
            role="menuitemradio"
            tabIndex={-1}
            aria-checked={checked}
            aria-disabled={disabled ? true : undefined}
            aria-label={label}
            aria-describedby={small ? said : undefined}
            data-kind={kind}
            data-value={value}
            onClick={() => {
                if (!disabled) onChoose();
            }}
        >
            <b>{label}</b>
            {small ? <small id={said}>{small}</small> : null}
            {checked ? (
                <i className="acck" aria-hidden="true">
                    ✓
                </i>
            ) : null}
        </button>
    );
}

/** The access button: the current mode's name, and a menu of the modes with what each does or why it is refused. */
export function AccessButton({
    chat,
    choose,
    takeBack,
    buttonRef,
    header,
    order,
}: {
    chat: Chat;
    choose: (mode: DesktopChatMode) => void;
    takeBack: () => void;
    buttonRef: RefObject<HTMLButtonElement>;
    /** The words over the modes, which say whose mode it is. */
    header: string;
    /** The order the menu lists the modes in; absent, `modeChoices`'s own. */
    order?: readonly DesktopChatMode[];
}) {
    const feed = chat.main === true;
    const listed = modeChoices(hostOf(chat), feed);
    const choices = order ? [...listed].sort((a, b) => order.indexOf(a.mode) - order.indexOf(b.mode)) : listed;
    const current = choices.find((each) => each.mode === chat.mode)?.label ?? chat.mode;
    const tone = chat.mode === 'full_access' ? ' acc-full' : chat.mode === 'plan_only' ? ' acc-plan' : '';
    return (
        <MenuButton
            label={
                <>
                    <Glyph name="shield" />
                    {current}
                </>
            }
            ariaLabel={`Access mode: ${current}`}
            className={`start-access acc${tone}`}
            menuClassName="accmenu"
            buttonRef={buttonRef}
            disabled={chat.removing}
            onOpen={() => readStandingAgain(here().desktop)}
        >
            {(close) => (
                <>
                    <div className="ml" aria-hidden="true">
                        {header}
                    </div>
                    {choices.map((each) => {
                        const elsewhere = each.mode !== chat.mode && !can('chat.setMode');
                        return (
                            <Item
                                key={each.mode}
                                className="accmi"
                                kind="mode"
                                label={each.label}
                                value={each.mode}
                                checked={each.mode === chat.mode}
                                disabled={each.disabled !== null || elsewhere}
                                small={
                                    each.disabled?.replaceAll('`', '') ??
                                    (elsewhere ? cannotHere() : modeSays[each.mode])
                                }
                                onChoose={() => {
                                    close();
                                    if (each.mode !== chat.mode) choose(each.mode);
                                }}
                            />
                        );
                    })}
                    {fullAccessStanding.value !== null && (
                        <>
                            <hr />
                            <Item
                                className="accmi"
                                kind="mode"
                                label={takeBackWords}
                                value="take_back"
                                checked={false}
                                disabled={!can('fullAccess.takeBack')}
                                small={can('fullAccess.takeBack') ? undefined : cannotHere()}
                                onChoose={() => {
                                    close();
                                    takeBack();
                                }}
                            />
                        </>
                    )}
                </>
            )}
        </MenuButton>
    );
}

/** Why a host that is listed cannot drive the chat, in one line. */
const whyNot = (host: { belowFloor?: string | null }): string =>
    (host.belowFloor ?? 'runs campaign work, not the chat yet').replaceAll('`', '');

/**
 * The model button: "<host> default", or the model's name with the effort beside it. Its menu: the hosts that drive the
 * chat as tabs, the host's models (Default, the models it lists, a model no longer listed, Another model), the effort as
 * a row of steps where the model takes one, and the hosts that cannot drive the chat with why.
 */
export function ModelButton({
    chat,
    change,
    choose,
    header,
}: {
    chat: Chat;
    change: (next: (each: Chat) => Chat) => void;
    choose: (mode: DesktopChatMode) => void;
    /** The words over the models, which say whose model it is. */
    header: string;
}) {
    const { drive, held, other } = chatHosts(setup.value?.hosts ?? []);
    const host = hostOf(chat);
    const ownHeld = heldHostOf(chat);
    const nameBox = useRef<HTMLInputElement>(null);
    const standing = host ? onHost(chat, host.id) : chat;
    const picked = host ? modelView(standing, host) : null;
    // The typed name takes the focus once Another model is chosen.
    const typing = picked?.typing === true;
    useEffect(() => {
        if (typing) nameBox.current?.focus();
    }, [typing]);
    const cannot = [...held, ...other].map((each) => (
        <Item
            key={each.id}
            label={each.label}
            value={each.id}
            kind="host"
            checked={each.id === ownHeld?.id && !host}
            disabled
            small={whyNot(each)}
            className="mi mdlmi"
            onChoose={() => undefined}
        />
    ));
    const others = cannot.length > 0 && (
        <div className="mdlother" role="group" aria-label="Other AI hosts">
            {drive.length > 0 && <hr />}
            <div className="ml" aria-hidden="true">
                {drive.length > 0 ? 'Other AI hosts' : 'AI host'}
            </div>
            {cannot}
        </div>
    );
    // The hosts that drive the chat, as tabs; choosing one is choosing where the next turn runs.
    const tabs = drive.length > 0 && (
        <div className="mdltabs" role="group" aria-label="AI host">
            {drive.map((each) => (
                <button
                    key={each.id}
                    className={each.id === host?.id ? 'on' : undefined}
                    type="button"
                    role="menuitemradio"
                    tabIndex={-1}
                    aria-checked={each.id === host?.id}
                    data-kind="host"
                    data-value={each.id}
                    onClick={() => {
                        if (each.id === host?.id) return;
                        change((chosen) => hostChosen(chosen, each.id));
                        // A host that cannot take Full access or Auto-edit takes the chat back to Ask first.
                        if (
                            (chat.mode === 'full_access' && each.fullAccess !== null) ||
                            (chat.mode === 'auto_edit' && each.autoEdit !== null)
                        )
                            choose('ask_first');
                    }}
                >
                    {each.label}
                </button>
            ))}
        </div>
    );
    if (!host || !picked) {
        // No host drives this chat: the button names the chat's own host held, or says there is none, and the menu
        // says why each listed host cannot, beside any other host that can.
        const label = ownHeld?.label ?? 'No AI host';
        return (
            <MenuButton
                label={label}
                ariaLabel={`Model: ${label}`}
                className="start-model mdl"
                menuClassName="mdlmenu"
                disabled={chat.removing}
            >
                {() => (
                    <>
                        {tabs}
                        {others}
                    </>
                )}
            </MenuButton>
        );
    }
    const onThisHost = (next: (each: Chat) => Chat) => change((each) => next(onHost(each, host.id)));
    const label = picked.typing
        ? standing.model?.trim() || 'Another model…'
        : (picked.offer?.label ?? picked.kept ?? t('start.model.default', { host: host.label }));
    const efforts = picked.offer?.efforts ?? [];
    const effort = standing.effort !== null && efforts.includes(standing.effort) ? standing.effort : null;
    const listsModels = host.models.length > 0 || host.typedModel || picked.kept !== null || host.modelsUnread !== null;
    const unread = host.modelsUnread !== null;
    return (
        <MenuButton
            label={
                <>
                    {label}
                    {effort && <span className="mdl-e">{effortWords[effort]}</span>}
                </>
            }
            ariaLabel={effort ? `Model: ${label}, ${effortWords[effort]} effort` : `Model: ${label}`}
            className="start-model mdl"
            menuClassName="mdlmenu"
            disabled={chat.removing}
        >
            {(close) => (
                <>
                    {tabs}
                    {listsModels && (
                        <div className="mdllist" role="group" aria-label="Model">
                            <div className="ml" aria-hidden="true">
                                {header}
                            </div>
                            <Item
                                className="mi mdlmi"
                                kind="model"
                                label="Default (recommended)"
                                value=""
                                checked={!picked.typing && picked.offer === null && picked.kept === null}
                                disabled={unread}
                                small={host.modelsUnread}
                                onChoose={() => {
                                    onThisHost((each) => modelChosen(each, host, ''));
                                    close();
                                }}
                            />
                            {host.models.map((each) => (
                                <Item
                                    key={each.id}
                                    className="mi mdlmi"
                                    kind="model"
                                    label={each.label}
                                    value={each.id}
                                    checked={picked.offer?.id === each.id}
                                    disabled={unread}
                                    onChoose={() => {
                                        onThisHost((chosen) => modelChosen(chosen, host, each.id));
                                        close();
                                    }}
                                />
                            ))}
                            {picked.kept !== null && (
                                <Item
                                    className="mi mdlmi"
                                    kind="model"
                                    label={picked.kept}
                                    value={picked.kept}
                                    checked
                                    small={`No longer listed, so the turn uses ${host.label}'s own model with no effort.`}
                                    onChoose={close}
                                />
                            )}
                            {host.typedModel && (
                                <Item
                                    className="mi mdlmi"
                                    kind="model"
                                    label="Another model…"
                                    value={anotherModel}
                                    checked={picked.typing}
                                    disabled={unread}
                                    onChoose={() => {
                                        onThisHost((each) => modelChosen(each, host, anotherModel));
                                        nameBox.current?.focus();
                                    }}
                                />
                            )}
                            {picked.typing && (
                                <input
                                    ref={nameBox}
                                    className="model-name"
                                    type="text"
                                    aria-label="Model name"
                                    placeholder="Full model name"
                                    spellCheck={false}
                                    disabled={chat.removing}
                                    value={standing.model ?? ''}
                                    onChange={(event) => onThisHost((each) => modelTyped(each, event.target.value))}
                                />
                            )}
                        </div>
                    )}
                    {efforts.length > 0 && (
                        <div className="effrow" role="group" aria-label="Effort">
                            <div className="effh" aria-hidden="true">
                                <span>Effort</span>
                                <b>{effort ? effortWords[effort] : 'Default'}</b>
                            </div>
                            <div className="effbar">
                                {efforts.map((each, at) => (
                                    <button
                                        key={each}
                                        className={
                                            each === effort
                                                ? 'effdot cur'
                                                : effort !== null && at < efforts.indexOf(effort)
                                                  ? 'effdot fill'
                                                  : 'effdot'
                                        }
                                        type="button"
                                        role="menuitemradio"
                                        tabIndex={-1}
                                        aria-checked={each === effort}
                                        aria-label={effortWords[each]}
                                        title={effortWords[each]}
                                        data-kind="effort"
                                        data-value={each}
                                        onClick={() => onThisHost((chosen) => effortChosen(chosen, each))}
                                    />
                                ))}
                            </div>
                            <small>
                                <button
                                    className={effort === null ? 'effdef on' : 'effdef'}
                                    type="button"
                                    role="menuitemradio"
                                    tabIndex={-1}
                                    aria-checked={effort === null}
                                    data-kind="effort"
                                    data-value=""
                                    onClick={() => onThisHost((chosen) => effortChosen(chosen, ''))}
                                >
                                    Default effort
                                </button>
                            </small>
                        </div>
                    )}
                    {others}
                </>
            )}
        </MenuButton>
    );
}
