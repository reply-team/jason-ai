namespace Jason.Cli.Installation;

/// <summary>The <c>jason</c> a terminal opened from now on finds by name. Not yet read: the rows come first.</summary>
internal static class NewTerminal
{
    public static string? FindsOnWindows(SetupPlaces places) => null;

    public static string? FindsOnMac(string? terminalPath, string localBin, bool inFront) => null;

    public static bool Runs(string? found, string? image) => false;

    public static string Problem(string found, string carried, bool commandLineInstall, PackageOwner? owner) => string.Empty;
}
