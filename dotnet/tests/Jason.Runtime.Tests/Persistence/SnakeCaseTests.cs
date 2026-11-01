using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Persistence;

public class SnakeCaseTests
{
    [Theory]
    [InlineData("Campaigns", "campaigns")]
    [InlineData("PublicId", "public_id")]
    [InlineData("CreatedAt", "created_at")]
    [InlineData("PK_Campaigns", "pk_campaigns")]
    [InlineData("IX_Campaigns_PublicId", "ix_campaigns_public_id")]
    [InlineData("AwaitingApproval", "awaiting_approval")]
    [InlineData("already_snake", "already_snake")]
    public void Converts_pascal_case_to_snake_case(string input, string expected) =>
        Assert.Equal(expected, SnakeCase.Convert(input));
}
