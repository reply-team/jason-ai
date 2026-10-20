namespace Jason.Runtime.Hosting;

/// <summary>
/// This process does not compose text, so this runtime has not started (<see cref="TextComposition"/>). Its own type so
/// that it is said where the start was asked for, as every refused start is, before there is a log to read.
/// </summary>
public sealed class TextCompositionUnavailableException(string message) : Exception(message);
