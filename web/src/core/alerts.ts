import type { AlertKind, AlertPayload, NotifyPrefs } from "./protocol";

export const DEFAULT_NOTIFY_PREFS: NotifyPrefs = { partyFull: true, dutyPop: true, waitSeconds: 10 };

export const WAIT_CHOICES = [0, 5, 10, 15, 20, 30, 45, 60];

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
