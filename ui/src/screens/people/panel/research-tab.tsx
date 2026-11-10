import { useEffect, useState, type FormEvent } from 'react';
import { ApiError, Unpaired } from '../../../api/client';
import { useApi } from '../../../api/context';
import { session } from '../../../api/session';
import type { Contact, WorkItem } from '../../../api/types';
import { Notice } from '../../../layout/notice';
import { href } from '../../../router';
import { shortDate } from '../../../time/format';
import * as panel from './store';

type Props = { person: Contact; campaignId: string | undefined; now: Date; zone: string };

type Said = { asked: WorkItem; campaign: string } | { refused: string };

/**
 * What a role produced, drawn as the text it is: each field of an object under its own key, a text as it stands,
 * anything else as JSON. Nothing in it is read as markup.
 */
function Result({ result }: { result: unknown }) {
    if (typeof result === 'string') return <p className="said">{result}</p>;
    if (result && typeof result === 'object' && !Array.isArray(result))
        return (
            <dl>
                {Object.entries(result).map(([key, value]) => (
                    <div key={key}>
                        <dt>{key}</dt>
                        <dd className="said">{typeof value === 'string' ? value : JSON.stringify(value, null, 2)}</dd>
                    </div>
                ))}
            </dl>
        );
    return <pre className="said">{JSON.stringify(result, null, 2)}</pre>;
}

/** Research: what the researcher role found about the person, and the ask for more in a campaign they are in. */
export function ResearchTab({ person, campaignId, now, zone }: Props) {
    const api = useApi();
    const [question, setQuestion] = useState('');
    const [campaign, setCampaign] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [said, setSaid] = useState<Said | null>(null);

    useEffect(() => {
        setQuestion('');
        setCampaign(null);
        setSaid(null);
        void panel.loadFindings(api, person.id);
    }, [api, person.id]);

    const live = panel.liveCampaigns(person);
    const chosen = campaign ?? panel.defaultCampaign(person, campaignId);
    const shown = panel.findings.value?.contactId === person.id ? panel.findings.value : null;

    async function ask(event: FormEvent) {
        event.preventDefault();
        if (busy || !chosen) return;
        setBusy(true);
        const name = live.find((each) => each.campaign_id === chosen)?.campaign_name ?? chosen;
        try {
            const asked = await panel.askResearcher(api, chosen, person.id, question.trim() || undefined);
            setQuestion('');
            setSaid({ asked, campaign: name });
        } catch (error) {
            if (error instanceof Unpaired) session.clear();
            setSaid({ refused: error instanceof ApiError ? error.message : String(error) });
        } finally {
            setBusy(false);
        }
    }

    return (
        <div className="panel-body research">
            {chosen && (
                <form className="ask" onSubmit={(event) => void ask(event)}>
                    <label htmlFor="research-question">Question (optional)</label>
                    <textarea
                        id="research-question"
                        className="textarea"
                        value={question}
                        onChange={(event) => setQuestion(event.target.value)}
                    />
                    <div className="where">
                        {live.length > 1 ? (
                            <select
                                className="select"
                                aria-label="The campaign the researcher works in"
                                value={chosen}
                                onChange={(event) => setCampaign(event.target.value)}
                            >
                                {live.map((each) => (
                                    <option key={each.campaign_id} value={each.campaign_id}>
                                        {each.campaign_name}
                                    </option>
                                ))}
                            </select>
                        ) : (
                            <span className="muted">in {live[0]?.campaign_name}</span>
                        )}
                        <button className="btn primary" type="submit" disabled={busy}>
                            Ask the researcher
                        </button>
                    </div>
                </form>
            )}
            {said && 'refused' in said && <Notice danger>Asking the researcher was refused: {said.refused}</Notice>}
            {said && 'asked' in said && (
                <Notice status>
                    Asked the researcher in {said.campaign}.{' '}
                    <a
                        href={href({
                            screen: 'workitems',
                            tab: said.asked.status === 'created' ? 'planned' : 'now',
                            id: said.asked.id,
                            filters: {},
                        })}
                    >
                        Open the work item
                    </a>
                </Notice>
            )}
            {panel.findingsFailure.value && <Notice danger>{panel.findingsFailure.value}</Notice>}
            {shown && shown.items.length === 0 && <div className="empty">Nothing found about this person yet.</div>}
            {shown?.items.map((item) => (
                <article key={item.id} className="finding">
                    <header className="muted">
                        {item.campaign_name}
                        {item.finished_at && ` · ${shortDate(new Date(item.finished_at), zone, now)}`}
                    </header>
                    <Result result={item.result} />
                </article>
            ))}
        </div>
    );
}
