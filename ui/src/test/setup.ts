import { cleanup } from '@testing-library/preact';
import { afterEach } from 'vitest';
import { takeUnanswered } from './fake-api';

// Times on the page are the person's clock; a test reads them in one zone on every machine.
process.env.TZ = 'UTC';

// Vitest globals are off, so testing-library's automatic cleanup is not; each screen test starts from an empty document.
// A request the test recorded no answer for fails it, even when the screen caught the error and showed a failure.
afterEach(() => {
    cleanup();
    const unanswered = takeUnanswered();
    if (unanswered.length > 0)
        throw new Error(`unanswered: ${unanswered.map((c) => `${c.operation} ${JSON.stringify(c.body)}`).join(' ; ')}`);
});

// happy-dom has no IntersectionObserver; the cursor-list hook falls back to a "more" control a test can click.
