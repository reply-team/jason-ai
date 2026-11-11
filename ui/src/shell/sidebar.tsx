import { session } from '../api/session';
import { t } from '../platform/i18n';
import { registry, textOf } from '../platform/slots';
import { route } from '../router';
import { info } from './shell';

export function Sidebar() {
    const current = route.value.screen;
    return (
        <aside className="sidebar">
            <div className="wordmark">
                <span className="glyph" />
                Jason
            </div>
            <nav className="nav" aria-label={t('nav.aria')}>
                {registry.list('screen').map((item) => (
                    <a key={item.id} className={current === item.id ? 'active' : ''} href={item.href()}>
                        {textOf(item.label)} {item.marked?.() && <span className="dot" aria-label={t('nav.mark')} />}
                    </a>
                ))}
            </nav>
            <div className="foot">
                {session.actorId.value} · Jason {info.value?.runtime_version ?? '…'}
            </div>
        </aside>
    );
}
