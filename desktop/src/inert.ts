/**
 * Pure: a value shown in a dialog that a person, an agent or another program wrote (a folder from the installation's
 * settings or an environment variable, the executable a start at logon names, a file under the desktop's name), made
 * inert first. A control character or a character that turns the direction of text is written as its escape, so a
 * value never starts a line of its own or forges one; a value longer than the cap is cut, with an ellipsis.
 */

/** How long a value may be in a dialog. */
export const inertLimit = 300;

const named: Readonly<Record<string, string>> = { '\n': '\\n', '\r': '\\r', '\t': '\\t' };

export function inert(value: string, limit = inertLimit): string {
    const escaped = value.replace(/[\p{Cc}\p{Zl}\p{Zp}\p{Bidi_Control}]/gu, (character) => named[character] ?? `\\u${character.codePointAt(0)?.toString(16).padStart(4, '0')}`);
    return escaped.length <= limit ? escaped : `${escaped.slice(0, limit - 1)}…`;
}
