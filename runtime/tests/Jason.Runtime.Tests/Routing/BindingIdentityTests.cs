using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Runtime.Routing;

namespace Jason.Runtime.Tests.Routing;

/// <summary>
/// An attempt records which account it ran against as a hash rather than a copy: the same identity across two
/// attempts is what answers "was this retried somewhere else?", the value itself is already in the journal and
/// in <c>route.list</c>, and the attempt row stays small and free of anything a vendor might later call
/// sensitive. Two spellings of the same binding must therefore agree, and two different bindings must not.
/// </summary>
public class BindingIdentityTests
{
    [Fact]
    public void No_binding_has_no_identity_rather_than_the_identity_of_an_empty_one()
    {
        Assert.Null(BindingIdentity.Of(null));
        Assert.NotNull(BindingIdentity.Of([]));
    }

    [Fact]
    public void Two_bindings_that_differ_only_in_key_order_are_the_same_binding()
    {
        var one = new JsonObject { ["workspace"] = "west", ["mailbox"] = "sales" };
        var other = new JsonObject { ["mailbox"] = "sales", ["workspace"] = "west" };

        Assert.Equal(BindingIdentity.Of(one), BindingIdentity.Of(other));
    }

    [Fact]
    public void Key_order_is_ignored_at_every_depth_and_never_inside_an_array()
    {
        var one = new JsonObject
        {
            ["account"] = new JsonObject { ["id"] = "A", ["region"] = "eu" },
            ["boxes"] = new JsonArray("first", "second"),
        };
        var other = new JsonObject
        {
            ["boxes"] = new JsonArray("first", "second"),
            ["account"] = new JsonObject { ["region"] = "eu", ["id"] = "A" },
        };
        var reordered = new JsonObject
        {
            ["account"] = new JsonObject { ["id"] = "A", ["region"] = "eu" },
            ["boxes"] = new JsonArray("second", "first"),
        };

        Assert.Equal(BindingIdentity.Of(one), BindingIdentity.Of(other));
        Assert.NotEqual(BindingIdentity.Of(one), BindingIdentity.Of(reordered));
    }

    [Fact]
    public void Two_bindings_that_differ_in_a_value_are_two_bindings()
    {
        var one = new JsonObject { ["workspace"] = "west" };
        var other = new JsonObject { ["workspace"] = "east" };

        Assert.NotEqual(BindingIdentity.Of(one), BindingIdentity.Of(other));
    }

    [Fact]
    public void A_value_and_the_text_of_that_value_are_not_the_same_binding()
    {
        Assert.NotEqual(
            BindingIdentity.Of(new JsonObject { ["workspace"] = 7 }),
            BindingIdentity.Of(new JsonObject { ["workspace"] = "7" }));
        Assert.NotEqual(
            BindingIdentity.Of(new JsonObject { ["shared"] = true }),
            BindingIdentity.Of(new JsonObject { ["shared"] = "true" }));
        Assert.NotEqual(
            BindingIdentity.Of(new JsonObject { ["workspace"] = null }),
            BindingIdentity.Of(new JsonObject()));
    }

    [Fact]
    public void An_identity_is_the_algorithm_a_package_digest_uses_and_says_so()
    {
        var identity = BindingIdentity.Of(new JsonObject { ["workspace"] = "west" })!;

        Assert.StartsWith("sha256:", identity, StringComparison.Ordinal);
        Assert.Equal(7 + 64, identity.Length);
        Assert.Equal(identity, identity.ToLowerInvariant());
    }

    [Fact]
    public void The_same_binding_hashes_the_same_way_twice()
    {
        var binding = new JsonObject { ["workspace"] = "west", ["nested"] = new JsonObject { ["a"] = 1 } };

        Assert.Equal(BindingIdentity.Of(binding), BindingIdentity.Of(binding.DeepClone().AsObject()));
    }

    /// <summary>
    /// Built rather than parsed, because a parser would refuse it and the claim is about the hashing: a binding
    /// nested far past anything a person would write is hashed rather than thrown over. Hashing is never the
    /// place a malformed route is reported — activation is.
    /// </summary>
    [Fact]
    public void A_binding_nested_deeper_than_any_writer_would_allow_is_still_hashed()
    {
        var binding = Nested(4000);

        var identity = BindingIdentity.Of(binding);

        Assert.NotNull(identity);
        Assert.Equal(identity, BindingIdentity.Of(Nested(4000)));
        Assert.NotEqual(identity, BindingIdentity.Of(Nested(3999)));
    }

    /// <summary>
    /// The other input no parser can produce: a number JSON cannot spell. It is hashed as its own text rather
    /// than throwing, so nothing that merely wants an identity has to be prepared to fail.
    /// </summary>
    [Fact]
    public void A_number_that_is_not_a_json_number_does_not_throw()
    {
        var binding = new JsonObject { ["workspace"] = JsonValue.Create(double.PositiveInfinity) };

        var identity = BindingIdentity.Of(binding);

        Assert.NotNull(identity);
        Assert.NotEqual(identity, BindingIdentity.Of(new JsonObject { ["workspace"] = JsonValue.Create(double.NegativeInfinity) }));
        Assert.NotEqual(identity, BindingIdentity.Of(new JsonObject { ["workspace"] = 0 }));
    }

    private static JsonObject Nested(int depth)
    {
        var root = new JsonObject();
        var current = root;
        for (var level = 0; level < depth; level++)
        {
            var child = new JsonObject { ["level"] = level.ToString(CultureInfo.InvariantCulture) };
            current["down"] = child;
            current = child;
        }

        return root;
    }
}
