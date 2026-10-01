using ChatTerror.Plugin.Logic;

namespace ChatTerror.Plugin.Tests;

public class PairingGateTests
{
    [Fact]
    public void Bind_WithoutSession_IsUnverified()
    {
        var gate = new PairingGate();

        Assert.Null(gate.Bind("dev", 0));
    }

    [Fact]
    public void Bind_ActiveSession_ReturnsSecretOnce()
    {
        var gate = new PairingGate();
        gate.Start("ABCD2345", expiresAt: 1_000);

        Assert.Equal("ABCD2345", gate.Bind("dev", 500));
        Assert.Null(gate.Bind("other", 500));
        Assert.False(gate.Active(500));
    }

    [Fact]
    public void Bind_SameDeviceAgain_KeepsSecret()
    {
        var gate = new PairingGate();
        gate.Start("ABCD2345", expiresAt: 1_000);
        gate.Bind("dev", 500);

        Assert.Equal("ABCD2345", gate.Bind("dev", 5_000));
    }

    [Fact]
    public void Bind_AfterExpiry_IsUnverified()
    {
        var gate = new PairingGate();
        gate.Start("ABCD2345", expiresAt: 1_000);

        Assert.False(gate.Active(1_000));
        Assert.Null(gate.Bind("dev", 1_000));
    }

    [Fact]
    public void Bind_AfterCancel_IsUnverified()
    {
        var gate = new PairingGate();
        gate.Start("ABCD2345", expiresAt: 1_000);
        gate.Cancel();

        Assert.Null(gate.Bind("dev", 500));
    }

    [Fact]
    public void Start_ReplacesPreviousSession()
    {
        var gate = new PairingGate();
        gate.Start("AAAA1111", expiresAt: 1_000);
        gate.Start("BBBB2222", expiresAt: 1_000);

        Assert.Equal("BBBB2222", gate.Bind("dev", 500));
    }

    [Fact]
    public void Forget_DropsBoundSecret()
    {
        var gate = new PairingGate();
        gate.Start("ABCD2345", expiresAt: 1_000);
        gate.Bind("dev", 500);
        gate.Forget("dev");

        Assert.Null(gate.Bind("dev", 500));
    }
}
