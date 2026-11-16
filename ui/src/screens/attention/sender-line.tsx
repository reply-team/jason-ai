import { useEffect } from 'react';
import { useApi } from '../../api/context';
import type { Approval } from '../../api/types';
import { readSender, type SenderReads } from '../../console/senders';
import { SENDER_WHEN, senderAsked, senderKey, senderLine, senderWords } from '../../console/senders-model';

/**
 * The sending name the provider holds now for the mailbox an approval's send names, under the address it goes from: one
 * piece, drawn on an agent's chat's card and on Needs you. Read when the card is drawn, once per campaign and channel in
 * a drawing; nothing while it is read, and nothing for an approval whose input names no sender. It decides nothing and
 * holds nothing: Approve, Reject and their gate are the card's.
 */
export function SenderLine({ approval, reads }: { approval: Approval | undefined; reads: SenderReads }) {
    const api = useApi();
    const asked = senderAsked(approval);
    const campaignId = asked?.campaignId;
    const channel = asked?.channel;
    const externalId = asked?.externalId;
    useEffect(() => {
        if (campaignId === undefined || channel === undefined || externalId === undefined) return;
        void readSender(api, reads, { campaignId, channel, externalId });
    }, [api, reads, campaignId, channel, externalId]);
    if (asked === null) return null;
    const line = senderLine(reads.lookups.value[senderKey(asked)], asked.externalId);
    if (line === null) return null;
    const words = senderWords(line);
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
            </div>
            {words.when && <div className="sender-when">{SENDER_WHEN}</div>}
        </div>
    );
}
