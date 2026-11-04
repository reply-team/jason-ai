using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Profiles;

/// <summary>
/// At the runtime's start, how many profiles have a current revision this build's rules for its host refuse, said once as a
/// count. A host's rules are code and change only with the executable, so a start is the one moment a profile written under
/// an older build can newly be refused; each is held again at its launch, and <c>profile get</c> names what to change. The
/// count is all the log carries, since a profile's name is a person's text. Nothing here fails the start.
/// </summary>
public sealed class ProfileRevisionCheck(IServiceScopeFactory scopes, IEnumerable<IAgentHost> hosts, ILogger<ProfileRevisionCheck> logger) : IHostedService
{
    private static readonly Action<ILogger, int, Exception?> Refused = LoggerMessage.Define<int>(
        LogLevel.Warning,
        new EventId(1, nameof(Refused)),
        "Execution profiles whose current revision this build's rules for its host refuse: {Refused}; work that runs under them ends profile_revision_refused");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var current = await db.ExecutionProfiles.AsNoTracking()
                .Join(db.ExecutionProfileRevisions.AsNoTracking(), profile => new { profile.Id, Number = profile.CurrentRevision }, revision => new { Id = revision.ProfileId, revision.Number }, (profile, revision) => revision)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var count = current.Count(revision =>
                ProfileValidation.StoredProblems(revision, hosts.LastOrDefault(candidate => candidate.Kind == revision.Host)).Count > 0);
            if (count > 0)
            {
                Refused(logger, count, null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Nothing a reader would quote: the check is a courtesy, and a launch holds each profile again anyway.
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
