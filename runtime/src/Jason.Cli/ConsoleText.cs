using System.Text;

namespace Jason.Cli;

/// <summary>
/// What the CLI writes to the console, held to what the console's code page makes of it. A Windows console on an OEM code
/// page writes some glyphs as a control byte (page 437 writes the right arrow as 0x1A, the left arrow as ESC, the eighth
/// note as CR), and a control byte acts on the terminal rather than reads. So a character outside ASCII that the output
/// encoding writes as a byte below 0x20 or as 0x7F is written as U+FFFD instead, as <c>RenderText.Shown</c> marks a
/// character that changes the line; every other character, ASCII's controls included, is written as it came. UTF-8, UTF-16
/// and UTF-32 write no character outside ASCII as a control code, though the last two write a zero byte in most of them,
/// so under each the console's writer is left as it is. The CLI does not change the console's code page: the setting
/// outlives the process and changes how the shell decodes every program after it.
/// </summary>
internal static class ConsoleText
{
    /// <summary>
    /// The console's writer, held to the encoding it writes through. The encoding is told by its code page, not its type:
    /// the console's own encoding is a type internal to .NET.
    /// </summary>
    public static TextWriter Guarded(TextWriter console, Encoding encoding) =>
        encoding.CodePage is 65001 or 1200 or 1201 or 12000 or 12001
            ? console
            : TextWriter.Synchronized(new GuardedWriter(console, encoding));

    private sealed class GuardedWriter(TextWriter console, Encoding encoding) : TextWriter
    {
        private readonly Dictionary<Rune, bool> _controls = [];

        /// <summary>The first half of a surrogate pair whose second half the next write brings.</summary>
        private char? _held;

        public override Encoding Encoding => console.Encoding;

        public override IFormatProvider FormatProvider => console.FormatProvider;

        public override void Write(char value) => Put([value]);

        public override void Write(string? value) => Put(value);

        public override void Write(char[] buffer, int index, int count) => Put(buffer.AsSpan(index, count));

        public override void Write(ReadOnlySpan<char> buffer) => Put(buffer);

        public override void Flush()
        {
            if (_held is { } held)
            {
                console.Write(held);
                _held = null;
            }

            console.Flush();
        }

        private void Put(ReadOnlySpan<char> text)
        {
            if (_held is null && !text.ContainsAnyExceptInRange('\0', '\u007F'))
            {
                console.Write(text);
                return;
            }

            var joined = new StringBuilder(text.Length + 1);
            if (_held is { } held)
            {
                joined.Append(held);
                _held = null;
            }

            joined.Append(text);
            if (joined.Length > 0 && char.IsHighSurrogate(joined[^1]))
            {
                _held = joined[^1];
                joined.Length--;
            }

            var written = new StringBuilder(joined.Length);
            foreach (var character in joined.ToString().EnumerateRunes())
            {
                written.Append(!character.IsAscii && WritesAControl(character) ? "\uFFFD" : character.ToString());
            }

            console.Write(written.ToString());
        }

        private bool WritesAControl(Rune character)
        {
            if (!_controls.TryGetValue(character, out var control))
            {
                control = encoding.GetBytes(character.ToString()).Any(written => written is < 0x20 or 0x7F);
                _controls[character] = control;
            }

            return control;
        }
    }
}
