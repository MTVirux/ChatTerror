import { useState } from "preact/hooks";
import type { AccountManager, AccountView } from "../core/accounts";
import { channelIncludes, saveCustom, type ChannelPrefs, type CustomChannel } from "../core/channelPrefs";
import { ChannelMenu, customColor } from "./ChannelMenu";
import { channelKey, findCustom, isCollapsed, toggleCategory, type Category, type ChannelRef } from "./channels";
import { CustomChannelSheet } from "./CustomChannelSheet";
import { STATUS_LABELS } from "./format";
import { PlusIcon } from "./icons";
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

type Editing = { character: string; custom?: CustomChannel };

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
  const [menu, setMenu] = useState<{ ref: ChannelRef; keys: string[] } | null>(null);
  const [editing, setEditing] = useState<Editing | null>(null);
  const selected = channel && channelKey(channel);
  const prefs = account.state.channelPrefs;
  const save = (next: ChannelPrefs) => manager.session(id)?.setChannelPrefs(next).catch(() => undefined);

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
        count={(ref) => unread.count(id, (key) => channelIncludes(prefs, channelKey(ref), key))}
        onToggle={() => toggle(category.character)}
        onAdd={() => setEditing({ character: category.character })}
        onOpen={onOpenChannel}
        onMenu={(ref) => setMenu({ ref, keys: category.refs.map(channelKey) })}
      />
    ));
  }

  const menuCustom = menu ? findCustom(prefs, menu.ref) : undefined;

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
          channel={menu.ref}
          custom={menuCustom}
          keys={menu.keys}
          prefs={prefs}
          onChange={save}
          onEdit={menuCustom ? () => setEditing({ character: menuCustom.character, custom: menuCustom }) : undefined}
          onClose={() => setMenu(null)}
        />
      )}
      {editing && (
        <CustomChannelSheet
          character={editing.character}
          custom={editing.custom}
          relayChannels={account.state.relayChannels}
          onSave={(custom) => {
            save(saveCustom(prefs, custom));
            if (!editing.custom) onOpenChannel({ kind: "custom", character: custom.character, id: custom.id });
          }}
          onClose={() => setEditing(null)}
        />
      )}
    </div>
  );
}

function CategoryView({ category, collapsed, selected, prefs, count, onToggle, onAdd, onOpen, onMenu }: {
  category: Category;
  collapsed: boolean;
  selected: string | null;
  prefs: ChannelPrefs;
  count: (ref: ChannelRef) => number;
  onToggle: () => void;
  onAdd: () => void;
  onOpen: (ref: ChannelRef) => void;
  onMenu: (ref: ChannelRef) => void;
}) {
  const { character, refs } = category;
  const shownCount = (ref: ChannelRef) => (prefs.muted.includes(channelKey(ref)) ? 0 : count(ref));
  // A collapsed category still shows the open channel and unread ones, like Discord.
  const shown = collapsed ? refs.filter((r) => channelKey(r) === selected || shownCount(r) > 0) : refs;
  const hasCustom = refs.some((r) => r.kind === "custom");

  return (
    <section class="category">
      <div class="category-row">
        <button class={`category-head${collapsed ? " collapsed" : ""}`} aria-expanded={!collapsed} onClick={onToggle}>
          <ChevronIcon />
          <span class="category-name">{character}</span>
          {category.active && <span class="online-dot" aria-label="Logged in" />}
        </button>
        <button class="category-add" aria-label={`Add chat channel for ${character}`} onClick={onAdd}>
          <PlusIcon />
        </button>
      </div>
      {!collapsed && !hasCustom && (
        <button class="channel add-channel" onClick={onAdd}>
          <span class="hash" aria-hidden="true">+</span>
          <span class="channel-name">Add chat channel</span>
        </button>
      )}
      {shown.map((ref) => {
        const key = channelKey(ref);
        return (
          <ChannelRow
            key={key}
            channel={ref}
            custom={findCustom(prefs, ref)}
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

function ChannelRow({ channel, custom, selected, muted, count, onClick, onMenu }: {
  channel: ChannelRef;
  custom?: CustomChannel;
  selected: boolean;
  muted: boolean;
  count: number;
  onClick: () => void;
  onMenu: () => void;
}) {
  const press = useLongPress(onMenu, onClick);
  const cls = `channel${selected ? " selected" : ""}${count > 0 ? " unread" : ""}${muted ? " muted" : ""}`;
  if (channel.kind === "custom") {
    return (
      <button class={cls} style={{ "--c": customColor(custom) }} aria-current={selected ? "page" : undefined} {...press}>
        <span class="hash" aria-hidden="true">#</span>
        <span class="channel-name">{custom?.name}</span>
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
              <span class={`dm-avatar${a.status === "revoked" ? " revoked" : ""}`} style={{ "--c": a.color }} aria-hidden="true">{initials(a.label)}</span>
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
