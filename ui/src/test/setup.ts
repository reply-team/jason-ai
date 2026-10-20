import { cleanup } from '@testing-library/preact';
import { afterEach } from 'vitest';

// Times on the page are the person's clock; a test reads them in one zone on every machine.
process.env.TZ = 'UTC';

// Vitest globals are off, so testing-library's automatic cleanup is not; each screen test starts from an empty document.
afterEach(cleanup);

// happy-dom has no IntersectionObserver; the cursor-list hook falls back to a "more" control a test can click.
