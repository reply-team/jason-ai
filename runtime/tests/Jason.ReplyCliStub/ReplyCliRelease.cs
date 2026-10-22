namespace Jason.ReplyCliStub;

/// <summary>
/// The reply-cli release this stand-in answers as, which is the oldest one the official package accepts: the first that
/// takes the contact options on the direct sends and on <c>contact update</c>. It is named once, here; the tests hold the
/// package's manifest, its README and its changelog to it, so moving the release is this line and the sentences they read.
/// </summary>
public static class ReplyCliRelease
{
    public const string Version = "0.15.0";
}
