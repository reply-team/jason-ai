import { useEffect, useMemo } from 'react';
import { useApi } from '../../api/context';
import type { Approval } from '../../api/types';
import { lookupFor, readSender, senderMark, type SenderReads } from '../../console/senders';
import { SENDER_WHEN, senderAsked, senderLine, senderWords } from '../../console/senders-model';

/**
 * The sending name the provider holds now for the mailbox an approval's send names, under the address it goes from: one
 * piece, drawn on an agent's chat's card and on Needs you. Read when the card is drawn, once per campaign and channel in
 * a drawing; nothing while it is read, and nothing for an approval whose input names no sender. It decides nothing and
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
    const words = line === null ? null : senderWords(line);
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
