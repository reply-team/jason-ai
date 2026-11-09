using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Jason.App.Tests.Processes;

/// <summary>
/// Every way around <see cref="Jason.PluginHost.Sdk.FilesService"/> the guard has to see, in each shape the compiler
/// turns source into. Never called: they exist to be read.
/// </summary>
internal static class FileOffenders
{
    public static FileStream OpensAFile() => File.Open("x", FileMode.Append);

    public static FileStream OpensAFileToWrite() => File.OpenWrite("x");

    public static SafeFileHandle OpensAHandle() => File.OpenHandle("x", FileMode.Append, FileAccess.Write);

    public static FileStream CreatesAFile() => File.Create("x");

    public static StreamWriter CreatesAText() => File.CreateText("x");

    public static StreamWriter AppendsAText() => File.AppendText("x");

    public static void AppendsAllText() => File.AppendAllText("x", "y");

    public static void AppendsAllLines() => File.AppendAllLines("x", ["y"]);

    public static Task AppendsAllTextLater() => File.AppendAllTextAsync("x", "y");

    public static void WritesAllText() => File.WriteAllText("x", "y");

    public static void WritesAllBytes() => File.WriteAllBytes("x", [1]);

    public static void WritesAllLines() => File.WriteAllLines("x", ["y"]);

    public static Task WritesAllTextLater() => File.WriteAllTextAsync("x", "y");

    public static void MovesAFile() => File.Move("x", "y");

    public static void DeletesAFile() => File.Delete("x");

    public static void CopiesAFile() => File.Copy("x", "y");

    public static void ReplacesAFile() => File.Replace("x", "y", null);

    public static FileStream MakesAFileStream() => new("x", FileMode.Append);

    public static FileStream MakesAFileStreamTargetTyped()
    {
        FileStream stream = new(new SafeFileHandle(0, ownsHandle: false), FileAccess.Write);
        return stream;
    }

    public static StreamWriter MakesAWriterFromAPath() => new("x");

    public static StreamWriter MakesAWriterFromAPathAndAnEncoding() => new("x", append: true, Encoding.UTF8);

    public static FileStream OpensAFileInfo() => new FileInfo("x").Open(FileMode.Append);

    public static FileStream OpensAFileInfoToWrite() => new FileInfo("x").OpenWrite();

    public static FileStream CreatesThroughAFileInfo() => new FileInfo("x").Create();

    public static StreamWriter CreatesATextThroughAFileInfo() => new FileInfo("x").CreateText();

    public static void WritesAtAnOffset(SafeFileHandle handle) => RandomAccess.Write(handle, new byte[] { 1 }.AsSpan(), 0);

    public static DirectoryInfo CreatesADirectory() => Directory.CreateDirectory("x");

    public static void DeletesADirectory() => Directory.Delete("x");

    public static void MovesADirectory() => Directory.Move("x", "y");

    public static StreamWriter AppendsThroughAFileInfo() => new FileInfo("x").AppendText();

    public static void DeletesThroughAFileInfo() => new FileInfo("x").Delete();

    public static void MovesThroughAFileInfo() => new FileInfo("x").MoveTo("y");

    public static FileInfo CopiesThroughAFileInfo() => new FileInfo("x").CopyTo("y");

    public static FileInfo ReplacesThroughAFileInfo() => new FileInfo("x").Replace("y", null);

    public static void CreatesThroughADirectoryInfo() => new DirectoryInfo("x").Create();

    public static DirectoryInfo CreatesASubdirectory() => new DirectoryInfo("x").CreateSubdirectory("y");

    public static void DeletesThroughADirectoryInfo() => new DirectoryInfo("x").Delete();

    public static void DeletesATreeThroughADirectoryInfo() => new DirectoryInfo("x").Delete(recursive: true);

    public static void MovesThroughADirectoryInfo() => new DirectoryInfo("x").MoveTo("y");

    public static void LinksThroughADirectoryInfo() => new DirectoryInfo("x").CreateAsSymbolicLink("y");

    public static void LinksThroughAFileInfo() => new FileInfo("x").CreateAsSymbolicLink("y");

    public static ValueTask WritesAtAnOffsetLater(SafeFileHandle handle) => RandomAccess.WriteAsync(handle, new byte[] { 1 }.AsMemory(), 0);

    public static void SetsALength(SafeFileHandle handle) => RandomAccess.SetLength(handle, 0);

    public static FileSystemInfo LinksAFile() => File.CreateSymbolicLink("x", "y");

    public static FileSystemInfo LinksADirectory() => Directory.CreateSymbolicLink("x", "y");

    public static void SetsAttributes() => File.SetAttributes("x", FileAttributes.ReadOnly);

    [UnsupportedOSPlatform("windows")]
    public static void SetsAUnixMode() => File.SetUnixFileMode("x", UnixFileMode.UserRead);

    public static void SetsCreationOfAFile() => File.SetCreationTime("x", DateTime.UnixEpoch);

    public static void SetsCreationOfADirectory() => Directory.SetCreationTime("x", DateTime.UnixEpoch);

    public static void SetsCreationOfAFileInfo() => new FileInfo("x").CreationTime = DateTime.UnixEpoch;

    public static void SetsUtcCreationOfAFile() => File.SetCreationTimeUtc("x", DateTime.UnixEpoch);

    public static void SetsUtcCreationOfADirectory() => Directory.SetCreationTimeUtc("x", DateTime.UnixEpoch);

    public static void SetsUtcCreationOfAFileInfo() => new FileInfo("x").CreationTimeUtc = DateTime.UnixEpoch;

    public static void SetsLastWriteOfAFile() => File.SetLastWriteTime("x", DateTime.UnixEpoch);

    public static void SetsLastWriteOfADirectory() => Directory.SetLastWriteTime("x", DateTime.UnixEpoch);

    public static void SetsLastWriteOfAFileInfo() => new FileInfo("x").LastWriteTime = DateTime.UnixEpoch;

    public static void SetsUtcLastWriteOfAFile() => File.SetLastWriteTimeUtc("x", DateTime.UnixEpoch);

    public static void SetsUtcLastWriteOfADirectory() => Directory.SetLastWriteTimeUtc("x", DateTime.UnixEpoch);

    public static void SetsUtcLastWriteOfAFileInfo() => new FileInfo("x").LastWriteTimeUtc = DateTime.UnixEpoch;

    public static void SetsLastAccessOfAFile() => File.SetLastAccessTime("x", DateTime.UnixEpoch);

    public static void SetsLastAccessOfADirectory() => Directory.SetLastAccessTime("x", DateTime.UnixEpoch);

    public static void SetsLastAccessOfAFileInfo() => new FileInfo("x").LastAccessTime = DateTime.UnixEpoch;

    public static void SetsUtcLastAccessOfAFile() => File.SetLastAccessTimeUtc("x", DateTime.UnixEpoch);

    public static void SetsUtcLastAccessOfADirectory() => Directory.SetLastAccessTimeUtc("x", DateTime.UnixEpoch);

    public static void SetsUtcLastAccessOfAFileInfo() => new FileInfo("x").LastAccessTimeUtc = DateTime.UnixEpoch;

    public static void SetsTheAttributesOfAFileInfo() => new FileInfo("x").Attributes = FileAttributes.ReadOnly;

    public static void SetsTheAttributesOfADirectoryInfo() => new DirectoryInfo("x").Attributes = FileAttributes.Hidden;

    [UnsupportedOSPlatform("windows")]
    public static void SetsTheUnixModeOfAFileInfo() => new FileInfo("x").UnixFileMode = UnixFileMode.UserRead;

    [UnsupportedOSPlatform("windows")]
    public static void SetsTheUnixModeOfADirectoryInfo() => new DirectoryInfo("x").UnixFileMode = UnixFileMode.UserRead;

    [SupportedOSPlatform("windows")]
    public static void EncryptsAFile() => File.Encrypt("x");

    [SupportedOSPlatform("windows")]
    public static void DecryptsAFile() => File.Decrypt("x");

    [SupportedOSPlatform("windows")]
    public static void EncryptsThroughAFileInfo() => new FileInfo("x").Encrypt();

    [SupportedOSPlatform("windows")]
    public static void DecryptsThroughAFileInfo() => new FileInfo("x").Decrypt();

    public static void MakesAFileInfoReadOnly() => new FileInfo("x").IsReadOnly = true;

    public static MemoryMappedFile MapsAFile() => MemoryMappedFile.CreateFromFile("x");

    public static string MakesATemporaryFile() => Path.GetTempFileName();

    public static DirectoryInfo MakesATemporaryDirectory() => Directory.CreateTempSubdirectory();

    public static Action InALambda() => () => File.WriteAllText("x", "y");

    public static async Task InAnAsyncMethod()
    {
        await Task.Yield();
        File.Delete("x");
    }

    public static IEnumerable<FileStream> InAnIterator()
    {
        yield return new FileStream("x", FileMode.Append);
    }

    public static void InALocalFunction()
    {
        Remove();

        static void Remove() => Directory.Delete("x");
    }

    public static StreamWriter InAGenericMethod<T>(T name) => new(name?.ToString() ?? "x");

    public static Action<string> InAMethodGroup() => File.Delete;

    public static class InAStaticInitializer
    {
        public static readonly FileStream Stream = File.Create("x");
    }
}
