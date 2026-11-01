using Jason.Cli.Doctor;

namespace Jason.Cli.Uninstall;

/// <summary>
/// A move of a directory this account is refused: an access-denied that no program holds open. On Windows it is one the
/// directory itself shows to be a permission, by refusing to be opened for its deletion; anywhere else the held-file wait
/// is never made, so every access-denied of the three package moves is read this way.
/// </summary>
/// <remarks>
/// An <see cref="IOException"/>, as the refusal it stands for was, so every step that reports that one reports this. It says
/// that waiting would not help, and does not say that no program holds the directory open. Its message is the clause alone:
/// the operating system's own, which carries an absolute path, is left out as <see cref="HeldOpen"/> leaves it out, and stays
/// the inner exception.
/// </remarks>
public sealed class AccessRefused(Exception refusal) : IOException(Clause, refusal)
{
    /// <summary>What every sentence about such a refusal says first.</summary>
    public const string Clause = "this account is refused access to it, so waiting would not help";

    /// <summary>The sentence for a move of <paramref name="path"/>, with the path written from the home directory: <c>~</c> and the rest, where it is inside it.</summary>
    public static string Said(string path, string home) =>
        $"'{IdentityRewriter.FromHome(path, home)}' could not be moved: {Clause}.";
}
