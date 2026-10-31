namespace Jason.ReplyCliStub;

/// <summary>
/// The spelling of a direct send's reference reply-cli 0.18.0 takes (<c>REFERENCE</c> in its
/// <c>src/operations/direct-sends.ts</c>): the channel, a dash and Reply's own number for the send, with no leading zero
/// and of up to ten digits, where a LinkedIn send Reply holds for later is <c>linkedin-scheduled-</c> for its whole life.
/// <c>direct-send get</c> and <c>direct-send cancel</c> refuse any other as a usage error before they call Reply, so a
/// reference the package reads by is held to this pattern.
/// </summary>
public static class DirectSendReference
{
    public const string Pattern = @"^(email|linkedin|linkedin-scheduled)-[1-9]\d{0,9}$";
}
