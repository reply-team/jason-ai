/**
 * Pure: the desktop's links, `jason-desktop://<page>[/<id>]`. A link opens a page of the application and does nothing
 * else: no command, no turn, no text put into a chat, no card, approval or question answered, no chat mode, no setting,
 * no route, no record made. Its addresses are a closed table; anything else is refused by a closed code and opens
 * nothing. A link reaches the page as a typed value, never as the address.
 *
 * The scheme is the desktop's own, `jason-desktop`, on every platform. `jason:` is the runtime's: on Windows its toast
 * host registers it for the command line, which handles `jason://attention...` (docs/notifications.md); the desktop never
 * registers, declares or removes it.
 *
 * Where a link comes from:
 *
 * - Windows: the packaged application registers the scheme at each start with `app.setAsDefaultProtocolClient(scheme,
 *   path, ['--'])`, which the pinned Electron 44.10.2 writes under `HKEY_CURRENT_USER\Software\Classes\jason-desktop`
 *   with the command `"<path>" -- "%1"`: each part quoted only when it holds a space, a backslash or a quote, so the
 *   executable's path is quoted and the `--` is bare (shell/browser/browser_win.cc, GetProtocolLaunchPath, and
 *   shell/common/command_line_util_win.cc, AddQuoteForArg). The address arrives as an argument: in `process.argv` at a
 *   cold start, and from a second start by the `additionalData` of `requestSingleInstanceLock`. What protects a start
 *   from an address: the `--`, which ends Chromium's switches, so nothing after it is read as one; the packaged build's
 *   fuse that refuses Node's inspector options (`enableNodeCliInspectArguments` off); and the pick (`linkIn`), which
 *   takes a link only when the arguments are the address alone or `--` and the address, so a crafted address that a quote
 *   splits into several arguments opens nothing. Electron's own check of such arguments
 *   (shell/app/command_line_args.cc, CheckCommandLineArguments) stops at the first `--`, which here comes first, and so
 *   checks nothing. The uninstall takes the registration off (`uninstall.ts`).
 * - macOS: the bundle declares the scheme (electron-builder's `mac.protocols`, written as `CFBundleURLTypes`), and the
 *   address arrives by `open-url` alone, at a cold start as at a running one; a link in a start's arguments is refused
 *   there.
 * - Linux: the deb's menu entry declares `MimeType=x-scheme-handler/jason-desktop;` (electron-builder's `deb.mimeTypes`)
 *   and starts the program with `--` before the address (`Exec=... -- %U`, the deb's `executableArgs`). An AppImage
 *   declares no scheme and takes no link, nor does a development build anywhere (`linksTaken`).
 */

import type { LinuxPackage } from './linux-package.ts';

export const linkScheme = 'jason-desktop';

/** A page a link opens: one of the console's panels, or a screen at one entity. */
export type LinkTarget =
    | { page: 'feed' }
    | { page: 'needs' }
    | { page: 'campaigns' }
    | { page: 'campaign'; id: string }
    | { page: 'approval'; id: string }
    | { page: 'question'; id: string }
    | { page: 'workitem'; id: string }
    | { page: 'person'; id: string };

export type LinkRefusal = 'too_long' | 'not_a_link' | 'unknown_page' | 'bad_id' | 'two_links' | 'beside_flag' | 'unregistered';

/** The longest address taken, in characters; the longest in the table, an item of `attention`, is sixty-six. */
export const linkLimit = 72;

const crockford = '[0-9A-HJKMNP-TV-Z]{26}';
/** The pages that take no id, and the pages that take one, with the prefix its id carries. */
const plain = new Set(['feed', 'needs', 'campaigns']);
const withId: Readonly<Record<string, string>> = { campaign: 'cmp', approval: 'apr', question: 'dec', workitem: 'wi', person: 'cnt' };
const prefix = new RegExp(`^${linkScheme}://`, 'i');
/**
 * The runtime's attention links in the desktop's scheme, which the command line hands on from a toast's click where the
 * desktop is installed (docs/notifications.md): `attention`, the list, and `attention/<collection>/<id>`, one item, each
 * opening Needs you. The collection names the page its item opens.
 */
const attention: Readonly<Record<string, 'approval' | 'question'>> = { approvals: 'approval', questions: 'question' };

/** An address read: the page it opens, or why it opens nothing. */
export function parseLink(raw: unknown): LinkTarget | { refused: LinkRefusal } {
    if (typeof raw !== 'string' || !prefix.test(raw)) return { refused: 'not_a_link' };
    if (raw.length > linkLimit) return { refused: 'too_long' };
    let rest = raw.slice(`${linkScheme}://`.length);
    if (rest.endsWith('/')) rest = rest.slice(0, -1);
    const parts = rest.split('/');
    const page = parts[0] ?? '';
    if (page === 'attention') return attentionLink(parts);
    if (plain.has(page)) return parts.length === 1 ? ({ page } as LinkTarget) : { refused: 'unknown_page' };
    const shape = Object.hasOwn(withId, page) ? new RegExp(`^${withId[page]}_${crockford}$`) : null;
    if (shape === null) return { refused: 'unknown_page' };
    const id = parts[1];
    if (parts.length !== 2 || id === undefined || !shape.test(id)) return { refused: 'bad_id' };
    return { page, id } as LinkTarget;
}

/** `attention` alone opens Needs you; one approval or question in its collection opens Needs you on that item. */
function attentionLink(parts: readonly string[]): LinkTarget | { refused: LinkRefusal } {
    if (parts.length === 1) return { page: 'needs' };
    const collection = parts[1] ?? '';
    const page = Object.hasOwn(attention, collection) ? attention[collection]! : null;
    if (page === null) return { refused: 'unknown_page' };
    const id = parts[2];
    if (parts.length !== 3 || id === undefined || !new RegExp(`^${withId[page]}_${crockford}$`).test(id)) return { refused: 'bad_id' };
    return { page, id };
}

const isLink = (arg: string) => prefix.test(arg) || new RegExp(`^${linkScheme}:`, 'i').test(arg);

/**
 * The link a start's arguments carry, the executable left out: none, one taken whole, or a refusal. A link is taken only
 * when the arguments are the address alone or `--` and the address, as the deb's menu entry and the Windows
 * registration start the application; anything beside it, a switch, a flag of the desktop's own or a stray word, is
 * `beside_flag`, and two addresses are `two_links`. On macOS, where a link comes by `open-url` alone, a link in the
 * arguments is `unregistered`. A start with no address is not this rule's.
 */
export function linkIn(args: readonly string[], platform?: NodeJS.Platform): { kind: 'none' } | { kind: 'link'; raw: string } | { kind: 'refused'; reason: LinkRefusal } {
    const links = args.filter(isLink);
    if (links.length === 0) return { kind: 'none' };
    if (platform === 'darwin') return { kind: 'refused', reason: 'unregistered' };
    if (links.length > 1) return { kind: 'refused', reason: 'two_links' };
    const raw = links[0]!;
    const alone = args.length === 1 || (args.length === 2 && args[0] === '--' && args[1] === raw);
    return alone ? { kind: 'link', raw } : { kind: 'refused', reason: 'beside_flag' };
}

/** Whether this form of the application takes links: a packaged one on Windows or macOS, and the deb on Linux. */
export function linksTaken(at: { packaged: boolean; platform: NodeJS.Platform; form: LinuxPackage | null }): boolean {
    if (!at.packaged) return false;
    if (at.platform === 'win32' || at.platform === 'darwin') return true;
    return at.platform === 'linux' && at.form?.form === 'deb';
}

/** What the start registers: on a packaged Windows application, the scheme with `--` before the address; else nothing. */
export function linkRegistration(platform: NodeJS.Platform, packaged: boolean, executable: string): { scheme: string; path: string; args: string[] } | null {
    return platform === 'win32' && packaged ? { scheme: linkScheme, path: executable, args: ['--'] } : null;
}

/**
 * Where a link waits for the page: held, the newest replacing an older one, until the page takes it (`take`), and sent
 * at once while a page that took its links is loaded; held again once that page goes (`away`).
 */
export class LinkPost {
    #held: LinkTarget | null = null;
    #listening = false;
    readonly #send: (target: LinkTarget) => void;

    constructor(send: (target: LinkTarget) => void) {
        this.#send = send;
    }

    arrived(target: LinkTarget): void {
        if (this.#listening) this.#send(target);
        else this.#held = target;
    }

    take(): LinkTarget | null {
        this.#listening = true;
        const held = this.#held;
        this.#held = null;
        return held;
    }

    away(): void {
        this.#listening = false;
    }
}
