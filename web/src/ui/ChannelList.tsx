import { useState } from "preact/hooks";
import type { AccountManager, AccountView } from "../core/accounts";
import type { ChannelPrefs } from "../core/channelPrefs";
import { ChannelMenu } from "./ChannelMenu";
import { channelKey, channelSlug, isCollapsed, pinFirst, toggleCategory, type Category, type ChannelRef } from "./channels";
import { channelColor, STATUS_LABELS } from "./format";
import { initials, senderColor } from "./identity";
import { useLongPress } from "./longPress";
import { PendingScreen } from "./PendingScreen";
import { RevokedNotice } from "./RevokedNotice";
import type { UnreadTracker } from "./unread";

function toggledKey(deviceId: string): string {
  return `chatterror.toggled.${deviceId}`;
}

function loadToggled(deviceId: string): string[] {
  try {
    const value = JSON.parse(localStorage.getItem(toggledKey(deviceId)) ?? "[]");
    return Array.isArray(value) ? value.filter((c) => typeof c === "string") : [];
  } catch {
    return [];
  }
}

function saveToggled(deviceId: string, characters: string[]) {
  try {
    localStorage.setItem(toggledKey(deviceId), JSON.stringify(characters));
  } catch {
    // Storage can be unavailable in private mode; categories then reset next visit.
  }
}

export function ChannelList({ manager, account, tree, unread, channel, onOpenChannel, onOpenSettings, onPairAgain }: {
  manager: AccountManager;
  account: AccountView;
  tree: Category[];
  unread: UnreadTracker;
  channel: ChannelRef | null;
  onOpenChannel: (ref: ChannelRef) => void;
  onOpenSettings: () => void;
  onPairAgain: () => void;
}) {
  const id = account.deviceId;
  const [toggled, setToggled] = useState(() => loadToggled(id));
  const [menu, setMenu] = useState<ChannelRef | null>(null);
  const selected = channel && channelKey(channel);
  const prefs = account.state.channelPrefs;

  function toggle(character: string) {
    const next = toggleCategory(toggled, character);
    setToggled(next);
    saveToggled(id, next);
  }

  function body() {
    if (account.status === "revoked") {
      return <RevokedNotice inline label={account.label} onRemove={() => manager.remove(id)} onPairAgain={onPairAgain} />;
    }
    if (account.state.status === "pending") {
      return <PendingScreen inline state={account.state} onCancel={() => manager.remove(id)} />;
    }
    if (tree.length === 0) return <p class="hint channels-empty">No chats yet. They show up here once the game sends some.</p>;
    return tree.map((category) => (
      <CategoryView
        key={category.character}
        category={category}
        collapsed={isCollapsed(category, toggled)}
        selected={selected}
        prefs={prefs}
        count={(ref) => unread.count(id, channelKey(ref))}
        onToggle={() => toggle(category.character)}
        onOpen={onOpenChannel}
        onMenu={setMenu}
      />
    ));
  }

  return (
    <div class="sidebar-body">
      {account.status === "revoked" ? (
        <div class="sidebar-head static">
          <span class="sidebar-title">{account.label}</span>
          <small class="sidebar-status">{STATUS_LABELS.revoked}</small>
        </div>
      ) : (
        <button class="sidebar-head" aria-label={`${account.label} settings`} onClick={onOpenSettings}>
          <span class="sidebar-title">{account.label}</span>
          <small class="sidebar-status">{STATUS_LABELS[account.state.status]}</small>
        </button>
      )}
      <div class="channels">{body()}</div>
      {menu && (
        <ChannelMenu
          channel={menu}
          prefs={prefs}
          onChange={(next) => manager.session(id)?.setChannelPrefs(next).catch(() => undefined)}
          onClose={() => setMenu(null)}
        />
      )}
    </div>
  );
}

function CategoryView({ category, collapsed, selected, prefs, count, onToggle, onOpen, onMenu }: {
  category: Category;
  collapsed: boolean;
  selected: string | null;
  prefs: ChannelPrefs;
  count: (ref: ChannelRef) => number;
  onToggle: () => void;
  onOpen: (ref: ChannelRef) => void;
  onMenu: (ref: ChannelRef) => void;
}) {
  const { character } = category;
  const chats: ChannelRef[] = category.chats.map((c) => ({ kind: "chat", character, channel: c }));
  const tells: ChannelRef[] = category.tells.map((t) => ({ kind: "tell", character, partner: t.partner }));
  const refs = pinFirst([...chats, ...tells], prefs.pinned);
  const shownCount = (ref: ChannelRef) => (prefs.muted.includes(channelKey(ref)) ? 0 : count(ref));
  // A collapsed category still shows the open channel and unread ones, like Discord.
  const shown = collapsed ? refs.filter((r) => channelKey(r) === selected || shownCount(r) > 0) : refs;

  return (
    <section class="category">
      <button class={`category-head${collapsed ? " collapsed" : ""}`} aria-expanded={!collapsed} onClick={onToggle}>
        <ChevronIcon />
        <span class="category-name">{character}</span>
        {category.active && <span class="online-dot" aria-label="Logged in" />}
      </button>
      {shown.map((ref) => {
        const key = channelKey(ref);
        return (
          <ChannelRow
            key={key}
            channel={ref}
            selected={key === selected}
            muted={prefs.muted.includes(key)}
            count={shownCount(ref)}
            onClick={() => onOpen(ref)}
            onMenu={() => onMenu(ref)}
          />
        );
      })}
    </section>
  );
}

function ChannelRow({ channel, selected, muted, count, onClick, onMenu }: {
  channel: ChannelRef;
  selected: boolean;
  muted: boolean;
  count: number;
  onClick: () => void;
  onMenu: () => void;
}) {
  const press = useLongPress(onMenu, onClick);
  const cls = `channel${selected ? " selected" : ""}${count > 0 ? " unread" : ""}${muted ? " muted" : ""}`;
  if (channel.kind === "chat") {
    return (
      <button class={cls} style={{ "--c": channelColor(channel.channel) }} aria-current={selected ? "page" : undefined} {...press}>
        <span class="hash" aria-hidden="true">#</span>
        <span class="channel-name">{channelSlug(channel.channel)}</span>
      </button>
    );
  }
  const [name, world] = channel.partner.split("@");
  return (
    <button class={cls} aria-current={selected ? "page" : undefined} {...press}>
      <span class="dm-avatar" style={{ "--c": senderColor(name) }} aria-hidden="true">{initials(name)}</span>
      <span class="channel-name">
        {name}
        {world && <span class="world">{world}</span>}
      </span>
      {count > 0 && <span class="count-badge" aria-label={`${count} unread`}>{count > 99 ? "99+" : count}</span>}
    </button>
  );
}

export function HomeList({ accounts, unread, onSelectServer }: {
  accounts: AccountView[];
  unread: UnreadTracker;
  onSelectServer: (deviceId: string) => void;
}) {
  return (
    <div class="sidebar-body">
      <div class="sidebar-head static">
        <span class="sidebar-title">All accounts</span>
      </div>
      <div class="channels">
        {accounts.map((a) => {
          const summary = unread.summary(a.deviceId);
          return (
            <button key={a.deviceId} class={`channel home-row${summary.unread ? " unread" : ""}`} onClick={() => onSelectServer(a.deviceId)}>
              <span class={`dm-avatar${a.status === "revoked" ? " revoked" : ""}`} style={{ "--c": senderColor(a.deviceId) }} aria-hidden="true">{initials(a.label)}</span>
              <span class="channel-name">
                {a.label}
                <small class="home-status">{STATUS_LABELS[a.state.status]}</small>
              </span>
              {summary.tells > 0 && <span class="count-badge" aria-label={`${summary.tells} unread tells`}>{summary.tells > 99 ? "99+" : summary.tells}</span>}
            </button>
          );
        })}
      </div>
    </div>
  );
}

function ChevronIcon() {
  return (
    <svg class="chevron" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <path d="M6 9l6 6 6-6" />
    </svg>
  );
}
