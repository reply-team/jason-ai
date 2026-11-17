using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;

namespace Jason.Runtime.Hosting;

/// <summary>
/// The database holds a migration this build does not know: a newer Jason migrated it, so this runtime has not started and
/// has written nothing (<see cref="Persistence.NewerDatabase"/>). It is said in the envelope, with the code, that
/// <c>jason runtime start</c> answers as its own.
/// </summary>
public sealed class DataNewerRefusalException(string message) : Exception(message)
{
    /// <summary><c>data_newer</c>.</summary>
    public string Code => DataNewer.Code;

    /// <summary>The refusal as one line of the envelope every refusal of this product carries.</summary>
    public string Envelope => JsonSerializer.Serialize(new ErrorResponse(new ErrorBody(Code, Message, Retryable: false)), JasonJson.Options);
}
