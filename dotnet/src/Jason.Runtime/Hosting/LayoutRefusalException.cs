using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Runtime.Hosting;

/// <summary>
/// The data directory's layout stamp names a layout newer than this build runs, or cannot be read, so this runtime has not
/// started and has written nothing (<see cref="Contracts.Discovery.LayoutStamp"/>). It is said in the envelope, with the
/// code, that <c>jason runtime start</c> refuses the same data directory with.
/// </summary>
public sealed class LayoutRefusalException(string code, string message) : Exception(message)
{
    /// <summary><c>layout_newer</c>, or <c>layout_unreadable</c>.</summary>
    public string Code { get; } = code;

    /// <summary>The refusal as one line of the envelope every refusal of this product carries.</summary>
    public string Envelope => JsonSerializer.Serialize(new ErrorResponse(new ErrorBody(Code, Message, Retryable: false)), JasonJson.Options);
}
