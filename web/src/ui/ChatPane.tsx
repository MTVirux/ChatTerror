import { useEffect, useMemo, useState } from "preact/hooks";
import type { AccountView, FeedItem } from "../core/accounts";
import { channelKey, channelSlug, inChannel, type ChannelRef } from "./channels";
import { Composer, type Tab } from "./Composer";
import { defaultSendAccount } from "./feed";
import { channelColor, STATUS_LABELS } from "./format";
import { GearIcon, MenuIcon } from "./icons";
import { MessageList } from "./MessageList";
import type { PendingSend, PendingSends } from "./pending";

export function ChatPane({ accounts, server, channel, items, unreadCount, sends, pending, onMenu, onOpenSettings }: {
  accounts: AccountView[];
  server: string;
  channel: ChannelRef | null;
  items: FeedItem[];
  unreadCount: number;
  sends: PendingSends;
  pending: PendingSend[];
  onMenu: () => void;
  onOpenSettings: () => void;
}) {
  const home = server === "home";
  const account = home ? undefined : accounts.find((a) => a.deviceId === server);
  const viewId = home ? "home" : channel ? channelKey(channel) : server;
  const [sendAccount, setSendAccount] = useState<string | undefined>(undefined);

  useEffect(() => {
    if (home) setSendAccount((c) => defaultSendAccount(items, accounts, c));
  }, [home, accounts]);

  // With no active account in Home, the first account's state shows why sending is blocked.
  const targetId = account?.deviceId ?? sendAccount ?? accounts[0].deviceId;
  const state = accounts.find((a) => a.deviceId === targetId)?.state ?? accounts[0].state;
  const activeAccounts = accounts.filter((a) => a.status === "active");

  const visible = useMemo(() => (home ? items : channel ? items.filter((i) => inChannel(i, channel)) : []), [items, viewId]);

  const visiblePending = pending.filter((p) => {
    if (home) return accounts.some((a) => a.deviceId === p.deviceId);
    if (!account || !channel || p.deviceId !== account.deviceId || channel.character !== account.character) return false;
    return channel.kind === "chat" ? p.channel === channel.channel : p.channel === "tell" && p.target === channel.partner;
  });

  const tab = useMemo((): Tab => {
    if (!channel || home) return { kind: "all" };
    return channel.kind === "chat" ? { kind: "channel", channel: channel.channel } : { kind: "tell", partner: channel.partner };
  }, [viewId]);

  const blocked = account && channel && channel.character !== account.character ? `${channel.character} isn't logged in` : undefined;

  function title() {
    if (home) return "Home";
    if (!channel) return account?.label ?? "";
    if (channel.kind === "tell") return channel.partner.split("@")[0];
    return (
      <>
        <span class="hash" style={{ "--c": channelColor(channel.channel) }} aria-hidden="true">#</span>
        {channelSlug(channel.channel)}
      </>
    );
  }

  const subtitle = home ? "All accounts" : channel ? channel.character : account?.character ?? "Not logged in";
  const characterOf = (deviceId: string) => {
    const a = accounts.find((x) => x.deviceId === deviceId);
    return a?.character ?? a?.label ?? "";
  };

  return (
    <div class="chat">
      <header class="chat-head">
        <button class="icon-btn menu-btn" aria-label="Open navigation" onClick={onMenu}>
          <MenuIcon />
        </button>
        <div class="chat-title">
          <h1>{title()}</h1>
          <small>
            {account && <span class={`status-dot ${account.state.status}`} role="img" title={STATUS_LABELS[account.state.status]} aria-label={STATUS_LABELS[account.state.status]} />}
            {subtitle}
          </small>
        </div>
        <button class="icon-btn" aria-label={home ? "App settings" : "Account settings"} onClick={onOpenSettings}>
          <GearIcon />
        </button>
      </header>

      <MessageList
        viewId={viewId}
        items={visible}
        unreadCount={unreadCount}
        showAccount={home}
        pending={visiblePending}
        characterOf={characterOf}
        onRetry={(p) => void sends.retry(p)}
        onDismiss={sends.dismiss}
      />

      <Composer
        state={state}
        tab={tab}
        blocked={blocked}
        account={home && activeAccounts.length > 1 && sendAccount ? {
          options: activeAccounts.map((a) => ({ deviceId: a.deviceId, label: a.label, online: a.state.status === "online" })),
          value: sendAccount,
          onChange: setSendAccount,
        } : undefined}
        onSend={(channel, text, target) => void sends.send(targetId, channel, text, target)}
      />
    </div>
  );
}
