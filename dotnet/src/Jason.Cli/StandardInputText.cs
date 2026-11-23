using System.Text;

namespace Jason.Cli;

/// <summary>
/// Standard input as this CLI reads it: redirected, as UTF-8 on every system; typed at a console, as the console gives it.
/// </summary>
/// <remarks>
/// <para>
/// The console's own reader decodes by the console's input code page, which on Windows is an OEM page — 437 or 866, never
/// UTF-8 unless somebody set it — so a person's words in another alphabet, handed over in a here-document or a pipe as
/// every shell an agent works in writes them, were stored garbled. A redirected standard input is therefore read through a
/// decoder of its own: UTF-8 always, whatever the console, the locale or the machine's language says; a UTF-8 byte-order
/// mark at the start is dropped; and any byte that is not UTF-8 — a code page's text, UTF-16, a sequence cut short — is
/// refused as a usage error (<see cref="Refusal"/>), before anything is sent, rather than stored as whatever it would
/// have been guessed to be.
/// </para>
/// <para>
/// A console that is not redirected is left as it is: what a person types there, the one answer an uninstall asks for
/// above all, is the console's to decode, and the CLI does not change the console's code page, a setting that outlives the
/// process (<see cref="ConsoleText"/>).
/// </para>
/// </remarks>
internal static class StandardInputText
{
    /// <summary>What a refusal says, as the usage error's message.</summary>
    public const string Refusal =
        "Standard input must be UTF-8, and what came in on it is not, so none of it was used. Send the text as UTF-8.";

    /// <summary>
    /// The reader this process's standard input is read through: <paramref name="open"/>'s stream as UTF-8 where it is
    /// redirected, <paramref name="console"/>'s reader otherwise. Each is asked for only when it is the one chosen.
    /// </summary>
    public static TextReader For(bool redirected, Func<Stream> open, Func<TextReader> console)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(console);
        return redirected ? Utf8(open()) : console();
    }

    /// <summary><paramref name="stream"/>, read as UTF-8: a leading mark dropped, any byte that is not UTF-8 refused.</summary>
    public static TextReader Utf8(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new Utf8Reader(stream);
    }

    private static UsageException Refused(DecoderFallbackException cause) => new(Refusal, cause);

    /// <summary>
    /// A strict UTF-8 reader whose every read turns the decoder's refusal into the usage error. The encoding carries the
    /// mark as its preamble, which is how the reader knows to drop one, and the mark of any other encoding is not looked
    /// for: UTF-16's is two bytes no UTF-8 text holds, and so it is refused with the rest.
    /// </summary>
    private sealed class Utf8Reader(Stream stream) : TextReader
    {
        private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

        private readonly StreamReader _reader = new(stream, Strict, detectEncodingFromByteOrderMarks: false);

        public override int Peek() => Decoded(_reader.Peek);

        public override int Read() => Decoded(_reader.Read);

        public override int Read(char[] buffer, int index, int count) => Decoded(() => _reader.Read(buffer, index, count));

        public override int Read(Span<char> buffer)
        {
            try
            {
                return _reader.Read(buffer);
            }
            catch (DecoderFallbackException refused)
            {
                throw Refused(refused);
            }
        }

        public override string? ReadLine() => Decoded(_reader.ReadLine);

        public override string ReadToEnd() => Decoded(_reader.ReadToEnd);

        public override Task<string> ReadToEndAsync() => ReadToEndAsync(CancellationToken.None);

        public override async Task<string> ReadToEndAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DecoderFallbackException refused)
            {
                throw Refused(refused);
            }
        }

        public override Task<string?> ReadLineAsync() => ReadLineAsync(CancellationToken.None).AsTask();

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DecoderFallbackException refused)
            {
                throw Refused(refused);
            }
        }

        public override Task<int> ReadAsync(char[] buffer, int index, int count) => ReadAsync(buffer.AsMemory(index, count)).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (DecoderFallbackException refused)
            {
                throw Refused(refused);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reader.Dispose();
            }

            base.Dispose(disposing);
        }

        private static T Decoded<T>(Func<T> read)
        {
            try
            {
                return read();
            }
            catch (DecoderFallbackException refused)
            {
                throw Refused(refused);
            }
        }
    }
}
