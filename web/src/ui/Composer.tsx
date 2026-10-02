import { useEffect, useRef, useState } from "preact/hooks";
import type { ChatChannel } from "../core/protocol";
import type { SessionState } from "../core/session";
import { channelSlug } from "./channels";
import { byteLength, channelColor, channelLabel, sendBlockedReason, sendPrefix, TELL_TARGET } from "./format";

// latest is the channel of the newest message in a custom channel, its default target.
export type Tab = { kind: "all" } | { kind: "custom"; channels: ChatChannel[]; latest?: ChatChannel } | { kind: "tell"; partner: string };

export function pickable(tab: Tab, sendChannels: ChatChannel[]): ChatChannel[] {
  if (tab.kind === "custom") return sendChannels.filter((c) => tab.channels.includes(c));
  return tab.kind === "all" ? sendChannels : [];
}

export function preferredChannel(tab: Tab, sendChannels: ChatChannel[], current: ChatChannel | undefined): ChatChannel | undefined {
  if (tab.kind === "tell") return "tell";
  const options = pickable(tab, sendChannels);
  if (current && options.includes(current)) return current;
  if (tab.kind === "custom" && tab.latest && options.includes(tab.latest)) return tab.latest;
  return options.find((c) => c !== "tell") ?? options[0];
}

export interface AccountPicker {
  options: { deviceId: string; label: string; online: boolean }[];
  value: string;
  onChange: (deviceId: string) => void;
}

export function Composer({ state, tab, account, blocked: blockedBy, onSend }: {
  state: SessionState;
  tab: Tab;
  account?: AccountPicker;
  blocked?: string;
  onSend: (channel: ChatChannel, text: string, target?: string) => void;
}) {
  const [channel, setChannel] = useState<ChatChannel | undefined>(() => preferredChannel(tab, state.sendChannels, undefined));
  const [text, setText] = useState("");
  const [target, setTarget] = useState("");
  const inputRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    setChannel((c) => preferredChannel(tab, state.sendChannels, c));
  }, [tab, state.sendChannels]);

  useEffect(() => {
    const el = inputRef.current;
    if (!el) return;
    el.style.height = "auto";
    el.style.height = `${Math.min(el.scrollHeight, 140)}px`;
  }, [text]);

  const fixedTarget = tab.kind === "tell" ? tab.partner : undefined;
  const needsTarget = channel === "tell" && !fixedTarget;
  const effectiveTarget = fixedTarget ?? (needsTarget ? target.trim() : undefined);
  const trimmed = text.trim();
  // The plugin limits the whole chat line, so the channel prefix counts too.
  const bytes = channel ? byteLength(sendPrefix(channel, effectiveTarget) + trimmed) : byteLength(trimmed);

  const options = pickable(tab, state.sendChannels);
  let blocked = blockedBy ?? sendBlockedReason(state.status);
  if (!blocked && state.sendChannels.length === 0) blocked = "Sending from your phone is turned off in the plugin";
  if (!blocked && tab.kind === "custom" && options.length === 0) blocked = "Sending to these channels is disabled in the plugin";
  if (!blocked && channel && !state.sendChannels.includes(channel)) blocked = "Sending to this channel is disabled in the plugin";
  if (!blocked && fixedTarget && !TELL_TARGET.test(fixedTarget)) blocked = "Can't reply here because this player's world is unknown";

  let problem: string | null = null;
  if (trimmed.startsWith("/")) problem = "Commands can't be sent, only chat messages";
  else if (bytes > state.maxLength) problem = "Message is too long for the game";
  else if (needsTarget && target.trim() && !TELL_TARGET.test(target.trim())) problem = "Use the full name and world, like Y'shtola Rhul@Twintania";

  const canSend = !blocked && !problem && !!channel && trimmed.length > 0 && (!needsTarget || TELL_TARGET.test(target.trim()));

  function submit(event?: Event) {
    event?.preventDefault();
    if (!canSend || !channel) return;
    onSend(channel, trimmed, effectiveTarget);
    setText("");
    inputRef.current?.focus();
  }

  function onKeyDown(event: KeyboardEvent) {
    if (event.key === "Enter" && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      submit();
    }
  }

  const color = channel ? channelColor(channel) : "var(--muted)";
  const placeholder = fixedTarget
    ? `Message ${fixedTarget.split("@")[0]}`
    : channel && channel !== "tell" ? `Message #${channelSlug(channel)}` : "Message";
  const counter = (
    <span class={`counter${bytes > state.maxLength ? " over" : ""}`} aria-live="polite">
      {bytes}/{state.maxLength}
    </span>
  );

  return (
    <form class={`composer${blocked ? " blocked" : ""}`} style={{ "--c": color }} onSubmit={submit}>
      {tab.kind !== "tell" && (
        <div class="composer-meta">
          {account && (
            <select class="account-select" aria-label="Send as" value={account.value} onChange={(e) => account.onChange(e.currentTarget.value)}>
              {account.options.map((o) => <option value={o.deviceId}>{o.online ? o.label : `${o.label} (offline)`}</option>)}
            </select>
          )}
          <select
            class="channel-select"
            aria-label="Channel"
            value={channel}
            disabled={options.length === 0}
            onChange={(e) => setChannel(e.currentTarget.value as ChatChannel)}
          >
            {options.map((c) => <option value={c}>{channelLabel(c)}</option>)}
          </select>
          {needsTarget && (
            <input
              class="target-input"
              aria-label="Send tell to"
              placeholder="First Last@World"
              value={target}
              onInput={(e) => setTarget(e.currentTarget.value)}
              autocapitalize="words"
              spellcheck={false}
            />
          )}
          {!blocked && counter}
        </div>
      )}
      {blocked ? (
        <p class="composer-reason">{blocked}</p>
      ) : (
        <div class="composer-row">
          <textarea
            ref={inputRef}
            rows={1}
            value={text}
            enterkeyhint="send"
            placeholder={placeholder}
            aria-label="Message"
            onInput={(e) => setText(e.currentTarget.value.replace(/[\r\n]+/g, " "))}
            onKeyDown={onKeyDown}
          />
          {tab.kind === "tell" && text && counter}
          <button type="submit" class="send-btn" disabled={!canSend} aria-label="Send">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
              <path d="M3.4 20.4 21 12 3.4 3.6l-.01 6.53L15 12 3.39 13.87z" />
            </svg>
          </button>
        </div>
      )}
      {!blocked && problem && <p class="composer-problem" role="alert">{problem}</p>}
    </form>
  );
}
