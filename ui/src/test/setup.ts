import { cleanup } from '@testing-library/preact';
import { afterEach } from 'vitest';

// Vitest globals are off, so testing-library's automatic cleanup is not; each screen test starts from an empty document.
afterEach(cleanup);

// happy-dom has no IntersectionObserver; the cursor-list hook falls back to a "more" control a test can click.
