import type { Api, PushSubscriptionBody } from "./api";
import { decode } from "./b64url";

export interface PushControl {
  enable(token: string): Promise<boolean>;
  // Without a token only the local subscription is dropped (the relay already forgot the device).
  disable(token?: string): Promise<void>;
}

export function pushSupported(): boolean {
  return typeof navigator !== "undefined" && "serviceWorker" in navigator && typeof PushManager !== "undefined" && typeof Notification !== "undefined";
}

export async function registerServiceWorker(): Promise<ServiceWorkerRegistration | null> {
  if (typeof navigator === "undefined" || !("serviceWorker" in navigator)) return null;
  try {
    return await navigator.serviceWorker.register("/sw.js");
  } catch {
    return null;
  }
}

export function createPushControl(api: Api): PushControl {
  return {
    async enable(token) {
      if (!pushSupported()) return false;
      if ((await Notification.requestPermission()) !== "granted") return false;
      await registerServiceWorker();
      const registration = await navigator.serviceWorker.ready;
      const { publicKey } = await api.getVapid();
      const subscription =
        (await registration.pushManager.getSubscription()) ??
        (await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: decode(publicKey) }));
      const json = subscription.toJSON();
      if (!json.endpoint || !json.keys?.p256dh || !json.keys?.auth) return false;
      const body: PushSubscriptionBody = { endpoint: json.endpoint, keys: { p256dh: json.keys.p256dh, auth: json.keys.auth } };
      await api.putPush(token, body);
      return true;
    },
    async disable(token) {
      if (token) await api.deletePush(token);
      if (!pushSupported()) return;
      const registration = await navigator.serviceWorker.getRegistration();
      await (await registration?.pushManager.getSubscription())?.unsubscribe();
    },
  };
}
