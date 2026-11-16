import { useEffect, useMemo } from 'react';
import { useApi } from '../../api/context';
import type { Approval } from '../../api/types';
import { lookupFor, readSender, senderMark, type SenderReads } from '../../console/senders';
import { SENDER_WHEN, senderAsked, senderLine, senderWords } from '../../console/senders-model';

/**
 * The sending name the provider holds now for the mailbox an approval's send names, under the address it goes from: one
 * piece, drawn on an agent's chat's card and on Needs you. Read when the card is drawn, one read per card; while it is
 * read the place is held empty at the line's height, and nothing for an approval whose input names no sender. It decides nothing and
 * holds nothing: Approve, Reject and their gate are the card's.
 */
export function SenderLine({ approval, reads }: { approval: Approval | undefined; reads: SenderReads }) {
    const api = useApi();
    const asked = senderAsked(approval);
    // When this card, for this approval, was drawn: it shows only a read begun after it.
    const id = approval?.id;
    const drawn = useMemo(() => (id === undefined ? 0 : senderMark()), [id]);
    const campaignId = asked?.campaignId;
    const channel = asked?.channel;
    const externalId = asked?.externalId;
    const pluginId = asked?.pluginId;
    useEffect(() => {
        if (campaignId === undefined || channel === undefined || externalId === undefined || pluginId === undefined)
            return;
        void readSender(api, reads, { campaignId, channel, externalId, pluginId }, drawn);
    }, [api, reads, campaignId, channel, externalId, pluginId, drawn]);
    if (asked === null) return null;
    const line = senderLine(lookupFor(reads, asked, drawn), asked);
    // While the read is out the line's place keeps its height, so the acts below do not move when it arrives.
    if (line === null) return <SenderHold />;
    const words = senderWords(line);
    if (words === null) return null;
    return (
        <div className="sender-line">
            <div className="sender-now">
                {words.text}
                {words.code !== null && (
                    <>
                        {' '}
                        <code>{words.code}</code>
                    </>
                )}
                {words.message !== null && <> {words.message}</>}
            </div>
            {words.when && <div className="sender-when">{SENDER_WHEN}</div>}
        </div>
    );
}

/** An empty line's worth of height, held where a sender line will be drawn; it says nothing and is hidden from readers. */
export function SenderHold() {
    return (
        <div className="sender-line sender-hold" aria-hidden="true">
            <div className="sender-now">{' '}</div>
            <div className="sender-when">{' '}</div>
        </div>
    );
}
