using Microsoft.AspNetCore.Http;

namespace Jason.Runtime.Ui;

/// <summary>Everything under <c>/ui</c> is the UI's: the files served and the pairing door. The bearer check exempts it and nothing else.</summary>
public static class UiPaths
{
    public const string Prefix = "/ui";

    public const string Door = "/ui/session";

    public static bool IsUi(PathString path) => path.StartsWithSegments(Prefix);
}
