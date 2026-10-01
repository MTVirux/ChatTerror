export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
  ) {
    super(`Request failed: ${status} ${code}`);
  }
}

export interface PushSubscriptionBody {
  endpoint: string;
  keys: { p256dh: string; auth: string };
}

export interface Api {
  lookupPairing(code: string): Promise<{ installId: string; pluginPublicKey: string }>;
  claimPairing(code: string, devicePublicKey: string, deviceName: string): Promise<{ deviceId: string; deviceToken: string }>;
  getMe(token: string): Promise<{ deviceId: string; status: string }>;
  deleteDevice(token: string, deviceId: string): Promise<void>;
  putPush(token: string, subscription: PushSubscriptionBody): Promise<void>;
  deletePush(token: string): Promise<void>;
  getVapid(): Promise<{ publicKey: string }>;
}

const FALLBACK_CODES: Record<number, string> = { 401: "unauthorized", 403: "forbidden", 404: "notFound", 409: "conflict", 429: "rateLimited" };

// Relative paths resolve against the relay that served the page or service worker.
export function createApi(base = ""): Api {
  async function call<T>(method: string, path: string, token?: string, body?: unknown): Promise<T> {
    const headers: Record<string, string> = {};
    if (token) headers.Authorization = `Bearer ${token}`;
    if (body !== undefined) headers["Content-Type"] = "application/json";
    const response = await fetch(base + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    if (!response.ok) {
      const error = await response.json().then((b) => b?.error, () => undefined);
      throw new ApiError(response.status, typeof error === "string" ? error : (FALLBACK_CODES[response.status] ?? `http${response.status}`));
    }
    return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
  }

  return {
    lookupPairing: (code) => call("GET", `/api/pairings/${encodeURIComponent(code)}`),
    claimPairing: (code, devicePublicKey, deviceName) =>
      call("POST", `/api/pairings/${encodeURIComponent(code)}/claim`, undefined, { devicePublicKey, deviceName }),
    getMe: (token) => call("GET", "/api/devices/me", token),
    deleteDevice: (token, deviceId) => call("DELETE", `/api/devices/${encodeURIComponent(deviceId)}`, token),
    putPush: (token, subscription) => call("PUT", "/api/devices/me/push", token, subscription),
    deletePush: (token) => call("DELETE", "/api/devices/me/push", token),
    getVapid: () => call("GET", "/api/vapid"),
  };
}
