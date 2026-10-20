using System.Data.Common;
using Jason.Contracts.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jason.Runtime.Persistence;

/// <summary>
/// The key the runtime stores for a LinkedIn profile link, as a function the database calls, so that a migration re-keys
/// the links already stored by the rule itself rather than by a second copy of it in SQL, which could neither decode an
/// escape nor check the letters a profile's name is made of. A stored link is keyed as one
/// (<see cref="ChannelRules.KeyOfStored"/>): by the rule, with no bound on its length, because a rule before could store a
/// link longer than a value may be given, and it names its profile all the same. Null where the rule refuses the text.
/// </summary>
public static class LinkedInKeyFunction
{
    public const string FunctionName = "jason_linkedin_key";

    internal static string? Apply(string? value) => ChannelRules.KeyOfStored(ChannelRules.LinkedIn, value);
}

/// <summary>Registers <see cref="LinkedInKeyFunction"/> on every connection as it opens, so a migration's script can call it.</summary>
internal sealed class LinkedInKeyInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Register(connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Register(connection);
        return Task.CompletedTask;
    }

    private static void Register(DbConnection connection)
    {
        if (connection is SqliteConnection sqlite)
        {
            sqlite.CreateFunction<string?, string?>(LinkedInKeyFunction.FunctionName, LinkedInKeyFunction.Apply, isDeterministic: true);
        }
    }
}
