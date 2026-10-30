namespace Jason.ReplyCliStub;

/// <summary>
/// The spelling of a direct send's reference reply-cli 0.17.0 takes: the channel, a dash and Reply's own number for the
/// send, with no leading zero and of up to ten digits. <c>direct-send get</c> refuses any other as a usage error before it
/// calls Reply, so a reference the package reads by is held to this pattern.
/// </summary>
public static class DirectSendReference
{
    public const string Pattern = @"^(email|linkedin)-[1-9]\d{0,9}$";
}
