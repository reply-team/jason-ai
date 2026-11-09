import { href, route } from '../router';
import { session } from '../api/session';
import { info } from './shell';
import { pendingMark } from './attention-mark';

export function Sidebar() {
    const current = route.value.screen;
    return (
        <aside className="sidebar">
            <div className="wordmark">
                <span className="glyph" />
                Jason
            </div>
            <nav className="nav" aria-label="Screens">
                <a className={current === 'attention' ? 'active' : ''} href={href({ screen: 'attention' })}>
                    Attention {pendingMark.value && <span className="dot" aria-label="something needs you" />}
                </a>
                <a
                    className={current === 'performance' ? 'active' : ''}
                    href={href({ screen: 'performance', tab: 'timeline', period: '7d', campaigns: [] })}
                >
                    Performance
                </a>
                <a
                    className={current === 'campaigns' ? 'active' : ''}
                    href={href({ screen: 'campaigns', tab: 'timeline', view: 'summary', period: '7d', filters: {} })}
                >
                    Campaigns
                </a>
                <a
                    className={current === 'workitems' ? 'active' : ''}
                    href={href({ screen: 'workitems', tab: 'now', filters: {} })}
                >
                    Work items
                </a>
                <a
                    className={current === 'people' ? 'active' : ''}
                    href={href({ screen: 'people', tab: 'timeline', filters: {} })}
                >
                    People
                </a>
                <a className={current === 'system' ? 'active' : ''} href={href({ screen: 'system' })}>
                    System
                </a>
            </nav>
            <div className="foot">
                {session.actorId.value} · Jason {info.value?.runtime_version ?? '…'}
            </div>
        </aside>
    );
}
