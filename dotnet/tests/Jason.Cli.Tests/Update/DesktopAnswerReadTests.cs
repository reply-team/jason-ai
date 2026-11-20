using Jason.Cli.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// The carried verb polls a request's file while the Desktop writes it. Its read shares write and delete, so a write of the
/// file in the moment the poll holds it is not refused: on Windows an open that does not share write makes a writer's open
/// fail ("If this flag is not specified, but the file or device has been opened for write access ..., the function fails",
/// CreateFileW, FILE_SHARE_WRITE), which is what a read through <see cref="File.ReadAllText(string)"/>, sharing read alone,
/// did to the writer.
/// </summary>
public sealed class DesktopAnswerReadTests : IDisposable
{
    private readonly TempPaths _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void While_the_verb_holds_the_file_it_can_be_written()
    {
        Directory.CreateDirectory(_dir.Paths.Root);
        var file = Path.Combine(_dir.Paths.Root, "desktop-cli-1.json");
        File.WriteAllText(file, """{"request":"cli-1","state":"downloaded"}""");

        using (DesktopAnswer.Open(file))
        {
            File.WriteAllText(file, """{"request":"cli-1","state":"installing"}""");
        }

        Assert.Equal(DesktopAnswer.Installing, DesktopAnswer.Read(file, "cli-1")!.State);
    }

    [Fact]
    public void Half_a_file_or_none_is_no_answer()
    {
        Directory.CreateDirectory(_dir.Paths.Root);
        var file = Path.Combine(_dir.Paths.Root, "desktop-cli-2.json");

        Assert.Null(DesktopAnswer.Read(file, "cli-2"));
        File.WriteAllText(file, """{"request":"cli-2","sta""");
        Assert.Null(DesktopAnswer.Read(file, "cli-2"));
    }
}
