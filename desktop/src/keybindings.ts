/**
 * Pure: the keys the application's menu gives, and what each one does. A key either sends the page one of the commands
 * the page declares (`DesktopCommand` in `ui/src/platform/desktop.d.ts`, through `onCommand`), or acts on the window
 * itself. The page ignores a command it does not know, so a key bound to a name the page never declared would do
 * nothing; the tests hold every binding to the list below and the list to the page's declaration.
 */

/** The commands the page declares for `onCommand`, in the order it declares them. */
export const desktopCommands = [
    'chat.new',
    'navigate.chat',
    'navigate.attention',
    'navigate.campaigns',
    'navigate.activity',
    'navigate.performance',
    'navigate.workitems',
    'navigate.people',
    'navigate.system',
    'navigate.settings',
    'navigate.connect',
] as const;

export type DesktopCommand = (typeof desktopCommands)[number];

/** What the window does itself: pair again and load the page fresh, or open the developer tools of a build not packaged. */
export type WindowAction = 'reload' | 'devtools';

export interface Binding {
    label: string;
    accelerator: string;
    /** The command sent to the page, or the window's own act. */
    does: { command: DesktopCommand } | { window: WindowAction };
}

export const bindings: readonly Binding[] = [
    { label: 'New Chat', accelerator: 'CmdOrCtrl+N', does: { command: 'chat.new' } },
    { label: 'What Needs You', accelerator: 'CmdOrCtrl+1', does: { command: 'navigate.attention' } },
    { label: 'Performance', accelerator: 'CmdOrCtrl+2', does: { command: 'navigate.performance' } },
    { label: 'Campaigns', accelerator: 'CmdOrCtrl+3', does: { command: 'navigate.campaigns' } },
    { label: 'Work Items', accelerator: 'CmdOrCtrl+4', does: { command: 'navigate.workitems' } },
    { label: 'People', accelerator: 'CmdOrCtrl+5', does: { command: 'navigate.people' } },
    { label: 'System', accelerator: 'CmdOrCtrl+6', does: { command: 'navigate.system' } },
    { label: 'Reload', accelerator: 'CmdOrCtrl+R', does: { window: 'reload' } },
    { label: 'Developer Tools', accelerator: 'F12', does: { window: 'devtools' } },
];

/** Electron's accelerator grammar, as far as the menu uses it. */
export const acceleratorPattern = /^(?:(?:CmdOrCtrl|CommandOrControl|Cmd|Command|Ctrl|Control|Alt|Option|Shift|Super)\+)*(?:[A-Za-z0-9]|F(?:[1-9]|1[0-9]|2[0-4])|Space|Tab|Enter|Escape|Up|Down|Left|Right|Home|End|PageUp|PageDown|Plus|-|=|,|\.|\/|;|\[|\]|\\|`|')$/;

export function isDesktopCommand(name: string): name is DesktopCommand {
    return (desktopCommands as readonly string[]).includes(name);
}

/** The bindings a build offers: the developer tools only in a build that is not packaged. */
export function bindingsFor(packaged: boolean): Binding[] {
    return bindings.filter((binding) => !('window' in binding.does && binding.does.window === 'devtools' && packaged));
}

/**
 * Whether a command may be sent to the window now: only while the window shows the runtime's own page, so a command
 * never reaches the desktop's starting page or anything else.
 */
export function mayCommand(windowUrl: string, runtimeOrigin: string | null): boolean {
    if (runtimeOrigin === null) return false;
    try {
        return new URL(windowUrl).origin === runtimeOrigin;
    } catch {
        return false;
    }
}
