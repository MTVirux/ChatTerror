import type { AlertKind, AlertPayload, NotifyPrefs } from "./protocol";

export const DEFAULT_NOTIFY_PREFS: NotifyPrefs = { partyFull: true, dutyPop: true, waitSeconds: 10 };

// Matches Limits.MaxWaitSeconds in the plugin.
const MAX_WAIT_SECONDS = 60;

export const WAIT_CHOICES = [0, 5, 10, 15, 20, 30, 45, 60];

// The plugin rejects a prefs payload with any malformed field, mutes included, so every field must be valid.
export function notifyPrefsWithDefaults(value: unknown): NotifyPrefs {
  const prefs: Partial<Record<keyof NotifyPrefs, unknown>> = typeof value === "object" && value !== null ? value : {};
  const flag = (key: "partyFull" | "dutyPop") => (typeof prefs[key] === "boolean" ? prefs[key] : DEFAULT_NOTIFY_PREFS[key]);
  const wait = prefs.waitSeconds;
  return {
    partyFull: flag("partyFull"),
    dutyPop: flag("dutyPop"),
    waitSeconds: typeof wait === "number" && Number.isInteger(wait) ? Math.min(Math.max(wait, 0), MAX_WAIT_SECONDS) : DEFAULT_NOTIFY_PREFS.waitSeconds,
  };
}

export function alertNotification(alert: Pick<AlertPayload, "kind" | "duty">, label: string, multiple: boolean): { title: string; body: string } {
  const { title, body } =
    alert.kind === "dutyPop"
      ? { title: "Duty ready", body: alert.duty || "Your duty is ready" }
      : { title: "Party is full", body: "Your Party Finder party has filled up" };
  return { title: multiple ? `${label} - ${title}` : title, body };
}

export function alertTag(deviceId: string, kind: AlertKind): string {
  return `${deviceId}:alert:${kind}`;
}

// renotify is missing from the DOM typings.
export type AlertNotificationOptions = NotificationOptions & { renotify: boolean };

export function alertNotificationOptions(
  deviceId: string,
  alert: Pick<AlertPayload, "kind" | "duty">,
  label: string,
  multiple: boolean,
): { title: string; options: AlertNotificationOptions } {
  const { title, body } = alertNotification(alert, label, multiple);
  return { title, options: { body, tag: alertTag(deviceId, alert.kind), renotify: true, icon: "/icon-192.png", data: { deviceId } } };
}
