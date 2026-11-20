import { afterEach, describe, expect, it } from 'vitest';
import { showOwnPlace } from './own-place';

// A plain browser tab at the runtime's `/ui/`, with no web interface of the person's own installed, gets one sentence and
// nothing else: no link, no button, no Demo and no console.

afterEach(() => document.body.replaceChildren());

describe('the page in a plain browser tab', () => {
    it('draws one sentence and nothing to press or follow', () => {
        const element = document.createElement('div');
        element.innerHTML = '<p>left over</p>';
        document.body.append(element);

        showOwnPlace(element);

        expect(element.children).toHaveLength(1);
        expect(element.textContent).toBe(
            'This is the place for a web interface of your own. To build one, ask your agent how.',
        );
        expect(element.querySelectorAll('a, button, input, iframe, nav')).toHaveLength(0);
        expect(document.documentElement.lang).toBe('en');
    });
});
