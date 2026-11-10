// The instruction text in the chat folder: the application's own text, written as the project memory file of both hosts
// the chat drives, CLAUDE.md for Claude Code and AGENTS.md for Codex, byte for byte. Only the chat folder gets them:
// never the harness root, nor a folder a turn adds, where the person's own memory files live.

import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

/** The memory files the hosts read from the folder they start in. */
export const memoryFiles = ['CLAUDE.md', 'AGENTS.md'] as const;

/** Writes each memory file that is missing or differs from the text; returns the names it wrote. */
export function placeInstructions(folder: string, text: string): string[] {
    const wanted = Buffer.from(text, 'utf8');
    mkdirSync(folder, { recursive: true });
    return memoryFiles.filter((name) => {
        const file = join(folder, name);
        if (holds(file, wanted)) return false;
        writeFileSync(file, wanted);
        return true;
    });
}

function holds(file: string, wanted: Buffer): boolean {
    try {
        return readFileSync(file).equals(wanted);
    } catch {
        return false;
    }
}
