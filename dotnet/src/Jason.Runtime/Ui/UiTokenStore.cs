using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Discovery;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Ui;

/// <summary>
/// The UI tokens this runtime accepts beside the run token. Hashes are read from the table once and kept in memory;
/// a mint or a revoke writes the table first and the memory second, so nothing is ever accepted that the table does
/// not hold. Last use is written at most once a minute per token.
/// </summary>
public sealed class UiTokenStore(IServiceScopeFactory scopes, TimeProvider clock, TokenRedactor redactor)
{
    public const string IdPrefix = "uit";

    public static readonly TimeSpan UseGranularity = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Known> _known = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _loading = new(1, 1);
    private volatile bool _loaded;

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<(string Token, UiTokenDto Issued)> IssueAsync(string issuedTo, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        var token = CapabilityToken.Generate();
        var hash = Hash(token);
        var now = clock.GetUtcNow();
        var row = new UiToken { PublicId = PublicId.New(IdPrefix), TokenHash = hash, IssuedTo = issuedTo, IssuedAt = now.UtcDateTime };
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            db.UiTokens.Add(row);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _known[hash] = new Known(row.PublicId, null);
        redactor.Add(token);
        return (token, new UiTokenDto(row.PublicId, issuedTo, now, null));
    }

    /// <summary>Whether a presented bearer is a live UI token. A match is a use, noted coarsely.</summary>
    public async Task<bool> MatchesAsync(string presented, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        var hash = Hash(presented);
        if (!_known.TryGetValue(hash, out var known))
        {
            return false;
        }

        var now = clock.GetUtcNow();
        if (known.LastWrittenUse is null || now - known.LastWrittenUse >= UseGranularity)
        {
            _known[hash] = known with { LastWrittenUse = now };
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            try
            {
                await db.UiTokens.Where(t => t.TokenHash == hash)
                    .ExecuteUpdateAsync(set => set.SetProperty(t => t.LastUsedAt, now.UtcDateTime), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is DbUpdateException or Microsoft.Data.Sqlite.SqliteException)
            {
                // The stamp is bookkeeping: a busy database costs it, never the request, and the next one tries again.
                _known[hash] = known;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the token a request was let in by is still accepted, by the hash <see cref="Hash"/> made of it: what a
    /// connection that outlives its request reads, without stamping a use. Only memory is read, and a revoke removes
    /// the hash from memory once the table has lost it.
    /// </summary>
    public bool IsLive(string hash) => _known.ContainsKey(hash);

    public async Task<IReadOnlyList<UiTokenDto>> ListAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var rows = await db.UiTokens.AsNoTracking().OrderBy(t => t.IssuedAt).ThenBy(t => t.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(t => new UiTokenDto(t.PublicId, t.IssuedTo, WorkItemMapper.Utc(t.IssuedAt), WorkItemMapper.Utc(t.LastUsedAt)))];
    }

    /// <summary>
    /// One token by id, or every one. The table first, then the memory, so a revoked token is refused from the next
    /// request on; "every one" is every one read here, so a token issued meanwhile is neither deleted nor forgotten.
    /// </summary>
    public async Task<int> RevokeAsync(string? tokenId, bool all, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        if (all)
        {
            var hashes = await db.UiTokens.Select(t => t.TokenHash).ToListAsync(cancellationToken).ConfigureAwait(false);
            var removed = await db.UiTokens.Where(t => hashes.Contains(t.TokenHash)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            foreach (var hash in hashes)
            {
                _known.TryRemove(hash, out _);
            }

            return removed;
        }

        var row = await db.UiTokens.FirstOrDefaultAsync(t => t.PublicId == tokenId, cancellationToken).ConfigureAwait(false)
            ?? throw DomainErrors.UiTokenNotFound(tokenId!);
        db.UiTokens.Remove(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _known.TryRemove(row.TokenHash, out _);
        return 1;
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        await _loading.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var rows = await db.UiTokens.AsNoTracking().Select(t => new { t.TokenHash, t.PublicId }).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                _known.TryAdd(row.TokenHash, new Known(row.PublicId, null));
            }

            _loaded = true;
        }
        finally
        {
            _loading.Release();
        }
    }

    private sealed record Known(string PublicId, DateTimeOffset? LastWrittenUse);
}
