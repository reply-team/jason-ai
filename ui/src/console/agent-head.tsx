import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { useApi } from '../api/context';
import { essentials } from '../platform/console-shell';
import { can } from '../platform/places';
import { navigate, route, type CampaignTab, type Route } from '../router';
import { Tabs } from '../layout/tabs';
import { agentKind, firstTabName, kindWord } from '../screens/campaigns/kind';
import { CampaignMoves } from '../screens/campaigns/moves';
import { agentChatRoute } from './agent-place';
import { agentFacts, peopleOf, readAgent } from './agent-facts';
import { isUnmade, restartChat } from './chat';
import type { Chat } from './chat-model';
import { read as listedRead, reload as reloadCampaigns } from './campaigns';
import { toCampaignRow } from './live';
import { begin, placeGates, runsOn } from './conversation-acts';
import { conversationItems, type MenuItem } from './conversation-items';
import { openFolder, openSession } from './open-folder';
import { MenuEntries, menuItems, menuKeys } from './menu-entries';
import { chats, conversations } from './state';

// The agent's header: one for every place the agent has in the console, its chat and its page's tabs, drawn by the area
// above them and kept while the person moves between that agent's tabs. Its name, its state and its type are the
// runtime's, read for it (`agent-facts.ts`); until they are read it says the name the conversation gives and nothing
// else. Its one move is the agent page's own control; "…" holds the acts of the agent's conversation, each naming it.

type Page = Exclude<CampaignTab, 'chat'>;

const pageTabNames: Record<Page, string> = {
    sequence: 'Sequence', // drawn by firstTabName: Sequence where the type says Sequence, Paths otherwise
    people: 'People',
    inbox: 'Inbox',
    results: 'Results',
    settings: 'Settings',
};

/** The tabs only the demo draws, and the classic shell; the console's Live leaves them out, and their addresses open them all the same. */
const demoOnly: readonly Page[] = ['sequence'];

const number = new Intl.NumberFormat('en-US');

/** The agent's conversation the console holds open, else the one the desktop keeps; the Feed is nobody's. */
function conversationOf(id: string): { chat: Chat | undefined; kept: string | undefined } {
    // A conversation made for it before its unmade chat, which stands in for one only while there is none.
    const ofAgent = Object.values(chats.value).filter((each) => !each.main && each.campaignId === id);
    const chat = ofAgent.find((each) => !isUnmade(each.id)) ?? ofAgent[0];
    const kept = conversations.value.find((each) => !each.main && each.campaignId === id)?.id;
    return { chat, kept };
}

/** "…": the acts of the agent's conversation, as its chat's bar held them, each word naming the conversation. */
function ConversationMenu({ agent }: { agent: string }) {
    const [open, setOpen] = useState(false);
    const button = useRef<HTMLButtonElement>(null);
    const menu = useRef<HTMLDivElement>(null);
    const { chat, kept } = conversationOf(agent);
    const id = kept ?? chat?.id;
    const items = conversationItems(
        {
            kept,
            id,
            campaignId: agent,
            running: chat?.running === true,
            removing: chat?.removing === true,
            host: runsOn(chat),
        },
        placeGates(),
    );
    useEffect(() => {
        if (!open) return;
        menuItems(menu.current)[0]?.focus();
        const away = (event: MouseEvent) => {
            if (!menu.current?.contains(event.target as Node) && !button.current?.contains(event.target as Node))
                setOpen(false);
        };
        document.addEventListener('mousedown', away);
        return () => document.removeEventListener('mousedown', away);
    }, [open]);
    const close = (focus: boolean) => {
        setOpen(false);
        if (focus) button.current?.focus();
    };
    // The menu's keys are taken on the wrap around the button and the menu, so a menu whose acts are all held, which
    // takes no focus, still closes on Escape and on Tab.
    const keys = (event: KeyboardEvent<HTMLDivElement>) => {
        if (open) menuKeys(event, menu.current, close);
    };
    const show = () => navigate(agentChatRoute(agent));
    const choose = (item: MenuItem) => {
        close(false);
        if (item.kind === 'rename' || item.kind === 'remove') void begin(kept!, item.kind, show);
        else if (item.kind === 'fresh') void restartChat(id!);
        else if (item.kind === 'folder') void openFolder(agent);
        else if (item.kind === 'session') void openSession(agent, item.host);
    };
    return (
        <div className="agent-more-wrap" onKeyDown={keys}>
            <button
                ref={button}
                type="button"
                className="agent-more"
                aria-label="More for this agent's conversation"
                title="More"
                aria-haspopup="menu"
                aria-expanded={open}
                onClick={() => setOpen(!open)}
                onKeyDown={(event) => {
                    if (event.key === 'ArrowDown' && !open) {
                        event.preventDefault();
                        setOpen(true);
                    }
                }}
            >
                <span aria-hidden="true">…</span>
            </button>
            {open && (
                <div ref={menu} className="agent-menu" role="menu" aria-label="This agent's conversation">
                    <MenuEntries items={items} onChoose={choose} />
                </div>
            )}
        </div>
    );
}

export function AgentHead({ id, tab }: { id: string; tab: CampaignTab }) {
    const api = useApi();
    // Read when drawn, and again once the list of agents is read: an agent it does not hold has its count read alone.
    const listRead = listedRead.value;
    useEffect(() => {
        void readAgent(api, id);
    }, [api, id, listRead]);
    const facts = agentFacts.value[id];
    const { chat, kept } = conversationOf(id);
    // Unread, the agent is named as its conversation names it, and nothing is said of its state or type.
    const name =
        facts?.campaign.name ?? conversations.value.find((each) => each.id === kept)?.title ?? chat?.title ?? '';
    const campaign = facts?.campaign;
    const kind = campaign ? agentKind(campaign, facts!.creating) : null;
    const word = kindWord(kind);
    const at = route.value;
    const shape: Extract<Route, { screen: 'campaigns' }> =
        at.screen === 'campaigns' && at.id === id
            ? at
            : { screen: 'campaigns', id, tab, view: 'summary', period: '7d', filters: {} };
    const to = (each: CampaignTab): Route =>
        each === 'chat'
            ? agentChatRoute(id)
            : { screen: 'campaigns', id, tab: each, view: shape.view, period: shape.period, filters: shape.filters };
    const pages = (['sequence', 'people', 'inbox', 'results', 'settings'] as const).filter(
        (each) => !essentials() || !demoOnly.includes(each) || each === tab,
    );
    const people = peopleOf(id);
    const label = (each: Page): string =>
        each === 'people' && shape.view === 'activity' && tab !== 'chat'
            ? 'People Activity'
            : each === 'sequence'
              ? firstTabName(kind)
              : pageTabNames[each];
    return (
        <div className="agent-head" role="region" aria-label="Agent">
            <div className="agent-bar">
                <div className="agent-title">
                    <h2>{name}</h2>
                    {campaign && (
                        <span
                            className={`pillc agent-state ${toCampaignRow(campaign, undefined).tone}`}
                            data-status={campaign.status}
                        >
                            {toCampaignRow(campaign, undefined).state}
                        </span>
                    )}
                    {word && (
                        <span className="pillc agent-kind" data-kind={kind}>
                            {word}
                        </span>
                    )}
                </div>
                <div className="agent-acts">
                    {campaign && (
                        <CampaignMoves
                            campaign={campaign}
                            buttonClass={(move) => (move === 'pause' ? 'btn agent-move' : 'btn p agent-move')}
                            frame={(dialog) => <div className="harness">{dialog}</div>}
                            onDone={() => {
                                void readAgent(api, id);
                                void reloadCampaigns();
                            }}
                        />
                    )}
                    {can('chat.create') && <ConversationMenu agent={id} />}
                </div>
            </div>
            <Tabs
                tabs={[
                    ...(can('chat.create') ? [{ key: 'chat' as CampaignTab, label: 'Chat' }] : []),
                    ...pages.map((each) => ({
                        key: each as CampaignTab,
                        label: label(each),
                        ...(each === 'people' && people !== null ? { count: number.format(people) } : {}),
                    })),
                ]}
                active={tab}
                to={to}
            />
        </div>
    );
}
