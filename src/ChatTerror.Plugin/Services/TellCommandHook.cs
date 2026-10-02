using System;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.Shell;

namespace ChatTerror.Plugin.Services;

// Sees every chat command, including lines sent from the phone through the chat box.
public sealed unsafe class TellCommandHook : IDisposable
{
    private delegate void ExecuteCommandInnerDelegate(ShellCommandModule* module, Utf8String* command, UIModule* uiModule);

    private readonly Hook<ExecuteCommandInnerDelegate> hook;
    private readonly TellRelay tells;
    private readonly IPluginLog log;

    public TellCommandHook(IGameInteropProvider interop, TellRelay tells, IPluginLog log)
    {
        this.tells = tells;
        this.log = log;
        hook = interop.HookFromAddress<ExecuteCommandInnerDelegate>(ShellCommandModule.MemberFunctionPointers.ExecuteCommandInner, Detour);
        hook.Enable();
    }

    public void Dispose() => hook.Dispose();

    private void Detour(ShellCommandModule* module, Utf8String* command, UIModule* uiModule)
    {
        string? replacement = null;
        try
        {
            replacement = tells.OnCommand(command->ToString());
        }
        catch (Exception ex)
        {
            log.Error(ex, "Tell command hook failed.");
        }

        if (replacement == null)
        {
            hook.Original(module, command, uiModule);
            return;
        }

        var text = Utf8String.FromString(replacement);
        try
        {
            hook.Original(module, text, uiModule);
        }
        finally
        {
            text->Dtor(true);
        }
    }
}
