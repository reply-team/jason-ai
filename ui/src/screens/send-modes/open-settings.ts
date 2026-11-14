import { here } from '../../console/state';
import { href } from '../../router';
import { voiceSectionId } from '../settings/voice-id';

/** Opens Settings at "Your name and tone": in the console's dock where it is mounted, by the address otherwise. */
export function openSettings(): void {
    try {
        here().dock.page({ screen: 'settings' });
    } catch {
        location.hash = href({ screen: 'settings' });
    }
    setTimeout(() => document.getElementById(voiceSectionId)?.scrollIntoView?.(), 0);
}
