namespace Jason.Runtime.Tests;

/// <summary>
/// The default project every test database has. The migration that brought projects in inserts it as the first row of a
/// table it has just made, so its key is the same in every database; a test that seeds a campaign by hand names it so.
/// </summary>
public static class DefaultProject
{
    public const int Id = 1;
}
