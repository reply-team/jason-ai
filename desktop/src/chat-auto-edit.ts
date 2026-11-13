// Pure: the words of the six file commands Auto-edit runs with no card. A literal reading (`readLiteral`) is read here
// command by command: each must be one of `mkdir`, `touch`, `mv`, `cp`, `rm` and `rmdir`, named bare, with only the
// options listed for it, nothing piped in or redirected, and as many operands as it needs. Where its paths lie is the
// permission model's to decide (`autoEditJudgement`); this only says what the paths are. Anything else is no file
// command, and the command keeps Ask first's card.

import type { LiteralCommand } from './chat-literal.ts';

export type FileProgram = 'mkdir' | 'touch' | 'mv' | 'cp' | 'rm' | 'rmdir';

/** One file command of a reading: its program and the operands it acts on, as the shell passes them on. */
export interface FileCommand {
    program: FileProgram;
    operands: string[];
}

interface Grammar {
    /** The one-dash letters it takes, alone or in a cluster. */
    letters: string;
    /** The long options it takes, each spelled whole: no abbreviation counts. */
    long: readonly string[];
    /** How many operands it needs at least. */
    least: number;
}

/**
 * The options each program takes in Auto-edit, closed: a recursive or forced form, an interactive one, one that takes a
 * value, one that copies or follows a link, a mode and a target folder named apart are all outside it, and so a card.
 */
export const fileGrammars: Readonly<Record<FileProgram, Grammar>> = {
    mkdir: { letters: 'pv', long: ['--parents', '--verbose'], least: 1 },
    touch: { letters: 'c', long: ['--no-create'], least: 1 },
    mv: { letters: 'nv', long: ['--no-clobber', '--verbose'], least: 2 },
    cp: { letters: 'nvp', long: ['--no-clobber', '--verbose'], least: 2 },
    rm: { letters: 'dv', long: ['--dir', '--verbose'], least: 1 },
    rmdir: { letters: 'v', long: ['--verbose'], least: 1 },
};

function isFileProgram(word: string): word is FileProgram {
    return Object.hasOwn(fileGrammars, word);
}

/**
 * The file commands of a literal reading, in order; null when any command of it is not one: another program, a program
 * named by a folder, an extension or another case, standard input or a redirection, a folder of its own, an option
 * outside its list, a lone `-` or an empty word, or too few operands. Before `--` every word that begins with a dash is
 * an option wherever it stands, since GNU's tools take an option after an operand as well (`rm a -r`); after it, every
 * word is an operand.
 */
export function fileCommandsOf(commands: readonly LiteralCommand[]): FileCommand[] | null {
    const files: FileCommand[] = [];
    for (const command of commands) {
        const [program, ...rest] = command.words;
        if (program === undefined || !isFileProgram(program)) return null;
        if (command.input !== 'none' || command.targets.length > 0 || command.cwd !== null) return null;
        const grammar = fileGrammars[program];
        const operands: string[] = [];
        let options = true;
        for (const word of rest) {
            if (word === '') return null;
            if (options && word === '--') {
                options = false;
                continue;
            }
            if (options && word.startsWith('-')) {
                if (word === '-') return null;
                if (word.startsWith('--')) {
                    if (!grammar.long.includes(word)) return null;
                } else if (![...word.slice(1)].every((letter) => grammar.letters.includes(letter))) {
                    return null;
                }
                continue;
            }
            operands.push(word);
        }
        if (operands.length < grammar.least) return null;
        files.push({ program, operands });
    }
    return files.length === 0 ? null : files;
}
