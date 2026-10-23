using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Projects;

namespace Jason.Runtime.Harness;

/// <summary>
/// The folder policy: the harness root, <c>projects/&lt;slug&gt;/</c> for a project and
/// <c>projects/&lt;slug&gt;/campaigns/&lt;leaf&gt;/</c> for a campaign, each level with its marker, a <c>CLAUDE.md</c> that
/// imports <c>AGENTS.md</c> and an empty <c>AGENTS.md</c>, the two written only where absent (<c>docs/projects.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// It writes structure only: a directory, a marker, and the two files where there are none. It never reads, writes or
/// deletes a word of any <c>AGENTS.md</c>, never writes over a <c>CLAUDE.md</c>, never deletes a folder or a file, never
/// runs <c>git</c> and never writes a <c>.gitignore</c>. The one thing it moves is a campaign's folder, by one rename, when
/// the campaign changes project.
/// </para>
/// <para>
/// A folder already at a level's path is never walked around: one with no marker is taken as the level's by writing its
/// marker, as the root is; one marked for anything else is refused, naming the path, for the person to rename or move.
/// The one exception is a campaign's leaf taken by another campaign this runtime holds, since campaign names are not
/// unique: that leaf is numbered. Whether something is there is always asked as a directory: a file where a folder
/// belongs is refused as being in the way, never taken for nothing.
/// </para>
/// <para>
/// What the disk refuses becomes a refusal of the request, said with the path and never with the platform's own message,
/// and no exception of the disk's reaches the log, where a path built from a name would carry the name.
/// </para>
/// </remarks>
public sealed class HarnessFolders(LiveSettings<HarnessOptions> settings, HarnessHome home)
{
    /// <summary>The directory below the root that holds one folder per project, and below a project one per campaign.</summary>
    public const string ProjectsDirectory = "projects";

    public const string CampaignsDirectory = "campaigns";

    /// <summary>What a level's <c>CLAUDE.md</c> holds when the runtime writes one: the import of the level's <c>AGENTS.md</c>, and nothing else.</summary>
    public const string ClaudeImport = "@AGENTS.md";

    /// <summary>A level's memory file, the person's own words: made empty where absent, and never opened.</summary>
    public const string MemoryFile = "AGENTS.md";

    /// <summary>How many numbered leaves are tried for one campaign before the disk is said to have none free.</summary>
    private const int MaxNumbered = 999;

    /// <summary>A create refused because the file is there, as Windows says it: <c>ERROR_FILE_EXISTS</c>.</summary>
    private const int WindowsFileExists = unchecked((int)0x80070050);

    /// <summary>The same, as Windows also says it: <c>ERROR_ALREADY_EXISTS</c>.</summary>
    private const int WindowsAlreadyExists = unchecked((int)0x800700B7);

    /// <summary>The same elsewhere: <c>EEXIST</c>, which .NET carries as the exception's <c>HResult</c>.</summary>
    private const int UnixExists = 17;

    /// <summary>What stands at a level's path, as far as whose it is.</summary>
    private enum Standing
    {
        /// <summary>Nothing of that name.</summary>
        Absent,

        /// <summary>A folder with no marker: somebody's own, taken as the level's by writing its marker.</summary>
        Unmarked,

        /// <summary>A folder whose marker names this very level and entity.</summary>
        Own,

        /// <summary>A folder whose marker names something else, or cannot be read.</summary>
        Marked,
    }

    /// <summary>The root as the settings in force name it, as a full path.</summary>
    public string Root
    {
        get
        {
            if (!settings.TryCurrent(out var options))
            {
                throw DomainErrors.SettingsUnreadable(HarnessOptions.Section);
            }

            return HarnessRoot.Of(options, home.Directory);
        }
    }

    /// <summary>
    /// The root as the settings in force name it, when it is there with its root marker; null when it is not — nothing
    /// made it yet, the folder there has no marker or one of anything else, the disk will not say, or no settings were
    /// ever in force. Only the marker is read: nothing is made, taken or written, and no memory file is opened. No record
    /// holds the root, so its marker is the one way to know the folder is Jason's.
    /// </summary>
    public string? MarkedRoot()
    {
        if (!settings.TryCurrent(out var options))
        {
            return null;
        }

        var root = HarnessRoot.Of(options, home.Directory);
        try
        {
            return HarnessMarker.In(root)?.Names(HarnessMarker.Root, null) == true ? root : null;
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Makes a project's folder, <c>projects/&lt;slug&gt;/</c> under the root — the root first, where there is none — and
    /// answers its full path. A folder already there with no marker becomes the project's; one marked for anything else
    /// is refused as taken, with nothing written.
    /// </summary>
    public string MakeProjectFolder(string projectId, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var root = Root;
        EnsureRoot(root);
        var projects = Path.Combine(root, ProjectsDirectory);
        var folder = Path.Combine(projects, slug);
        var mine = new HarnessMarker(HarnessMarker.Project, projectId);
        return Disk(folder, () =>
        {
            EnsureDirectory(projects);
            var (standing, _) = Look(folder, mine);
            if (standing == Standing.Marked || !Claim(folder, mine, standing))
            {
                throw DomainErrors.HarnessFolderTaken(folder);
            }

            return folder;
        });
    }

    /// <summary>
    /// Makes a campaign's folder in its project's folder and answers its full path. The leaf is made from the campaign's
    /// name as a project's slug is; a leaf another campaign this runtime holds has is followed by <c>-2</c>, <c>-3</c>. A
    /// folder there with no marker becomes the campaign's; one marked as another level, or for an id no campaign here
    /// holds, is refused as taken. Only the path is recorded.
    /// </summary>
    /// <param name="held">Whether a campaign of that id is one this runtime holds.</param>
    public async Task<string> MakeCampaignFolderAsync(string projectFolder, string campaignId, string name, Func<string, Task<bool>> held)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(held);

        var campaigns = Path.Combine(projectFolder, CampaignsDirectory);
        var mine = new HarnessMarker(HarnessMarker.Campaign, campaignId);
        var leaf = ProjectSlug.Leaf(name, HarnessMarker.Campaign);
        Disk(campaigns, () => EnsureDirectory(campaigns));
        foreach (var candidate in Names(leaf))
        {
            var folder = Path.Combine(campaigns, candidate);
            var (standing, found) = Disk(folder, () => Look(folder, mine));
            if (standing == Standing.Marked)
            {
                if (await AnotherCampaignAsync(found, held).ConfigureAwait(false))
                {
                    continue;
                }

                throw DomainErrors.HarnessFolderTaken(folder);
            }

            if (!Disk(folder, () => Claim(folder, mine, standing)))
            {
                throw DomainErrors.HarnessFolderTaken(folder);
            }

            return folder;
        }

        throw DomainErrors.HarnessFolderUnavailable(Path.Combine(campaigns, leaf));
    }

    /// <summary>
    /// The folder recorded for a project, when it is still there and still carries the project's marker; refused as
    /// missing otherwise, and as in the way where a file stands at its path. A folder the person moved is found again
    /// only when they say where it went.
    /// </summary>
    public string RequireProjectFolder(string projectId, string recorded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recorded);
        Require(recorded, HarnessMarker.Project, projectId);
        return recorded;
    }

    /// <summary>Where the policy puts a project's folder, <c>projects/&lt;slug&gt;/</c> under the root as the settings in force name it, with nothing made.</summary>
    public string ProjectFolderOf(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return Path.Combine(Root, ProjectsDirectory, slug);
    }

    /// <summary>
    /// The checks of a campaign's move into <paramref name="projectFolder"/> that leave nothing behind, made before
    /// anything is written, and the name the folder will have there. The campaign's folder is where it was recorded with
    /// its marker; nothing stands where the project's <c>campaigns/</c> belongs but a folder; the leaf stays as it is, or
    /// takes <c>-2</c>, <c>-3</c> where another campaign this runtime holds has it there, and any other folder at the
    /// leaf is refused as taken, since a rename never lands on a folder that is there; and the two are on one volume, as
    /// the paths say, which needs no folder made. The project's folder itself need not be there yet.
    /// </summary>
    /// <param name="held">Whether a campaign of that id is one this runtime holds.</param>
    public async Task<string> PlanCampaignMoveAsync(string campaignFolder, string campaignId, string projectFolder, Func<string, Task<bool>> held)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(held);

        Require(campaignFolder, HarnessMarker.Campaign, campaignId);
        var campaigns = Path.Combine(projectFolder, CampaignsDirectory);
        if (Disk(campaigns, () => File.Exists(campaigns)))
        {
            throw DomainErrors.HarnessFolderUnavailable(campaigns);
        }

        var mine = new HarnessMarker(HarnessMarker.Campaign, campaignId);
        var leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(campaignFolder));
        foreach (var candidate in Names(leaf))
        {
            var target = Path.Combine(campaigns, candidate);
            var (standing, found) = Disk(target, () => Look(target, mine));
            if (standing != Standing.Absent)
            {
                if (standing == Standing.Marked && await AnotherCampaignAsync(found, held).ConfigureAwait(false))
                {
                    continue;
                }

                throw DomainErrors.HarnessMoveTargetTaken(target);
            }

            if (!string.Equals(Path.GetPathRoot(Path.GetFullPath(campaignFolder)), Path.GetPathRoot(Path.GetFullPath(target)), HarnessRoot.Comparison))
            {
                throw DomainErrors.HarnessFolderNotMoved(campaignFolder, campaigns);
            }

            return candidate;
        }

        throw DomainErrors.HarnessFolderNotMoved(campaignFolder, campaigns);
    }

    /// <summary>
    /// Moves a campaign's folder into <paramref name="projectFolder"/>'s <c>campaigns/</c> under the name its plan chose,
    /// by one rename, and answers where it went: <c>campaigns/</c> is made where it is absent — the project's own
    /// structure, which stays whatever the rename meets — and then the folder is renamed. A folder the rename cannot
    /// move — held open, worked in, on another volume after all — refuses the move, and nothing of the campaign's is
    /// copied, deleted or left half moved.
    /// </summary>
    public string MoveCampaignFolder(string campaignFolder, string projectFolder, string leaf)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(campaignFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaf);

        var campaigns = Path.Combine(projectFolder, CampaignsDirectory);
        var target = Path.Combine(campaigns, leaf);
        Disk(campaigns, () => EnsureDirectory(campaigns));
        if (Disk(target, () => File.Exists(target) || Directory.Exists(target)))
        {
            throw DomainErrors.HarnessMoveTargetTaken(target);
        }

        try
        {
            Directory.Move(campaignFolder, target);
            return target;
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            throw DomainErrors.HarnessFolderNotMoved(campaignFolder, campaigns);
        }
    }

    /// <summary>
    /// Puts back a move whose update could not be saved, so the folder is where the database still says it is. One rename
    /// back; a folder that cannot be put back is left where the move put it, and the refusal that follows says so no less.
    /// </summary>
    public static void MoveBack(string moved, string recorded)
    {
        try
        {
            Directory.Move(moved, recorded);
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The root as a level of the policy: made with its marker and the two files where there is no folder; an existing
    /// folder with no marker adopted by writing its marker and the two files where absent, never a word of what is in
    /// it; one whose marker names anything but a root refused, with nothing written. A root that has its marker is used as
    /// it is: what the person did with its files since is theirs.
    /// </summary>
    private static void EnsureRoot(string root)
    {
        var mine = new HarnessMarker(HarnessMarker.Root, null);
        Disk(root, () =>
        {
            var (standing, _) = Look(root, mine);
            if (standing == Standing.Own)
            {
                return;
            }

            if (standing == Standing.Marked || !Claim(root, mine, standing))
            {
                throw DomainErrors.HarnessRootTaken(root);
            }
        });
    }

    /// <summary>
    /// What stands at <paramref name="folder"/>: nothing, a folder with no marker, one with <paramref name="mine"/>, or one
    /// marked for something else, with the marker found. A file there is in the way, and refused as such.
    /// </summary>
    private static (Standing Standing, HarnessMarker? Found) Look(string folder, HarnessMarker mine)
    {
        if (File.Exists(folder))
        {
            throw DomainErrors.HarnessFolderUnavailable(folder);
        }

        if (!Directory.Exists(folder))
        {
            return (Standing.Absent, null);
        }

        var found = HarnessMarker.In(folder);
        return found is null ? (Standing.Unmarked, null)
            : found.Names(mine.Kind!, mine.Id) ? (Standing.Own, found)
            : (Standing.Marked, found);
    }

    /// <summary>
    /// Takes <paramref name="folder"/> as a level: makes it where it is absent, writes its marker where it has none, and
    /// gives it the two files where absent. The marker comes first, so a folder whose marker of something else turned out
    /// to be there first is refused with nothing written in it, and the answer is then false. A step that fails after the
    /// marker leaves the folder marked for the id it was made for: the same level's next attempt finishes it, and a create
    /// taken back leaves it for the next one that reaches its path to name.
    /// </summary>
    private static bool Claim(string folder, HarnessMarker mine, Standing standing)
    {
        if (standing != Standing.Own)
        {
            Directory.CreateDirectory(folder);
            if (!TryMark(folder, mine) && HarnessMarker.In(folder)?.Names(mine.Kind!, mine.Id) != true)
            {
                return false;
            }
        }

        WriteFilesIfAbsent(folder);
        return true;
    }

    /// <summary>Whether a marker found at a campaign's leaf names another campaign this runtime holds, which numbers the leaf rather than refusing it.</summary>
    private static async Task<bool> AnotherCampaignAsync(HarnessMarker? found, Func<string, Task<bool>> held) =>
        found is { Kind: HarnessMarker.Campaign, Id: { } id } && await held(id).ConfigureAwait(false);

    /// <summary>
    /// That <paramref name="folder"/> is there as a folder and carries the marker of <paramref name="kind"/> and
    /// <paramref name="id"/>; refused as missing otherwise, and as in the way where a file stands at its path.
    /// </summary>
    private static void Require(string folder, string kind, string id)
    {
        var (standing, _) = Disk(folder, () => Look(folder, new HarnessMarker(kind, id)));
        if (standing != Standing.Own)
        {
            throw kind == HarnessMarker.Campaign ? DomainErrors.HarnessCampaignFolderMissing(id, folder) : DomainErrors.HarnessFolderMissing(id, folder);
        }
    }

    /// <summary>The name, then the name with <c>-2</c>, <c>-3</c> and so on after it.</summary>
    private static IEnumerable<string> Names(string leaf)
    {
        yield return leaf;
        for (var number = 2; number <= MaxNumbered; number++)
        {
            yield return ProjectSlug.Numbered(leaf, number);
        }
    }

    /// <summary>A plain directory of the policy's — <c>projects/</c>, <c>campaigns/</c> — which has no marker and no files of its own.</summary>
    private static void EnsureDirectory(string directory)
    {
        if (File.Exists(directory))
        {
            throw DomainErrors.HarnessFolderUnavailable(directory);
        }

        Directory.CreateDirectory(directory);
    }

    /// <summary>Writes the marker as a new file; false when one is there already, which is then left exactly as it is.</summary>
    private static bool TryMark(string folder, HarnessMarker marker)
    {
        Directory.CreateDirectory(Path.Combine(folder, HarnessMarker.Directory));
        return TryCreate(HarnessMarker.PathIn(folder), marker.Bytes());
    }

    /// <summary>
    /// <c>CLAUDE.md</c> holding exactly <see cref="ClaudeImport"/> and an empty <c>AGENTS.md</c>, each only where nothing of
    /// that name is there: an existing one is never opened, read or written.
    /// </summary>
    private static void WriteFilesIfAbsent(string folder)
    {
        TryCreate(Path.Combine(folder, "CLAUDE.md"), System.Text.Encoding.UTF8.GetBytes(ClaudeImport));
        TryCreate(Path.Combine(folder, MemoryFile), []);
    }

    /// <summary>
    /// Creates a file that is not there. One that is — a file or anything else of that name — is left alone and never
    /// opened: whether it is there is asked of its name, and the create that settles a race fails without opening it.
    /// Only that failure means the file is there; any other the disk gives — a file held open, a write that stops
    /// halfway — is the disk's refusal, and reaches the request as one.
    /// </summary>
    private static bool TryCreate(string path, byte[] content)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            return false;
        }

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException refused) when (AlreadyThere(refused))
        {
            return false;
        }

        using (stream)
        {
            stream.Write(content);
        }

        return true;
    }

    /// <summary>
    /// Whether a create of a new file was refused because something of that name was there first, as each platform says
    /// it: <c>ERROR_FILE_EXISTS</c> or <c>ERROR_ALREADY_EXISTS</c> on Windows, <c>EEXIST</c> elsewhere.
    /// </summary>
    private static bool AlreadyThere(IOException refused) =>
        OperatingSystem.IsWindows()
            ? refused.HResult is WindowsFileExists or WindowsAlreadyExists
            : refused.HResult == UnixExists;

    /// <summary>Runs an act on the disk, turning what the disk refuses into the request's refusal for <paramref name="folder"/>.</summary>
    private static T Disk<T>(string folder, Func<T> act)
    {
        try
        {
            return act();
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            throw DomainErrors.HarnessFolderUnavailable(folder);
        }
    }

    private static void Disk(string folder, Action act) => Disk<bool>(folder, () =>
    {
        act();
        return true;
    });
}
