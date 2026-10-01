namespace ChatTerror.Server.Tests.Support;

public sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}
