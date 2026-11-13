/**
 * The demo's story as the desktop reads it: the data module of the demo provider package, `modules/story.js`, which holds
 * one exported literal and nothing else. The seller, the campaigns, the people and their messages are that file's, so the
 * provider that reports the replies and the seed that makes the people work from one set; this file holds no copy of any
 * of it, only the shape it expects and the reading.
 *
 * The file is `export const story = ` and one strict JSON value and `;` and a newline, in ASCII. It is read by cutting
 * those off and parsing what is left: nothing in it is evaluated and nothing is imported.
 */

export interface StoryChannel {
    channel: 'email' | 'phone';
    value: string;
}

export interface StorySend {
    key: string;
    operation: 'message.send';
    channel: 'email';
    /** `given`: the story says it was already approved, by the operator. `waiting`: it waits for a person. */
    approval: 'given' | 'waiting';
    subject: string;
    body: string;
    note?: string;
}

export interface StoryReply {
    send: string;
    offset_seconds: number;
    body: string;
    meeting_intent?: boolean;
}

export interface StoryPerson {
    key: string;
    contact_id?: string;
    first_name: string;
    last_name: string;
    title: string;
    /** A company key. */
    company: string;
    time_zone: string;
    channels: StoryChannel[];
    /** A campaign key. */
    campaign: string;
    sends: StorySend[];
    replies: StoryReply[];
}

export interface StorySender {
    external_id: string;
    channel: string;
    name: string;
    address: string;
}

export interface StoryCampaign {
    key: string;
    name: string;
    status: 'active' | 'paused' | 'draft';
    description: string;
}

export interface StoryCompany {
    key: string;
    name: string;
    domain: string;
    city: string;
    region: string;
    trucks: number;
}

export interface Story {
    seller: { name: string; domain: string };
    operator: { name: string; actor: string };
    project: { name: string };
    senders: StorySender[];
    campaigns: StoryCampaign[];
    companies: StoryCompany[];
    people: StoryPerson[];
}

const prefix = 'export const story = ';
const suffix = ';\n';

/** The most the manager's review cadence can be: seven days. */
export const reviewSeconds = 604_800;

/**
 * The story a module's text holds. The text must be the one literal and nothing around it; anything else, or a value of
 * another shape, is refused with the reason.
 */
export function parseStory(text: string): Story {
    if (!text.startsWith(prefix) || !text.endsWith(suffix)) throw new Error('The story module is not one exported literal.');
    // eslint-disable-next-line no-control-regex
    if (/[^\x00-\x7f]/.test(text)) throw new Error('The story module is not ASCII.');
    const value: unknown = JSON.parse(text.slice(prefix.length, text.length - suffix.length));
    return checked(value);
}

function checked(value: unknown): Story {
    const fail = (what: string): never => {
        throw new Error(`The story has no ${what}.`);
    };
    const object = (v: unknown, what: string): Record<string, unknown> => (typeof v === 'object' && v !== null && !Array.isArray(v) ? (v as Record<string, unknown>) : fail(what));
    const list = (v: unknown, what: string): unknown[] => (Array.isArray(v) ? v : fail(what));
    const text = (v: unknown, what: string): string => (typeof v === 'string' && v !== '' ? v : fail(what));

    const root = object(value, 'object');
    text(object(root['seller'], 'seller')['name'], 'seller name');
    text(object(root['operator'], 'operator')['actor'], 'operator actor');
    text(object(root['project'], 'project')['name'], 'project name');
    const senders = list(root['senders'], 'senders');
    if (senders.length === 0) fail('sender');
    for (const sender of senders) {
        const s = object(sender, 'sender');
        text(s['external_id'], 'sender id');
        text(s['address'], 'sender address');
    }
    const campaigns = list(root['campaigns'], 'campaigns');
    const keys = new Set<string>();
    for (const campaign of campaigns) {
        const c = object(campaign, 'campaign');
        keys.add(text(c['key'], 'campaign key'));
        text(c['name'], 'campaign name');
        if (c['status'] !== 'active' && c['status'] !== 'paused' && c['status'] !== 'draft') fail('campaign status');
    }
    list(root['companies'], 'companies');
    for (const person of list(root['people'], 'people')) {
        const p = object(person, 'person');
        text(p['key'], 'person key');
        text(p['first_name'], 'person name');
        if (!keys.has(text(p['campaign'], 'person campaign'))) fail('campaign for a person');
        const channels = list(p['channels'], 'channels');
        if (!channels.some((c) => object(c, 'channel')['channel'] === 'email')) fail('email for a person');
        for (const send of list(p['sends'], 'sends')) {
            const s = object(send, 'send');
            text(s['key'], 'send key');
            if (s['operation'] !== 'message.send') fail('send operation');
            if (s['approval'] !== 'given' && s['approval'] !== 'waiting') fail('send approval');
            text(s['subject'], 'send subject');
            text(s['body'], 'send body');
        }
        list(p['replies'], 'replies');
    }
    return value as Story;
}
