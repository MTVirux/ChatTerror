import { useMemo } from "preact/hooks";
import type { AccountView, FeedItem } from "../core/accounts";
import { channelIncludes, type ChannelPrefs } from "../core/channelPrefs";
import { customColor, itemKey, showsChatTags } from "./channels";
import type { ChatRow } from "./chatList";
import { Composer, composerTab } from "./Composer";
import { channelLabel, STATUS_LABELS } from "./format";
import { BackIcon, MoreIcon } from "./icons";
import { initials, senderColor } from "./identity";
import { MessageList } from "./MessageList";
import { pendingIn, type PendingSend, type PendingSends } from "./pending";
import { viewKey } from "./place";
import { Portrait } from "./Portrait";

export function ChatPane({ account, prefs, chat, items, unreadCount, sends, pending, wide, onBack, onMenu }: {
  account: AccountView;
  prefs: ChannelPrefs;
  chat: ChatRow;
  items: FeedItem[];
  unreadCount: number;
  sends: PendingSends;
  pending: PendingSend[];
  wide: boolean;
  onBack: () => void;
  onMenu: () => void;
}) {
  const viewId = viewKey(account.deviceId, chat.key);
  const visible = useMemo(() => items.filter((i) => channelIncludes(prefs, chat.key, itemKey(i))), [items, chat.key, prefs]);
  const visiblePending = pending.filter((p) => pendingIn(p, account.deviceId, chat));
  const latest = visible.at(-1)?.channel;
  const tab = useMemo(() => composerTab(chat, latest), [chat.key, chat.custom?.channels.join(), latest]);
  const blocked = chat.character !== account.character ? `${chat.character} isn't logged in` : undefined;
  const status = STATUS_LABELS[account.state.status];

  return (
    <div class="chat">
      <header class="chat-head">
        {!wide && (
          <button class="icon-btn" aria-label="Back" onClick={onBack}>
            <BackIcon />
          </button>
        )}
        <ChatAvatar chat={chat} />
        <div class="chat-title">
          <h1>{chat.name}</h1>
          <small>
            <span class={`status-dot ${account.state.status}`} role="img" title={status} aria-label={status} />
            <span class="chat-sub">{subtitle(chat)}</span>
          </small>
        </div>
        <button class="icon-btn" aria-label="Chat options" onClick={onMenu}>
          <MoreIcon />
        </button>
      </header>

      <MessageList
        viewId={viewId}
        items={visible}
        unreadCount={unreadCount}
        chatTags={showsChatTags(chat.custom)}
        showNames={chat.kind === "group"}
        pending={visiblePending}
        onRetry={(p) => void sends.retry(p)}
        onDismiss={sends.dismiss}
      />

      <Composer
        key={viewId}
        state={account.state}
        tab={tab}
        blocked={blocked}
        onSend={(channel, text, target, from) => void sends.send(account.deviceId, channel, text, target, from)}
      />
    </div>
  );
}

function subtitle(chat: ChatRow): string {
  if (chat.kind === "dm") return chat.world ? `${chat.world} · ${chat.character}` : chat.character;
  const channels = chat.custom?.channels ?? [];
  return `${channels.map(channelLabel).join(", ")} · ${chat.character}`;
}

function ChatAvatar({ chat }: { chat: ChatRow }) {
  if (chat.kind === "group") {
    return (
      <span class="head-avatar group" style={{ "--c": customColor(chat.custom) }} aria-hidden="true">
        {initials(chat.name)}
      </span>
    );
  }
  return (
    <span class="head-avatar" style={{ "--c": senderColor(chat.name) }} aria-hidden="true">
      {initials(chat.name)}
      <Portrait name={chat.name} world={chat.world} />
    </span>
  );
}
