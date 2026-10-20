using Jason.Contracts.Channels;
using Jint;
using Jint.Native;

namespace Jason.PluginHost.Sdk;

/// <summary>
/// <c>host.channelKey(channel, value)</c>: the value the runtime's contacts, its do-not-contact register and its claim
/// compare for a value on a channel, or <c>null</c> where the runtime would store none. It is the runtime's own rule —
/// <see cref="ChannelRules.KeyOf"/>, the function the runtime calls — handed to the plugin, so a plugin that compares a
/// provider's spelling of an address with the one the runtime stored compares by the runtime's key, never by a copy of the
/// rule that could drift from it. A pure function: it reads no grant, spends no budget and writes nothing.
/// </summary>
public static class ChannelKeyFunction
{
    public const string Function = "host.channelKey";

    public static JsValue Of(Engine engine, JsValue[] args)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || !args[0].IsString())
        {
            throw ArgumentReader.TypeError(engine, $"{Function}: the channel must be a string.");
        }

        if (args.Length < 2 || !args[1].IsString())
        {
            throw ArgumentReader.TypeError(engine, $"{Function}: the value must be a string.");
        }

        return ChannelRules.KeyOf(args[0].AsString(), args[1].AsString()) is { } key ? key : JsValue.Null;
    }
}
