import { openPayload } from "./core/crypto";
import { CHANNEL_LABELS, parsePluginPayload, type ChatItem } from "./core/protocol";
import { SeqGuard } from "./core/seq";
import { addMessages, getMeta, getPairing, setMeta } from "./core/storage";

declare const self: ServiceWorkerGlobalScope;

const SHELL_CACHE = "chatterror-shell-v1";
const SHELL_FILES = ["/", "/index.html", "/manifest.webmanifest", "/icon-192.png", "/icon-512.png"];

async function cacheShell() {
  const cache = await caches.open(SHELL_CACHE);
  await cache.addAll(SHELL_FILES);
  const html = await (await fetch("/index.html", { cache: "no-store" })).text();
  const assets = [...html.matchAll(/(?:src|href)="(\/assets\/[^"]+)"/g)].map((m) => m[1]);
  await cache.addAll(assets);
}

self.addEventListener("install", (event) => {
  // Offline start is a nicety; a failed precache must not block push delivery.
  event.waitUntil(cacheShell().catch(() => undefined).then(() => self.skipWaiting()));
});

self.addEventListener("activate", (event) => {
  event.waitUntil(self.clients.claim());
});

async function networkFirst(request: Request): Promise<Response> {
  try {
    const response = await fetch(request);
    const path = new URL(request.url).pathname;
    if (response.ok && (path === "/" || path === "/index.html")) await (await caches.open(SHELL_CACHE)).put("/index.html", response.clone());
    return response;
  } catch {
    return (await caches.match("/index.html")) ?? Response.error();
  }
}

async function cacheFirst(request: Request): Promise<Response> {
  const cached = await caches.match(request);
  if (cached) return cached;
  const response = await fetch(request);
  if (response.ok) await (await caches.open(SHELL_CACHE)).put(request, response.clone());
  return response;
}

self.addEventListener("fetch", (event) => {
  const request = event.request;
  const url = new URL(request.url);
  if (request.method !== "GET" || url.origin !== self.location.origin) return;
  if (request.mode === "navigate") event.respondWith(networkFirst(request));
  else if (url.pathname.startsWith("/assets/")) event.respondWith(cacheFirst(request));
});

async function decryptPush(envelope: string): Promise<ChatItem | null> {
  const pairing = await getPairing();
  if (!pairing) return null;
  const payload = parsePluginPayload(await openPayload(pairing.aesKey, "p2d", envelope));
  if (payload?.type !== "chat") return null;

  const guard = new SeqGuard(await getMeta("lastSeenPush"));
  if (!guard.accept(payload.seq)) return null;
  await setMeta("lastSeenPush", payload.seq);
  await addMessages([payload.item], await getMeta("cacheLimit"));
  return payload.item;
}

async function handlePush(data: PushMessageData | null) {
  let item: ChatItem | null = null;
  try {
    const body = data?.json() as { p?: unknown } | undefined;
    if (typeof body?.p === "string") item = await decryptPush(body.p);
  } catch {
    item = null;
  }

  // Browsers penalise push events that do not show a notification, so failures still show one.
  if (!item) {
    await self.registration.showNotification("ChatTerror", { body: "New message", tag: "chatterror", icon: "/icon-192.png" });
    return;
  }
  await self.registration.showNotification(`${item.sender} (${CHANNEL_LABELS[item.channel]})`, {
    body: item.text,
    tag: item.channel + item.sender,
    icon: "/icon-192.png",
    data: { id: item.id },
  });
}

self.addEventListener("push", (event) => {
  event.waitUntil(handlePush(event.data));
});

async function focusApp() {
  const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
  const existing = windows[0];
  if (existing) await existing.focus();
  else await self.clients.openWindow("/");
}

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  event.waitUntil(focusApp());
});
