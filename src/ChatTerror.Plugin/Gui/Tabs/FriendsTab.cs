using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ChatTerror.Plugin.Logic;
using ChatTerror.Plugin.Services;
using ChatTerror.Protocol;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace ChatTerror.Plugin.Gui.Tabs;

public sealed class FriendsTab(Configuration config, FriendDirectory friends) : ITab
{
    private static readonly Vector4 Yellow = new(1f, 0.8f, 0.3f, 1f);

    private string createScope = FriendScopes.Account;
    private string redeemScope = FriendScopes.Account;
    private string codeInput = "";
    private string? confirmRemove;
    private (string Code, Task<string> Result)? redeeming;

    // Codes are hidden by default for streams and screenshots.
    private string? revealedInvite;

    public string Title => "Friends";

    public void Draw()
    {
        ImGui.TextWrapped("Pair with a friend's ChatTerror once to exchange relayed tells. One of you creates a code and shares it privately, the other enters it.");
        ImGui.Spacing();

        DrawPaired();
        ImGui.Separator();
        DrawAddFriend();
        ImGui.Separator();
        DrawEnterCode();
    }

    private void DrawPaired()
    {
        ImGui.TextUnformatted("Paired friends");
        if (config.PairedFriends.Count == 0)
        {
            ImGui.TextDisabled("No friends paired yet.");
            return;
        }

        if (!ImGui.BeginTable("##friends", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn("Characters");
        ImGui.TableSetupColumn("Their scope");
        ImGui.TableSetupColumn("Your scope");
        ImGui.TableSetupColumn("");
        ImGui.TableHeadersRow();

        foreach (var friend in config.PairedFriends.ToList())
        {
            ImGui.PushID(friend.InstallId);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var names = FriendTrust.CharacterNames(config.TellCharacters, friend);
            if (names.Count == 0)
                ImGui.TextDisabled("No characters on your friend list yet");
            else
                ImGui.TextWrapped(string.Join(", ", names));
            if (FriendTrust.HasConflict(config.PairedFriends, friend))
            {
                ImGui.TextColored(Yellow, "Another paired friend claims the same character");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Tells to a character claimed by more than one paired friend are not relayed. Remove the friend that shouldn't have it.");
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FriendTrust.TheirScopeLabel(config.TellCharacters, friend.TheirScope));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FriendTrust.MyScopeLabel(config.TellCharacters, friend.MyScope));

            ImGui.TableNextColumn();
            if (confirmRemove == friend.InstallId)
            {
                if (ImGui.SmallButton("Confirm"))
                {
                    confirmRemove = null;
                    _ = friends.Remove(friend.InstallId);
                }

                ImGui.SameLine();
                if (ImGui.SmallButton("Keep"))
                    confirmRemove = null;
            }
            else
            {
                using (ImRaii.Disabled(friends.Busy))
                {
                    if (ImGui.SmallButton("Remove"))
                        confirmRemove = friend.InstallId;
                }
            }

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawAddFriend()
    {
        ImGui.TextUnformatted("Add friend");
        ScopeCombo("Share##create", ref createScope);
        ImGui.SameLine();
        using (ImRaii.Disabled(friends.Busy || config.InstallToken == null))
        {
            if (ImGui.Button("Create code"))
                _ = friends.CreateInvite(createScope);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var invite in config.FriendInvites.ToList())
        {
            var remaining = DateTimeOffset.FromUnixTimeMilliseconds(invite.ExpiresAt) - now;
            if (remaining <= TimeSpan.Zero)
                continue;

            ImGui.PushID(invite.Id);
            var code = invite.Secret is { } secret ? new FriendCode(invite.Id, secret).Format() : null;
            var revealed = revealedInvite == invite.Id;
            ImGui.BulletText(code == null ? "Unreadable code" : revealed ? code : Mask(code));
            ImGui.SameLine();
            if (code != null && ImGui.SmallButton(revealed ? "Hide" : "Show"))
                revealedInvite = revealed ? null : invite.Id;
            ImGui.SameLine();
            if (code != null && ImGui.SmallButton("Copy"))
                ImGui.SetClipboardText(code);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Only share the code with the friend you want to pair with.");
            ImGui.SameLine();
            using (ImRaii.Disabled(friends.Busy))
            {
                if (ImGui.SmallButton("Cancel"))
                    _ = friends.CancelInvite(invite.Id);
            }
            ImGui.TextDisabled($"   {FriendTrust.MyScopeLabel(config.TellCharacters, invite.Scope)}, expires in {remaining:h\\:mm}");
            ImGui.PopID();
        }
    }

    private void DrawEnterCode()
    {
        ImGui.TextUnformatted("Enter code");
        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##code", "XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX", ref codeInput, 64);
        ScopeCombo("Share##redeem", ref redeemScope);
        ImGui.SameLine();
        using (ImRaii.Disabled(friends.Busy || config.InstallToken == null || codeInput.Trim().Length == 0))
        {
            if (ImGui.Button("Pair"))
                redeeming = (codeInput, friends.Redeem(codeInput, redeemScope));
        }

        // Kept on a failure so a typo doesn't mean typing all 28 characters again.
        if (redeeming is { Result.IsCompleted: true } done)
        {
            redeeming = null;
            if (done.Result.Result == FriendTrust.CodeSent && codeInput == done.Code)
                codeInput = "";
        }

        if (config.PendingFriends.Count > 0)
            ImGui.TextDisabled($"Waiting for {config.PendingFriends.Count} friend(s) to confirm your code.");
        if (friends.Status is { } status)
            ImGui.TextWrapped(status);
    }

    // Account, or one of our characters seen in game.
    private void ScopeCombo(string label, ref string scope)
    {
        ImGui.SetNextItemWidth(200);
        if (!ImGui.BeginCombo(label, FriendTrust.MyScopeLabel(config.TellCharacters, scope)))
            return;

        if (ImGui.Selectable("Account", scope == FriendScopes.Account))
            scope = FriendScopes.Account;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Every character on this install, including ones you add later.");
        foreach (var character in config.TellCharacters)
        {
            if (ImGui.Selectable($"{character.Name}@{character.World}##{character.Hash}", scope == character.Hash))
                scope = character.Hash;
        }

        ImGui.EndCombo();
    }

    private static string Mask(string code) => string.Concat(code.Select(c => c == '-' ? '-' : '*'));
}
