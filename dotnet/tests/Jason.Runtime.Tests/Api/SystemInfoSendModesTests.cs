using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// <c>system.info</c> says the installation's send mode in force for a cold contact and for a warm one, each with where it
/// comes from, read from the preferences as they stand: two closed words each, and never the name of the person who set
/// one or anything of a value but its mode.
/// </summary>
public class SystemInfoSendModesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Nine = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_section_says_each_mode_in_force_and_where_it_comes_from_and_names_nobody()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));

        var none = await api.PostOkAsync<SystemInfoResponse>(Operations.SystemInfo, null, Ct);
        Assert.Equal(
            new SendModesInForce(new SendModeInForce(SendMode.EveryMessage, SendModeSource.Default), new SendModeInForce(SendMode.EveryMessage, SendModeSource.Default)),
            none.SendModes);

        await api.PostOkAsync<PreferenceDto>(
            Operations.PreferenceSet, new { key = "send_mode.cold", value = new { mode = "automatic" }, actor = new { type = "human", id = "Quillfeather" } }, Ct);
        var set = await api.RecordAsync<SystemInfoResponse>(Operations.SystemInfo, "send-modes-set", null, Ct);

        Assert.Equal(
            new SendModesInForce(new SendModeInForce(SendMode.Automatic, SendModeSource.Preference), new SendModeInForce(SendMode.EveryMessage, SendModeSource.Default)),
            set.SendModes);
        Assert.DoesNotContain("Quillfeather", JsonSerializer.Serialize(set, JasonJson.Options), StringComparison.Ordinal);
    }
}
