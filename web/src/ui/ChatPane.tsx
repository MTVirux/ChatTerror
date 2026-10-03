import { useMemo } from "preact/hooks";
import type { AccountView, FeedItem } from "../core/accounts";
import { channelIncludes, type ChannelPrefs, type CustomChannel } from "../core/channelPrefs";
import { customColor, itemKey, type SubRow } from "./channels";
import { Composer, composerTab } from "./Composer";
import { channelColor, STATUS_LABELS } from "./format";
import { ChatBubbleIcon, MenuIcon } from "./icons";
import { MessageList } from "./MessageList";
import { pendingIn, type PendingSend, type PendingSends } from "./pending";
import { viewKey } from "./place";

export function ChatPane({ account, prefs, custom, row, items, unreadCount, sends, pending, onMenu }: {
  account: AccountView;
  prefs: ChannelPrefs;
  custom: CustomChannel | null;
  row: SubRow | null;
  items: FeedItem[];
  unreadCount: number;
  sends: PendingSends;
  pending: PendingSend[];
  onMenu: () => void;
}) {
  const viewId = row ? viewKey(account.deviceId, row.key) : account.deviceId;
  const visible = useMemo(() => (row ? items.filter((i) => channelIncludes(prefs, row.key, itemKey(i))) : []), [items, row?.key, prefs]);
  const visiblePending = pending.filter((p) => pendingIn(p, account.deviceId, custom, row));
  const latest = visible.at(-1)?.channel;
  const tab = useMemo(() => (custom && row ? composerTab(custom, row, latest) : null), [custom, row?.key, latest]);
  const character = custom?.character;
  const blocked = character && character !== account.character ? `${character} isn't logged in` : undefined;
  const status = STATUS_LABELS[account.state.status];

  function title() {
    if (!custom || !row) return account.label;
    if (row.kind === "partner") return row.label;
    const color = row.channel ? channelColor(row.channel) : customColor(custom);
    return (
      <>
        <span class="hash" style={{ "--c": color }} aria-hidden="true">{row.hash ? "#" : <ChatBubbleIcon size={20} />}</span>
        {row.kind === "type" ? row.label : custom.name}
      </>
    );
  }

  const subtitle = !custom || !row ? status : row.kind === "all" ? custom.character : `${custom.name} · ${custom.character}`;

  return (
    <div class="chat">
      <header class="chat-head">
        <button class="icon-btn menu-btn" aria-label="Open navigation" onClick={onMenu}>
          <MenuIcon />
        </button>
        <div class="chat-title">
          <h1>{title()}</h1>
          <small>
            <span class={`status-dot ${account.state.status}`} role="img" title={status} aria-label={status} />
            {subtitle}
          </small>
        </div>
      </header>

      <MessageList
        viewId={viewId}
        items={visible}
        unreadCount={unreadCount}
        pending={visiblePending}
        accountOf={(id) => (id === account.deviceId ? account : undefined)}
        onRetry={(p) => void sends.retry(p)}
        onDismiss={sends.dismiss}
      />

      {tab && (
        <Composer
          key={viewId}
          state={account.state}
          tab={tab}
          blocked={blocked}
          onSend={(channel, text, target, from) => void sends.send(account.deviceId, channel, text, target, from)}
        />
      )}
    </div>
  );
}
