using Jason.Contracts.Harness;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Harness;

/// <summary>
/// Beside the runtime's start, the import file of each host that reads one is written into every folder the runtime has
/// recorded — the root where it carries its marker, each project's folder, each campaign's — where it is missing, so a
/// folder made before a host was supported is readable by that host without waiting for its next create.
/// </summary>
/// <remarks>
/// It writes only what <see cref="HarnessFolders.CompleteImportFiles"/> writes: a file that is there is left alone, never
/// opened, and no word of any <c>AGENTS.md</c> is read or written; nothing is deleted and <c>git</c> is never run. A folder
/// that is not there, no longer carries its marker, or refused the write, is skipped and said by its level and identifier alone, since a path
/// carries a name a person gave. Nothing here fails the start or holds it: the sweep runs off it, once the host has handed it
/// off, so the runtime answers while a folder on a slow or absent share is still being read, and a failure is
/// said once and leaves the runtime running. A stop ends the sweep between two folders.
/// </remarks>
public sealed class ImportFilesSweep(
    IServiceScopeFactory scopes,
    HarnessFolders folders,
    ILogger<ImportFilesSweep> logger,
    ImportFilesSweep.FolderCompletion? completeOne = null) : BackgroundService
{
    /// <summary>Completes one folder's import files, answering false for a folder skipped; <see cref="HarnessFolders.CompleteImportFiles"/> unless a test holds it.</summary>
    public delegate bool FolderCompletion(string folder, string level, string? id);

    private readonly FolderCompletion _completeOne = completeOne ?? HarnessFolders.CompleteImportFiles;

    private static readonly Action<ILogger, int, int, Exception?> Swept = LoggerMessage.Define<int, int>(
        LogLevel.Debug,
        new EventId(1, nameof(Swept)),
        "Import files swept: {Folders} recorded folders checked, {Skipped} skipped");

    private static readonly Action<ILogger, string, string?, Exception?> Skipped = LoggerMessage.Define<string, string?>(
        LogLevel.Information,
        new EventId(2, nameof(Skipped)),
        "The import-file sweep skipped a recorded {Level} folder that is not there, no longer carries its marker, or refused the write: {EntityId}");

    private static readonly Action<ILogger, Exception?> Failed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(3, nameof(Failed)),
        "The import-file sweep could not read the recorded folders; the runtime continues");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // On the pool by this method's own word, not only by the base class's: the database read alone can block, and
            // the start must never be the thread that waits on it.
            await Task.Run(() => SweepAsync(stoppingToken), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A stop, between two folders: a sweep that was stopped says nothing more.
        }
        catch (Exception)
        {
            // What a reader would quote is a path or a stored value; the line says that it happened, and the runtime goes on,
            // since a background service that ends in an exception stops the host.
            Failed(logger, null);
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var recorded = new List<(string Level, string? Id, string Folder)>();
        recorded.AddRange(
            (await db.Projects.AsNoTracking().Where(project => project.FolderPath != null).OrderBy(project => project.Id)
                .Select(project => new { project.PublicId, project.FolderPath }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(project => (HarnessMarker.Project, (string?)project.PublicId, project.FolderPath!)));
        recorded.AddRange(
            (await db.Campaigns.AsNoTracking().Where(campaign => campaign.FolderPath != null).OrderBy(campaign => campaign.Id)
                .Select(campaign => new { campaign.PublicId, campaign.FolderPath }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(campaign => (HarnessMarker.Campaign, (string?)campaign.PublicId, campaign.FolderPath!)));

        var checkedFolders = 0;
        var skipped = 0;
        if (folders.MarkedRoot() is { } root)
        {
            checkedFolders++;
            if (!_completeOne(root, HarnessMarker.Root, null))
            {
                skipped++;
                Skipped(logger, HarnessMarker.Root, null, null);
            }
        }

        foreach (var (level, id, folder) in recorded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            checkedFolders++;
            if (!_completeOne(folder, level, id))
            {
                skipped++;
                Skipped(logger, level, id, null);
            }
        }

        Swept(logger, checkedFolders, skipped, null);
    }
}
