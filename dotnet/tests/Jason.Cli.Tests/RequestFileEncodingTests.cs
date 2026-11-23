using System.Text;
using Jason.Cli;
using Jason.Cli.Tests.Commands;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests;

/// <summary>A file given to a verb is read as UTF-8, or as what its byte-order mark names, and is refused otherwise.</summary>
public class RequestFileEncodingTests
{
    private static readonly byte[] Windows1251Word = [0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2];

    [Fact]
    public async Task A_file_in_a_code_page_is_refused_naming_the_file_and_nothing_is_sent()
    {
        using var file = new BytesFile([.. "{\"first_name\":\""u8.ToArray(), .. Windows1251Word, .. "\"}"u8.ToArray()]);
        using var cli = new CliRun();

        var exit = await cli.RunAsync("contact", "create", "--file", file.Path);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Equal(0, cli.Requests);
        Assert.Contains(file.Path, cli.Error.ToString(), StringComparison.Ordinal);
        Assert.Contains("must be UTF-8", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_refusal_is_the_usage_error_that_names_the_file()
    {
        using var file = new BytesFile(Windows1251Word);

        var failure = await Assert.ThrowsAsync<UsageException>(() => RequestFile.ReadAsync(Env(), "--note-file", file.Path, namesOption: true, TestContext.Current.CancellationToken));

        Assert.Equal(RequestFile.NotUtf8(file.Path), failure.Message);
    }

    [Fact]
    public async Task A_utf8_file_without_a_mark_is_read_as_written()
    {
        using var file = new BytesFile(new UTF8Encoding(false).GetBytes("Привет"));

        var (text, _) = await RequestFile.ReadAsync(Env(), "--file", file.Path, namesOption: false, TestContext.Current.CancellationToken);

        Assert.Equal("Привет", text);
    }

    [Fact]
    public async Task A_utf8_file_with_a_mark_is_read_without_it()
    {
        using var file = new BytesFile([.. Encoding.UTF8.GetPreamble(), .. new UTF8Encoding(false).GetBytes("Привет")]);

        var (text, _) = await RequestFile.ReadAsync(Env(), "--file", file.Path, namesOption: false, TestContext.Current.CancellationToken);

        Assert.Equal("Привет", text);
    }

    [Fact]
    public async Task A_utf16_file_with_its_mark_is_still_read()
    {
        using var file = new BytesFile([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("Привет")]);

        var (text, _) = await RequestFile.ReadAsync(Env(), "--file", file.Path, namesOption: false, TestContext.Current.CancellationToken);

        Assert.Equal("Привет", text);
    }

    [Fact]
    public async Task A_utf16_file_with_a_lone_surrogate_is_refused()
    {
        using var lone = new BytesFile([.. Encoding.Unicode.GetPreamble(), 0x00, 0xDC]);

        await Assert.ThrowsAsync<UsageException>(() => RequestFile.ReadAsync(Env(), "--file", lone.Path, namesOption: false, TestContext.Current.CancellationToken));
    }

    private static CliEnvironment Env() =>
        new(TextWriter.Null, TextWriter.Null, new JasonPaths("unused", 1), In: TextReader.Null, Variables: new Dictionary<string, string>());

    private sealed class BytesFile : IDisposable
    {
        public BytesFile(byte[] bytes)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jason-cli-tests", Guid.NewGuid().ToString("N") + ".txt");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllBytes(Path, bytes);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
