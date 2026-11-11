/** Pure: when to look for an update next. The first failure waits a minute; each further one doubles, up to a day. */

export const updateCheckInterval = 6 * 60 * 60 * 1000;
const firstRetry = 60 * 1000;
const longestRetry = 24 * 60 * 60 * 1000;

export function nextCheckDelay(consecutiveFailures: number): number {
    if (consecutiveFailures <= 0) return updateCheckInterval;
    return Math.min(firstRetry * 2 ** (consecutiveFailures - 1), longestRetry);
}
