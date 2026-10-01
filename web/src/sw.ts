import { notificationTitle, routePush, type RoutedPush } from "./core/pushRoute";
import { referencedAssets, staleAssets } from "./core/shell";

declare const self: ServiceWorkerGlobalScope;

const SHELL_CACHE = "chatterror-shell-v1";
const SHELL_FILES = ["/", "/index.html", "/manifest.webmanifest", "/icon-192.png", "/icon-512.png"];

async function cacheShell() {
  const cache = await caches.open(SHELL_CACHE);
  await cache.addAll(SHELL_FILES);
  const html = await (await fetch("/index.html", { cache: "no-store" })).text();
  await cache.addAll(referencedAssets(html));
}

self.addEventListener("install", (event) => {
  // Offline start is a nicety; a failed precache must not block push delivery.
  event.waitUntil(cacheShell().catch(() => undefined).then(() => self.skipWaiting()));
});

async function pruneAssets() {
  const cache = await caches.open(SHELL_CACHE);
  const index = await cache.match("/index.html");
  if (!index) return;
  const cachedPaths = (await cache.keys()).map((request) => new URL(request.url).pathname);
  const stale = staleAssets(cachedPaths, await index.text());
  await Promise.all(stale.map((path) => cache.delete(path)));
}

self.addEventListener("activate", (event) => {
  event.waitUntil(pruneAssets().catch(() => undefined).then(() => self.clients.claim()));
});

async function networkFirst(request: Request): Promise<Response> {
  try {
    const response = await fetch(request, { cache: "no-cache" });
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

async function handlePush(data: PushMessageData | null) {
  let routed: RoutedPush | null = null;
  try {
    routed = await routePush(data?.json());
  } catch {
    routed = null;
  }

  // Browsers penalise push events that do not show a notification, so failures still show one.
  if (!routed) {
    await self.registration.showNotification("ChatTerror", { body: "New message", tag: "chatterror", icon: "/icon-192.png" });
    return;
  }
  await self.registration.showNotification(notificationTitle(routed), {
    body: routed.item.text,
    tag: routed.deviceId + routed.item.channel + routed.item.sender,
    icon: "/icon-192.png",
    data: { deviceId: routed.deviceId },
  });
}

self.addEventListener("push", (event) => {
  event.waitUntil(handlePush(event.data));
});

async function focusApp(deviceId: string | undefined) {
  const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
  const existing = windows[0];
  if (existing) {
    if (deviceId) existing.postMessage({ type: "openAccount", deviceId });
    await existing.focus();
  } else {
    await self.clients.openWindow(deviceId ? `/#account=${encodeURIComponent(deviceId)}` : "/");
  }
}

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const deviceId = (event.notification.data as { deviceId?: string } | null)?.deviceId;
  event.waitUntil(focusApp(deviceId));
});
