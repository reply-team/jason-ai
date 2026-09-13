using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jason.Runtime.Persistence;

/// <summary>SQLite stores timestamps as text without a kind; everything in this database is UTC.</summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    toProvider => toProvider.Kind == DateTimeKind.Utc ? toProvider : toProvider.ToUniversalTime(),
    fromProvider => DateTime.SpecifyKind(fromProvider, DateTimeKind.Utc));
