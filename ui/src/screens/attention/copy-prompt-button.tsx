import { useEffect, useRef, useState } from 'react';
import { useApi } from '../../api/context';
import { composePrompt, resolveFolder, type PromptKind } from './copy-prompt';

type Props = { kind: PromptKind; itemId: string; campaignId: string; workItemId: string };

/**
 * Puts on the clipboard the text that briefs an agent session on this one item. It holds nothing of the item; the
 * session reads it live, and the person still decides.
 */
export function CopyPromptButton({ kind, itemId, campaignId, workItemId }: Props) {
    const api = useApi();
    const [state, setState] = useState<'idle' | 'working' | 'copied' | 'failed'>('idle');
    const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
    useEffect(() => () => void (timer.current && clearTimeout(timer.current)), []);

    const copy = async () => {
        if (timer.current) clearTimeout(timer.current);
        setState('working');
        try {
            // The clipboard is asked inside this click, before any read is awaited: a browser takes a write only
            // within the gesture's call stack, so the text is handed over as a promise it resolves when it is ready.
            // Where there is no `ClipboardItem`, the `writeText` fallback awaits the text first, by design: it is the
            // exception.
            const text = resolveFolder(api, campaignId).then((folder) =>
                composePrompt(kind, itemId, campaignId, workItemId, folder),
            );
            text.catch(() => undefined);
            if (typeof ClipboardItem !== 'undefined' && typeof navigator.clipboard?.write === 'function') {
                const blob = text.then((value) => new Blob([value], { type: 'text/plain' }));
                await navigator.clipboard.write([new ClipboardItem({ 'text/plain': blob })]);
            } else {
                await navigator.clipboard.writeText(await text);
            }
            setState('copied');
        } catch {
            setState('failed');
        }
        timer.current = setTimeout(() => setState('idle'), 2500);
    };

    return (
        <button className="btn" type="button" disabled={state === 'working'} onClick={() => void copy()}>
            {state === 'copied' ? 'Copied' : state === 'failed' ? 'Copy failed' : 'Copy prompt'}
        </button>
    );
}
