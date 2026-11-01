namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// A directory of programs a test invents, to be handed to a <see cref="TestSearchPath"/>. Resolving a declared
/// executable must never depend on what the machine running the tests happens to have installed, and a test
/// about a file that cannot be started has to be able to create exactly that file.
/// </summary>
public sealed class TestPrograms : IDisposable
{
    public TestPrograms()
    {
        Root = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>A program the resolver accepts: an <c>.exe</c> on Windows, a file with the execute bit elsewhere.</summary>
    public string AddProgram(string name) => Add(name + (OperatingSystem.IsWindows() ? ".exe" : string.Empty), executable: true);

    /// <summary>A file with exactly this name, executable or not — for the cases a resolver must refuse.</summary>
    public string Add(string fileName, bool executable = false)
    {
        var file = Path.Combine(Root, fileName);
        File.WriteAllText(file, "not a real program");
        if (executable && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return file;
    }

    /// <summary>A file whose content is the point of the test, such as a shim the resolver has to read.</summary>
    public string Add(string fileName, string content)
    {
        var file = Path.Combine(Root, fileName);
        File.WriteAllText(file, content);
        return file;
    }

    /// <summary>Writes a file, creating the directories above it, and answers with its full path.</summary>
    public string AddNested(string relativePath, string content)
    {
        var file = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        return file;
    }

    /// <summary>
    /// A program that stands in for a language runtime — the referenced stand-in CLI's apphost under another
    /// name, with the files it needs to start beside it. Never the machine's own interpreter: CI installs no
    /// Node at all, and a test that reached for one would prove something about the agent rather than about
    /// the resolver.
    /// </summary>
    public string AddInterpreter(string fileName)
    {
        var target = Path.Combine(Root, fileName);
        File.Copy(FakeProviderCli.ExecutablePath, target, overwrite: true);

        // An apphost finds its own application beside itself, so the copy alone would not start.
        foreach (var extension in new[] { ".dll", ".runtimeconfig.json", ".deps.json" })
        {
            var companion = Path.Combine(FakeProviderCli.Directory, FakeProviderCli.ExecutableName + extension);
            if (File.Exists(companion))
            {
                File.Copy(companion, Path.Combine(Root, Path.GetFileName(companion)), overwrite: true);
            }
        }

        return target;
    }

    /// <summary>
    /// npm's own shim, as <c>npm install -g</c> writes it. The tests vary it one rule at a time, so a refusal
    /// is a refusal for the reason the test names rather than for an accident of the text.
    /// </summary>
    public static string NpmShim(string package, string entry) =>
        "@ECHO off\r\nGOTO start\r\n:find_dp0\r\nSET dp0=%~dp0\r\nEXIT /b\r\n:start\r\nSETLOCAL\r\nCALL :find_dp0\r\n\r\n"
        + "IF EXIST \"%dp0%\\node.exe\" (\r\n  SET \"_prog=%dp0%\\node.exe\"\r\n) ELSE (\r\n  SET \"_prog=node\"\r\n"
        + "  SET PATHEXT=%PATHEXT:;.JS;=;%\r\n)\r\n\r\nendLocal & goto #_undefined_# 2>NUL || title %COMSPEC% & "
        + "\"%_prog%\"  \"%dp0%\\node_modules\\" + package + "\\" + entry + "\" %*\r\n";

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
