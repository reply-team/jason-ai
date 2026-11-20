import { cleanup, configure } from '@testing-library/react';
import { afterEach, beforeEach } from 'vitest';
import { setPlace } from '../platform/places';
import { enrolReads } from '../console/enrol-route';
import { forgetEdits } from '../screens/attention/reasking';
import { takeUnanswered } from './fake-api';

// Times on the page are the person's clock; a test reads them in one zone on every machine.
process.env.TZ = 'UTC';

// A wait is for an element or a call, never a clock: testing-library's one second is a guess that a slow first render of the
// window, on a loaded or hosted machine, overruns. The deadline is generous and still fails with the wait's own message.
configure({ asyncUtilTimeout: 15_000 });

// Vitest globals are off, so testing-library's automatic cleanup is not; each screen test starts from an empty document.
// A request the test recorded no answer for fails it, even when the screen caught the error and showed a failure.
// A screen test runs in the desktop's own window unless it says otherwise; the page names its place as it starts.
beforeEach(() => setPlace('window'));

// A message edited on the page is the page's own memory, which a test starts without.
beforeEach(() => forgetEdits());

// Where each campaign's enrolment was last read is the page's own memory too: a test reads it afresh.
beforeEach(() => (enrolReads.value = {}));

afterEach(() => {
    cleanup();
    const unanswered = takeUnanswered();
    if (unanswered.length > 0)
        throw new Error(`unanswered: ${unanswered.map((c) => `${c.operation} ${JSON.stringify(c.body)}`).join(' ; ')}`);
});

// happy-dom has no IntersectionObserver; the cursor-list hook falls back to a "more" control a test can click.
