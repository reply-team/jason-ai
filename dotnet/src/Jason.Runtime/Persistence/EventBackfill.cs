using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// A person's request to read a span of one account's events again: the source, the span, where its own reading stands,
/// and how it ended. It reads with a cursor of its own and never the source's, so asking for a span again moves nothing
/// the account's own reading depends on; what it finds lands on the store's keys, so a span read twice stores nothing
/// twice.
/// </summary>
/// <remarks>
/// A read of it under way is the reader's state, never this row's: a runtime that stops while one runs finds it pending,
/// and its successor reads on from the last page that landed.
/// </remarks>
public sealed class EventBackfill
{
    public int Id { get; set; }

    /// <summary><c>evb_</c> + ULID.</summary>
    public required string PublicId { get; set; }

    public int SourceId { get; set; }

    public EventSource? Source { get; set; }

    /// <summary>Every event at or after this moment, to the millisecond.</summary>
    public DateTime From { get; set; }

    /// <summary>And before this one.</summary>
    public DateTime To { get; set; }

    public EventBackfillStatus Status { get; set; }

    /// <summary>Where its next page starts, exactly as the plugin returned it with the last page stored; null before any.</summary>
    public string? Cursor { get; set; }

    /// <summary>How many reads of it in a row ended in a failure that may pass, each doubling its wait; a read that ends without one clears it.</summary>
    public int Failures { get; set; }

    /// <summary>When its last read started, on the runtime's clock; null before any.</summary>
    public DateTime? AttemptedAt { get; set; }

    public DateTime RequestedAt { get; set; }

    public ActorType ActorType { get; set; }

    public string? ActorId { get; set; }

    /// <summary>The person's words for asking, as the journal keeps them.</summary>
    public string? Reason { get; set; }

    /// <summary>Events its pages added to the store.</summary>
    public int Stored { get; set; }

    /// <summary>Events its pages found the store already held.</summary>
    public int Duplicates { get; set; }

    /// <summary>When it was read to the end, or ended in failure.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>The code its last read failed with: the one it ended with when it failed.</summary>
    public string? LastCode { get; set; }

    /// <summary>The class of that failure, as the contract's word.</summary>
    public string? LastClass { get; set; }
}
