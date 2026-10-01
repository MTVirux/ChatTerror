import { useEffect, useRef, useState } from "preact/hooks";
import type { ChatChannel } from "../core/protocol";
import type { SessionState } from "../core/session";
import type { Tab } from "./ChatView";
import { byteLength, channelColor, channelLabel, sendBlockedReason, sendPrefix, TELL_TARGET } from "./format";

function preferredChannel(tab: Tab, sendChannels: ChatChannel[], current: ChatChannel | undefined): ChatChannel | undefined {
  if (tab.kind === "tell") return "tell";
  if (tab.kind === "channel" && sendChannels.includes(tab.channel)) return tab.channel;
  if (current && sendChannels.includes(current)) return current;
  return sendChannels.find((c) => c !== "tell") ?? sendChannels[0];
}

export function Composer({ state, tab, onSend }: {
  state: SessionState;
  tab: Tab;
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

  let blocked = sendBlockedReason(state.status);
  if (!blocked && state.sendChannels.length === 0) blocked = "Sending from your phone is turned off in the plugin";
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
    : channel === "tell" ? "Message" : channel ? `Message ${channelLabel(channel)}` : "Message";

  return (
    <form class={`composer${blocked ? " blocked" : ""}`} style={{ "--c": color }} onSubmit={submit}>
      {blocked && <p class="composer-reason">{blocked}</p>}
      <div class="composer-meta">
        {fixedTarget ? (
          <span class="to-fixed"><span class="chip">Tell</span> to {fixedTarget}</span>
        ) : (
          <select
            class="channel-select"
            aria-label="Channel"
            value={channel}
            disabled={state.sendChannels.length === 0}
            onChange={(e) => setChannel(e.currentTarget.value as ChatChannel)}
          >
            {state.sendChannels.map((c) => <option value={c}>{channelLabel(c)}</option>)}
          </select>
        )}
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
        <span class={`counter${bytes > state.maxLength ? " over" : ""}`} aria-live="polite">
          {bytes}/{state.maxLength}
        </span>
      </div>
      <div class="composer-row">
        <textarea
          ref={inputRef}
          rows={1}
          value={text}
          enterkeyhint="send"
          placeholder={placeholder}
          aria-label="Message"
          disabled={!!blocked}
          onInput={(e) => setText(e.currentTarget.value.replace(/[\r\n]+/g, " "))}
          onKeyDown={onKeyDown}
        />
        <button type="submit" class="send-btn" disabled={!canSend} aria-label="Send">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path d="M3.4 20.4 21 12 3.4 3.6l-.01 6.53L15 12 3.39 13.87z" />
          </svg>
        </button>
      </div>
      {problem && <p class="composer-problem" role="alert">{problem}</p>}
    </form>
  );
}
