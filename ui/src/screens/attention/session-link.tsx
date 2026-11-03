import type { PromptKind } from './copy-prompt';

type Props = { kind: PromptKind; itemId: string };

/** The browser's own word for the machine: the newer reading where the browser offers it, else the older one. */
function onWindows(): boolean {
    const nav = navigator as Navigator & { userAgentData?: { platform?: string } };
    const platform = nav.userAgentData?.platform ?? nav.platform ?? '';
    return /^win/i.test(platform);
}

/**
 * Starts the person's own agent host on this one item, in a new terminal at its campaign's folder. On Windows a link the
 * executable answers, once a Windows toast has registered it; on every system the command that does the same, to run by
 * hand. Jason's brief asks the agent to propose only and not to approve, reject or answer; what it may run is your
 * host's own permission settings' to decide. The runtime is told nothing of the machine.
 */
export function SessionLink({ kind, itemId }: Props) {
    const collection = kind === 'approval' ? 'approvals' : 'questions';
    const command = `jason session open --${kind} ${itemId}`;
    return (
        <>
            {onWindows() && (
                <a class="btn" href={`jason://attention/${collection}/${itemId}/session`}>
                    Open an AI session
                </a>
            )}
            <span class="session-command">
                <span>Run this in a terminal:</span> <code class="mono">{command}</code>
            </span>
        </>
    );
}
