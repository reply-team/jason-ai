import { can, cannotHere, type DesktopMember } from '../platform/places';
import { html, type Html } from './html';

/** The attributes of a control whose member this place cannot do: off, with the one sentence; nothing where it can. */
export const offUnless = (member: DesktopMember): Html | null =>
    can(member) ? null : html`disabled title="${cannotHere()}"`;
