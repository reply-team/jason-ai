using Jason.Contracts;
using Jason.Contracts.Discovery;
using Jason.Runtime.Persistence;
using Microsoft.Data.Sqlite;

namespace Jason.App.Tests.EndToEnd;

/// <summary>
/// A database a newer Jason migrated, met by the shipped program: the runtime <c>jason runtime start</c> launches refuses it
/// with <c>data_newer</c> before it writes anything, and the start answers that refusal as its own, which is what an agent
/// and the desktop branch on.
/// </summary>
public class NewerDatabaseStartRefusalTests
{
    [Fact]
    public async Task Runtime_start_answers_data_newer_and_nothing_is_started_or_written()
    {
        using var it = GoldenPath.Create("start-data-newer");
        new DatabaseMigrator(it.Paths).Migrate();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = it.Paths.DatabaseFile, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('29991231000000_FromANewerJason', '99.0.0');";
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        var database = await File.ReadAllBytesAsync(it.Paths.DatabaseFile, TestContext.Current.CancellationToken);

        var result = await GoldenPath.JasonAsync(it, "runtime", "start");
        if (result.ExitCode == 0)
        {
            await GoldenPath.StopAsync(it);
        }

        Assert.True(result.ExitCode == 1, $"The start answered {result.ExitCode}: {result.Output} {result.Error}");
        var error = GoldenPath.Json(result)["error"];
        Assert.Equal("data_newer", (string?)error?["code"]);
        Assert.Equal(DataNewer.Sentence(JasonVersion.Current, it.Root), (string?)error?["message"]);
        Assert.Null(GoldenPath.ReadDescriptor(it.Paths));
        Assert.False(Directory.Exists(it.Paths.LogsDirectory), $"The runtime wrote {it.Paths.LogsDirectory} before it refused.");
        Assert.False(Directory.Exists(it.Paths.BackupsDirectory), "The runtime took a backup before it refused.");
        Assert.Equal(database, await File.ReadAllBytesAsync(it.Paths.DatabaseFile, TestContext.Current.CancellationToken));
        Assert.Equal(["jason.db"], Directory.EnumerateFileSystemEntries(it.Paths.StateDirectory).Select(Path.GetFileName));
    }
}
