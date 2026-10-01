using ChatTerror.Plugin.Services;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Tests;

public class HistoryFileTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "chatterror-tests-" + Guid.NewGuid().ToString("N"));

    public HistoryFileTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, true);

    private string FilePath => Path.Combine(dir, HistoryFile.Name);

    private static ChatItem Item(long ts, string character) =>
        new($"id{ts}", ts, ChatChannel.FreeCompany, "Bob Smith", Text: "secret plans", Character: character, Outgoing: false);

    [WindowsFact]
    public void SaveRoundTripsEncrypted()
    {
        List<ChatItem> items = [Item(1, "Alex Doe"), Item(2, "Sam Roe")];

        Assert.True(HistoryFile.Save(FilePath, items));

        Assert.DoesNotContain("secret plans", File.ReadAllText(FilePath));
        Assert.Equal(items, HistoryFile.Load(FilePath));
    }

    [Fact]
    public void LoadReturnsEmptyWhenMissing()
    {
        Assert.Empty(HistoryFile.Load(FilePath)!);
    }

    [Fact]
    public void LoadReturnsNullForAnUnreadableFile()
    {
        File.WriteAllBytes(FilePath, [1, 2, 3]);

        Assert.Null(HistoryFile.Load(FilePath));
    }
}
