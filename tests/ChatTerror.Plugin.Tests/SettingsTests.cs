using System;
using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;
using Xunit;

namespace ChatTerror.Plugin.Tests;

public class SettingsTests
{
    [Fact]
    public void OrderedChannels_DefaultsToEnumOrder()
    {
        Assert.Equal(Enum.GetValues<ChatChannel>(), new RelaySettings().OrderedChannels());
    }

    [Fact]
    public void OrderedChannels_KeepsSavedOrderAndAppendsMissing()
    {
        var s = new RelaySettings { ChannelOrder = [ChatChannel.Say, ChatChannel.Party, ChatChannel.Say, (ChatChannel)999] };

        var order = s.OrderedChannels();

        Assert.Equal([ChatChannel.Say, ChatChannel.Party, ChatChannel.Tell], order[..3]);
        Assert.Equal(Enum.GetValues<ChatChannel>().Length, order.Count);
    }
}
