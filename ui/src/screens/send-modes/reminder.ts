// The quiet reminder about name and tone hides for seven days once hidden. The moment is kept in this browser's page
// storage and nowhere else; a browser that keeps nothing shows the reminder every time.

export const reminderKey = 'jason.ui.modes-voice-reminder-until';
export const hiddenDays = 7;

/** The moment, in milliseconds, until which a reminder hidden at `now` stays hidden. */
export const hiddenUntil = (now: Date): number => now.getTime() + hiddenDays * 24 * 60 * 60 * 1000;

/** Whether what the storage holds still hides the reminder at `now`; anything unreadable hides nothing. */
export function isHidden(stored: string | null, now: Date): boolean {
    if (stored === null) return false;
    const until = Number(stored);
    return Number.isFinite(until) && now.getTime() < until;
}

export function hiddenNow(now: Date): boolean {
    try {
        return isHidden(localStorage.getItem(reminderKey), now);
    } catch {
        return false;
    }
}

export function hide(now: Date): void {
    try {
        localStorage.setItem(reminderKey, String(hiddenUntil(now)));
    } catch {
        // A browser that keeps nothing shows the reminder again next time.
    }
}
