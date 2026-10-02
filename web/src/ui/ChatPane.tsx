import { useEffect, useMemo, useState } from "preact/hooks";
import type { AccountView, FeedItem } from "../core/accounts";
import { channelKey, findCustom, inChannel, type ChannelRef } from "./channels";
import { customColor } from "./ChannelMenu";
import { Composer, type Tab } from "./Composer";
import { defaultSendAccount } from "./feed";
import { STATUS_LABELS } from "./format";
import { MenuIcon } from "./icons";
import { MessageList } from "./MessageList";
import type { PendingSend, PendingSends } from "./pending";

export function ChatPane({ accounts, server, channel, items, unreadCount, sends, pending, onMenu }: {
  accounts: AccountView[];
  server: string;
  channel: ChannelRef | null;
  items: FeedItem[];
  unreadCount: number;
  sends: PendingSends;
  pending: PendingSend[];
  onMenu: () => void;
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

  const prefs = account?.state.channelPrefs;
  const custom = channel && prefs ? findCustom(prefs, channel) : undefined;
  const visible = useMemo(() => (home ? items : channel && prefs ? items.filter((i) => inChannel(i, channel, prefs)) : []), [items, viewId, custom]);

  const visiblePending = pending.filter((p) => {
    if (home) return accounts.some((a) => a.deviceId === p.deviceId);
    if (!account || p.deviceId !== account.deviceId) return false;
    // With no channels yet, failed sends still need somewhere to show Retry and Dismiss.
    if (!channel) return true;
    if (channel.character !== account.character) return false;
    return channel.kind === "custom" ? !!custom?.channels.includes(p.channel) : p.channel === "tell" && p.target === channel.partner;
  });

  const tab = useMemo((): Tab => {
    if (!channel || home) return { kind: "all" };
    if (channel.kind === "tell") return { kind: "tell", partner: channel.partner };
    return { kind: "custom", channels: custom?.channels ?? [], latest: visible.at(-1)?.channel };
  }, [viewId, custom, visible.at(-1)?.channel]);

  const blocked = account && channel && channel.character !== account.character ? `${channel.character} isn't logged in` : undefined;

  function title() {
    if (home) return "Home";
    if (!channel) return account?.label ?? "";
    if (channel.kind === "tell") return channel.partner.split("@")[0];
    return (
      <>
        <span class="hash" style={{ "--c": customColor(custom) }} aria-hidden="true">#</span>
        {custom?.name}
      </>
    );
  }

  const subtitle = home ? "All accounts" : channel ? channel.character : account?.character ?? "Not logged in";
  const accountOf = (deviceId: string) => accounts.find((a) => a.deviceId === deviceId);

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
      </header>

      <MessageList
        viewId={viewId}
        items={visible}
        unreadCount={unreadCount}
        showAccount={home}
        pending={visiblePending}
        accountOf={accountOf}
        onRetry={(p) => void sends.retry(p)}
        onDismiss={sends.dismiss}
      />

      <Composer
        key={viewId}
        state={state}
        tab={tab}
        blocked={blocked}
        account={home && activeAccounts.length > 1 && sendAccount ? {
          options: activeAccounts.map((a) => ({ deviceId: a.deviceId, label: a.label, color: a.color, online: a.state.status === "online" })),
          value: sendAccount,
          onChange: setSendAccount,
        } : undefined}
        onSend={(channel, text, target) => void sends.send(targetId, channel, text, target)}
      />
    </div>
  );
}
