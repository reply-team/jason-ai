using System.IO.MemoryMappedFiles;
using System.Reflection;
using Jason.PluginHost.Sdk;
using Jason.Runtime.Tests;

namespace Jason.App.Tests.Processes;

/// <summary>
/// Nothing in the plugin host writes a file but <see cref="FilesService"/>, the compiled assembly read: the rules a
/// plugin's line is held to stand in one place only while there is one place. This reads what is constructed and
/// called, never an argument's value, so a read that takes a mode it never uses to write still counts. Reflection, an
/// expression tree and <c>new T()</c> reach a constructor without naming it in IL, and are beyond what this reads.
/// </summary>
public class OnlyTheFileDoorWritesTests
{
    private static readonly Assembly Host = typeof(FilesService).Assembly;

    [Fact]
    [Critical(Family.B1, "the IL guard over the plugin host's writes")]
    public void No_method_of_the_plugin_host_writes_a_file_outside_the_door()
    {
        var offenders = IlReferences.Methods(Host)
            .Where(method => !IsTheDoor(method))
            .SelectMany(method => Forbidden(method).Select(reason => $"{Name(method)} {reason}"))
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0, "a file could be written past the door's rules: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// The reader finds the writes there are, which is what makes "found nothing" worth anything: a guard that read no
    /// method at all would pass the row above.
    /// </summary>
    [Fact]
    [Critical(Family.B1, "the guard reads the door it holds apart")]
    public void The_scan_finds_the_door_itself_writing()
    {
        var found = IlReferences.Methods(Host)
            .Where(IsTheDoor)
            .SelectMany(Forbidden)
            .ToList();

        Assert.Contains("makes a FileStream", found);
        Assert.Contains("calls Directory.CreateDirectory", found);
    }

    [Theory]
    [InlineData(nameof(FileOffenders.OpensAFile), "calls File.Open")]
    [InlineData(nameof(FileOffenders.OpensAFileToWrite), "calls File.OpenWrite")]
    [InlineData(nameof(FileOffenders.OpensAHandle), "calls File.OpenHandle")]
    [InlineData(nameof(FileOffenders.CreatesAFile), "calls File.Create")]
    [InlineData(nameof(FileOffenders.CreatesAText), "calls File.CreateText")]
    [InlineData(nameof(FileOffenders.AppendsAText), "calls File.AppendText")]
    [InlineData(nameof(FileOffenders.AppendsAllText), "calls File.AppendAllText")]
    [InlineData(nameof(FileOffenders.AppendsAllLines), "calls File.AppendAllLines")]
    [InlineData(nameof(FileOffenders.AppendsAllTextLater), "calls File.AppendAllTextAsync")]
    [InlineData(nameof(FileOffenders.WritesAllText), "calls File.WriteAllText")]
    [InlineData(nameof(FileOffenders.WritesAllBytes), "calls File.WriteAllBytes")]
    [InlineData(nameof(FileOffenders.WritesAllLines), "calls File.WriteAllLines")]
    [InlineData(nameof(FileOffenders.WritesAllTextLater), "calls File.WriteAllTextAsync")]
    [InlineData(nameof(FileOffenders.MovesAFile), "calls File.Move")]
    [InlineData(nameof(FileOffenders.DeletesAFile), "calls File.Delete")]
    [InlineData(nameof(FileOffenders.CopiesAFile), "calls File.Copy")]
    [InlineData(nameof(FileOffenders.ReplacesAFile), "calls File.Replace")]
    [InlineData(nameof(FileOffenders.MakesAFileStream), "makes a FileStream")]
    [InlineData(nameof(FileOffenders.MakesAFileStreamTargetTyped), "makes a FileStream")]
    [InlineData(nameof(FileOffenders.MakesAWriterFromAPath), "makes a StreamWriter from a path")]
    [InlineData(nameof(FileOffenders.MakesAWriterFromAPathAndAnEncoding), "makes a StreamWriter from a path")]
    [InlineData(nameof(FileOffenders.OpensAFileInfo), "calls FileInfo.Open")]
    [InlineData(nameof(FileOffenders.OpensAFileInfoToWrite), "calls FileInfo.OpenWrite")]
    [InlineData(nameof(FileOffenders.CreatesThroughAFileInfo), "calls FileInfo.Create")]
    [InlineData(nameof(FileOffenders.CreatesATextThroughAFileInfo), "calls FileInfo.CreateText")]
    [InlineData(nameof(FileOffenders.WritesAtAnOffset), "calls RandomAccess.Write")]
    [InlineData(nameof(FileOffenders.CreatesADirectory), "calls Directory.CreateDirectory")]
    [InlineData(nameof(FileOffenders.DeletesADirectory), "calls Directory.Delete")]
    [InlineData(nameof(FileOffenders.MovesADirectory), "calls Directory.Move")]
    [InlineData(nameof(FileOffenders.AppendsThroughAFileInfo), "calls FileInfo.AppendText")]
    [InlineData(nameof(FileOffenders.DeletesThroughAFileInfo), "calls FileSystemInfo.Delete")]
    [InlineData(nameof(FileOffenders.MovesThroughAFileInfo), "calls FileInfo.MoveTo")]
    [InlineData(nameof(FileOffenders.CopiesThroughAFileInfo), "calls FileInfo.CopyTo")]
    [InlineData(nameof(FileOffenders.ReplacesThroughAFileInfo), "calls FileInfo.Replace")]
    [InlineData(nameof(FileOffenders.CreatesThroughADirectoryInfo), "calls DirectoryInfo.Create")]
    [InlineData(nameof(FileOffenders.CreatesASubdirectory), "calls DirectoryInfo.CreateSubdirectory")]
    [InlineData(nameof(FileOffenders.DeletesThroughADirectoryInfo), "calls FileSystemInfo.Delete")]
    [InlineData(nameof(FileOffenders.DeletesATreeThroughADirectoryInfo), "calls DirectoryInfo.Delete")]
    [InlineData(nameof(FileOffenders.MovesThroughADirectoryInfo), "calls DirectoryInfo.MoveTo")]
    [InlineData(nameof(FileOffenders.LinksThroughADirectoryInfo), "calls FileSystemInfo.CreateAsSymbolicLink")]
    [InlineData(nameof(FileOffenders.LinksThroughAFileInfo), "calls FileSystemInfo.CreateAsSymbolicLink")]
    [InlineData(nameof(FileOffenders.WritesAtAnOffsetLater), "calls RandomAccess.WriteAsync")]
    [InlineData(nameof(FileOffenders.SetsALength), "calls RandomAccess.SetLength")]
    [InlineData(nameof(FileOffenders.LinksAFile), "calls File.CreateSymbolicLink")]
    [InlineData(nameof(FileOffenders.LinksADirectory), "calls Directory.CreateSymbolicLink")]
    [InlineData(nameof(FileOffenders.SetsAttributes), "calls File.SetAttributes")]
    [InlineData(nameof(FileOffenders.SetsAUnixMode), "calls File.SetUnixFileMode")]
    [InlineData(nameof(FileOffenders.SetsCreationOfAFile), "calls File.SetCreationTime")]
    [InlineData(nameof(FileOffenders.SetsCreationOfADirectory), "calls Directory.SetCreationTime")]
    [InlineData(nameof(FileOffenders.SetsCreationOfAFileInfo), "calls FileSystemInfo.set_CreationTime")]
    [InlineData(nameof(FileOffenders.SetsUtcCreationOfAFile), "calls File.SetCreationTimeUtc")]
    [InlineData(nameof(FileOffenders.SetsUtcCreationOfADirectory), "calls Directory.SetCreationTimeUtc")]
    [InlineData(nameof(FileOffenders.SetsUtcCreationOfAFileInfo), "calls FileSystemInfo.set_CreationTimeUtc")]
    [InlineData(nameof(FileOffenders.SetsLastWriteOfAFile), "calls File.SetLastWriteTime")]
    [InlineData(nameof(FileOffenders.SetsLastWriteOfADirectory), "calls Directory.SetLastWriteTime")]
    [InlineData(nameof(FileOffenders.SetsLastWriteOfAFileInfo), "calls FileSystemInfo.set_LastWriteTime")]
    [InlineData(nameof(FileOffenders.SetsUtcLastWriteOfAFile), "calls File.SetLastWriteTimeUtc")]
    [InlineData(nameof(FileOffenders.SetsUtcLastWriteOfADirectory), "calls Directory.SetLastWriteTimeUtc")]
    [InlineData(nameof(FileOffenders.SetsUtcLastWriteOfAFileInfo), "calls FileSystemInfo.set_LastWriteTimeUtc")]
    [InlineData(nameof(FileOffenders.SetsLastAccessOfAFile), "calls File.SetLastAccessTime")]
    [InlineData(nameof(FileOffenders.SetsLastAccessOfADirectory), "calls Directory.SetLastAccessTime")]
    [InlineData(nameof(FileOffenders.SetsLastAccessOfAFileInfo), "calls FileSystemInfo.set_LastAccessTime")]
    [InlineData(nameof(FileOffenders.SetsUtcLastAccessOfAFile), "calls File.SetLastAccessTimeUtc")]
    [InlineData(nameof(FileOffenders.SetsUtcLastAccessOfADirectory), "calls Directory.SetLastAccessTimeUtc")]
    [InlineData(nameof(FileOffenders.SetsUtcLastAccessOfAFileInfo), "calls FileSystemInfo.set_LastAccessTimeUtc")]
    [InlineData(nameof(FileOffenders.SetsTheAttributesOfAFileInfo), "calls FileSystemInfo.set_Attributes")]
    [InlineData(nameof(FileOffenders.SetsTheAttributesOfADirectoryInfo), "calls FileSystemInfo.set_Attributes")]
    [InlineData(nameof(FileOffenders.SetsTheUnixModeOfAFileInfo), "calls FileSystemInfo.set_UnixFileMode")]
    [InlineData(nameof(FileOffenders.SetsTheUnixModeOfADirectoryInfo), "calls FileSystemInfo.set_UnixFileMode")]
    [InlineData(nameof(FileOffenders.EncryptsAFile), "calls File.Encrypt")]
    [InlineData(nameof(FileOffenders.DecryptsAFile), "calls File.Decrypt")]
    [InlineData(nameof(FileOffenders.MapsAFile), "calls MemoryMappedFile.CreateFromFile")]
    [InlineData(nameof(FileOffenders.MakesATemporaryFile), "calls Path.GetTempFileName")]
    [InlineData(nameof(FileOffenders.MakesATemporaryDirectory), "calls Directory.CreateTempSubdirectory")]
    [InlineData(nameof(FileOffenders.InALambda), "calls File.WriteAllText")]
    [InlineData(nameof(FileOffenders.InAnAsyncMethod), "calls File.Delete")]
    [InlineData(nameof(FileOffenders.InAnIterator), "makes a FileStream")]
    [InlineData(nameof(FileOffenders.InALocalFunction), "calls Directory.Delete")]
    [InlineData(nameof(FileOffenders.InAGenericMethod), "makes a StreamWriter from a path")]
    [InlineData(nameof(FileOffenders.InAMethodGroup), "calls File.Delete")]
    [InlineData(nameof(FileOffenders.InAStaticInitializer), "calls File.Create")]
    public void The_guard_sees_every_way_around_the_door(string method, string reason)
    {
        // A lambda, an async method, an iterator and a local function are compiled into methods whose names carry the
        // source method's name, a static initializer is its type's .cctor, and a method group is an ldftn, not a call.
        var found = IlReferences.Methods(typeof(FileOffenders))
            .Where(candidate => Name(candidate).Contains(method, StringComparison.Ordinal))
            .SelectMany(Forbidden)
            .ToList();

        Assert.Contains(reason, found);
    }

    /// <summary>A read goes on being a read: the plugin host opens packages, and the rule is about writing.</summary>
    [Fact]
    public void The_guard_leaves_a_read_alone()
    {
        var read = typeof(ReadsOnly).GetMethod(nameof(ReadsOnly.ReadAll))!;

        Assert.Empty(Forbidden(read));
    }

    private static bool IsTheDoor(MethodBase method)
    {
        for (var type = method.DeclaringType; type is not null; type = type.DeclaringType)
        {
            if (type == typeof(FilesService))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> Forbidden(MethodBase method)
    {
        foreach (var called in IlReferences.Called(method))
        {
            var type = called.DeclaringType;
            var name = called.Name;
            if (called is ConstructorInfo)
            {
                if (type == typeof(FileStream))
                {
                    yield return "makes a FileStream";
                }
                else if (type == typeof(StreamWriter) && called.GetParameters() is [{ ParameterType: var first }, ..] && first == typeof(string))
                {
                    yield return "makes a StreamWriter from a path";
                }
            }
            else if ((type == typeof(File) && WritesThroughFile(name))
                || (type == typeof(FileInfo) && WritesThroughFileInfo(name))
                || (type == typeof(DirectoryInfo) && name is nameof(DirectoryInfo.Create) or nameof(DirectoryInfo.CreateSubdirectory) or nameof(DirectoryInfo.Delete) or nameof(DirectoryInfo.MoveTo))
                || (type == typeof(FileSystemInfo) && (name is nameof(FileSystemInfo.Delete) or nameof(FileSystemInfo.CreateAsSymbolicLink) || SetsMetadata(name)))
                || (type == typeof(RandomAccess) && name is nameof(RandomAccess.Write) or nameof(RandomAccess.WriteAsync) or nameof(RandomAccess.SetLength))
                || (type == typeof(Directory) && name is nameof(Directory.CreateDirectory) or nameof(Directory.Delete) or nameof(Directory.Move) or nameof(Directory.CreateSymbolicLink) or nameof(Directory.CreateTempSubdirectory) || SetsATime(name))
                || (type == typeof(MemoryMappedFile) && name == nameof(MemoryMappedFile.CreateFromFile))
                || (type == typeof(Path) && name == nameof(Path.GetTempFileName)))
            {
                yield return $"calls {type!.Name}.{name}";
            }
        }
    }

    private static bool WritesThroughFile(string name) =>
        name is nameof(File.Open) or nameof(File.OpenWrite) or nameof(File.OpenHandle)
            or nameof(File.Create) or nameof(File.CreateText) or nameof(File.AppendText)
            or nameof(File.Move) or nameof(File.Delete) or nameof(File.Copy) or nameof(File.Replace)
            or nameof(File.CreateSymbolicLink) or nameof(File.SetAttributes) or nameof(File.SetUnixFileMode)
            or nameof(File.Encrypt) or nameof(File.Decrypt)
        || SetsATime(name)
        || name.StartsWith("AppendAll", StringComparison.Ordinal)
        || name.StartsWith("WriteAll", StringComparison.Ordinal);

    /// <summary>
    /// <c>File.SetCreationTime</c>, <c>SetLastWriteTime</c> and <c>SetLastAccessTime</c> and the same three in UTC, which
    /// <see cref="File"/> and <see cref="Directory"/> both have: they change a file's metadata, and the door is the one
    /// place that may.
    /// </summary>
    private static bool SetsATime(string name) =>
        name is nameof(File.SetCreationTime) or nameof(File.SetCreationTimeUtc)
            or nameof(File.SetLastWriteTime) or nameof(File.SetLastWriteTimeUtc)
            or nameof(File.SetLastAccessTime) or nameof(File.SetLastAccessTimeUtc);

    /// <summary>
    /// The setters a <see cref="FileInfo"/> and a <see cref="DirectoryInfo"/> share, which compile to
    /// <see cref="FileSystemInfo"/>'s: the attributes, the Unix mode and the six timestamps.
    /// </summary>
    private static bool SetsMetadata(string name) =>
        name is "set_" + nameof(FileSystemInfo.Attributes) or "set_" + nameof(FileSystemInfo.UnixFileMode)
            or "set_" + nameof(FileSystemInfo.CreationTime) or "set_" + nameof(FileSystemInfo.CreationTimeUtc)
            or "set_" + nameof(FileSystemInfo.LastWriteTime) or "set_" + nameof(FileSystemInfo.LastWriteTimeUtc)
            or "set_" + nameof(FileSystemInfo.LastAccessTime) or "set_" + nameof(FileSystemInfo.LastAccessTimeUtc);

    /// <summary>A member a <see cref="FileInfo"/> and a <see cref="DirectoryInfo"/> share, <c>Delete</c> and the link, compiles to <see cref="FileSystemInfo"/>'s.</summary>
    private static bool WritesThroughFileInfo(string name) =>
        name.StartsWith("Open", StringComparison.Ordinal)
        || name.StartsWith("Create", StringComparison.Ordinal)
        || name is nameof(FileInfo.AppendText) or nameof(FileInfo.Delete) or nameof(FileInfo.MoveTo) or nameof(FileInfo.CopyTo) or nameof(FileInfo.Replace);

    private static string Name(MethodBase method) => $"{method.DeclaringType?.FullName}.{method.Name}";

    private static class ReadsOnly
    {
        public static string ReadAll() => File.ReadAllText("x") + File.Exists("y") + Directory.Exists("z");
    }
}
