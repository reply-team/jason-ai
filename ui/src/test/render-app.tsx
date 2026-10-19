import { render } from '@testing-library/preact';
import { App } from '../app';
import { session } from '../api/session';
import { route } from '../router';
import { FakeApi } from './fake-api';

type Options = { paired?: boolean; actorId?: string; tokenId?: string; apiVersion?: string };

/** The app as a screen test wants it: paired (unless told otherwise), on Attention, with the shell's one call answered. */
export function renderApp(
    fake: FakeApi,
    { paired = true, actorId = 'ada', tokenId = 'uit_1', apiVersion = 'v1' }: Options = {},
) {
    localStorage.clear();
    location.hash = '';
    route.value = { screen: 'attention' };
    session.clear();
    if (paired) {
        localStorage.setItem(
            'jason.ui.session',
            JSON.stringify({ token: 'tok', token_id: tokenId, actor_id: actorId, instance_id: 'rt_1' }),
        );
    }
    fake.answer('system.info', 'system.info/with-ui');
    return render(<App api={fake} uiApiVersion={apiVersion} />);
}
