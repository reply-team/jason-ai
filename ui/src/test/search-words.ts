import { fixtures } from './fixtures';

// What every place that shows an approval says of the recorded paid search (`approval.get/search-first`), line for
// line: its definition and its arguments as the runtime returns them, one statement for the four places' tests.

/** The operation's own sentence, which stands under the title as it always has. */
export const searchIntent = fixtures['approval.get/search-first'].response.preview.intent;

/** The search's lines under that sentence: each filter as written, what it leaves out, and its arguments. */
export const firstSearchLines = [
    'jobTitles: Head of Operations, Operations Director',
    'contactLocations: Portugal',
    'industries: Logistics',
    'companySizes: fifty, twoHundred',
    'Leaving out: jobTitles: Operations Intern · industries: Staffing and Recruiting',
    'count: 50 · people_per_company: 2 · include_without_email: false · find_phones: true',
];

/** The top-up's arguments line: its count and the audience it adds to, the options it was not given left out. */
export const topUpArguments = 'count: 20 · audience: 1';

/** An element's text with its runs of white space made one space, as a person reads it. */
const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();

/** The lines a place drew in its search block, each as a person reads it. */
export const linesOf = (root: Element, selector: string): string[] =>
    [...(root.querySelector(selector)?.children ?? [])].map((each) => words(each));
