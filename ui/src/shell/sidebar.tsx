import { href, route } from '../router';
import { session } from '../api/session';
import { info } from './shell';
import { pendingMark } from './attention-mark';

export function Sidebar() {
    const current = route.value.screen;
    return (
        <aside class="sidebar">
            <div class="wordmark">
                <span class="glyph" />
                Jason
            </div>
            <nav class="nav" aria-label="Screens">
                <a class={current === 'attention' ? 'active' : ''} href={href({ screen: 'attention' })}>
                    Attention {pendingMark.value && <span class="dot" aria-label="something needs you" />}
                </a>
                <a
                    class={current === 'performance' ? 'active' : ''}
                    href={href({ screen: 'performance', tab: 'timeline', period: '7d', campaigns: [] })}
                >
                    Performance
                </a>
                <a
                    class={current === 'campaigns' ? 'active' : ''}
                    href={href({ screen: 'campaigns', tab: 'timeline', view: 'summary', period: '7d', filters: {} })}
                >
                    Campaigns
                </a>
                <a
                    class={current === 'workitems' ? 'active' : ''}
                    href={href({ screen: 'workitems', tab: 'now', filters: {} })}
                >
                    Work items
                </a>
                <a
                    class={current === 'people' ? 'active' : ''}
                    href={href({ screen: 'people', tab: 'timeline', filters: {} })}
                >
                    People
                </a>
                <a class={current === 'system' ? 'active' : ''} href={href({ screen: 'system' })}>
                    System
                </a>
            </nav>
            <div class="foot">
                {session.actorId.value} · Jason {info.value?.runtime_version ?? '…'}
            </div>
        </aside>
    );
}
