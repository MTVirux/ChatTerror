using System.Collections.Generic;

namespace ChatTerror.Plugin.Logic;

public sealed class TellFriend
{
    public string Hash { get; set; } = "";
    public string Name { get; set; } = "";
    public string World { get; set; } = "";
}

// One of this install's characters, as last seen in game.
public sealed class TellCharacter
{
    public string Hash { get; set; } = "";
    public string Name { get; set; } = "";
    public string World { get; set; } = "";
    public List<TellFriend> Friends { get; set; } = new();
}
