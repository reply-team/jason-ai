namespace Jason.Runtime.Persistence;

/// <summary>
/// Which work items name a send — its cancels and its reads, each naming it by <c>context.input.send</c> — in one wording,
/// which every query that probes a send by it takes, and the index over the campaign, the operation and that expression.
/// </summary>
/// <remarks>
/// <para>
/// SQLite takes an expression index only for a query that writes the same expression, so a query that probes the send in
/// other words reads the campaign's work item by item again, and says nothing about it. The expression is therefore written
/// here once, and the index's own text is held to it by a test over a database migrated from nothing; a change of the
/// wording needs a migration that indexes the new one.
/// </para>
/// <para>
/// A send is named by its public id, a column of TEXT affinity, and the index's expression has none: SQLite seeks an index
/// column only by a comparison whose affinity matches it, so <see cref="Names"/> compares with the id under a unary plus,
/// which strips the affinity and lets the seek take all three columns. Without it the seek stops at the operation, and
/// every cancel and read of the campaign is read for the send it names.
/// </para>
/// <para>
/// The index is outside the model, as an expression is not a property: a later migration that rebuilds the work items'
/// table must create it again.
/// </para>
/// </remarks>
internal static class SendProbe
{
    /// <summary>The index over the campaign, the operation and the send an item names.</summary>
    internal const string Index = "ix_work_items_campaign_id_operation_send";

    /// <summary>The expression, over the work item aliased <c>{0}</c> followed by its dot, or over the bare column.</summary>
    private const string Expression = "json_extract({0}context_json, '$.input.send')";

    /// <summary>The expression as the index holds it, over the bare column.</summary>
    internal static readonly string Indexed = Of(item: null);

    /// <summary>The send the work item aliased <paramref name="item"/> names, or the bare column's where it is null.</summary>
    internal static string Of(string? item) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, Expression, item is null ? string.Empty : item + ".");

    /// <summary>
    /// Whether the work item aliased <paramref name="item"/> names the send whose public id is <paramref name="send"/>, an
    /// expression of the query: the one comparison every probe makes, the id under the unary plus.
    /// </summary>
    internal static string Names(string item, string send) => $"{Of(item)} = +{send}";
}
