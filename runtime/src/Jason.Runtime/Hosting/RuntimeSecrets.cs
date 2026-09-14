namespace Jason.Runtime.Hosting;

/// <summary>
/// What this runtime knows that must never be written down anywhere else. Held as a service so the one piece of
/// code that redacts it does not have to be handed it by every caller.
/// </summary>
public sealed record RuntimeSecrets(string CapabilityToken);
