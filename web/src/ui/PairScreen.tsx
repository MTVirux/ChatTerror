import { useEffect, useRef, useState } from "preact/hooks";
import { pair, parsePairCode } from "../core/session";
import { defaultDeviceName } from "./format";

function formatCodeInput(value: string): string {
  const raw = value.toUpperCase().replace(/[^0-9A-Z]/g, "").slice(0, 8);
  return raw.length > 4 ? `${raw.slice(0, 4)}-${raw.slice(4)}` : raw;
}

function pairErrorText(error: unknown): string {
  const text = String((error as { code?: string })?.code ?? (error as Error)?.message ?? error);
  const status = (error as { status?: number })?.status;
  if (/tooManyDevices/i.test(text) || status === 409) {
    return "This character already has 10 paired devices. Remove one in the plugin, then try again.";
  }
  if (/rateLimited|429/i.test(text) || status === 429) {
    return "Too many attempts. Wait a minute, then try again.";
  }
  if (/notFound|404|expired|invalid/i.test(text) || status === 404) {
    return "That code is wrong or has expired. Codes last 10 minutes - make a new one in the plugin.";
  }
  if (/fetch|network/i.test(text)) {
    return "Can't reach the relay. Check your connection and try again.";
  }
  return "Pairing failed. Make a new code in the plugin and try again.";
}

export function PairScreen({ revoked, onPaired }: { revoked: boolean; onPaired: () => void }) {
  const [code, setCode] = useState("");
  const [name, setName] = useState(defaultDeviceName);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [scanning, setScanning] = useState(false);

  useEffect(() => {
    const fromLink = location.hash.startsWith("#pair=") ? parsePairCode(location.href) : null;
    if (fromLink) {
      setCode(fromLink);
      history.replaceState(null, "", location.pathname + location.search);
    }
  }, []);

  async function submit(event: Event) {
    event.preventDefault();
    const normalized = parsePairCode(code);
    if (!normalized) {
      setError("Enter the 8-character code shown in the plugin, like ABCD-EFGH.");
      return;
    }
    setBusy(true);
    setError("");
    try {
      await pair(normalized, name.trim() || defaultDeviceName());
      onPaired();
    } catch (e) {
      setError(pairErrorText(e));
      setBusy(false);
    }
  }

  function onScanned(text: string) {
    setScanning(false);
    const normalized = parsePairCode(text);
    if (normalized) {
      setCode(normalized);
      setError("");
    } else {
      setError("That QR code isn't a ChatTerror pairing code.");
    }
  }

  return (
    <main class="screen pair">
      <header class="brand">
        <h1 class="wordmark">ChatTerror</h1>
        <p class="lede">Your FFXIV chat on your phone, end-to-end encrypted.</p>
      </header>

      {revoked && (
        <div class="banner" role="alert">
          <strong>This device was removed.</strong> Its messages were deleted from this phone. Pair again to keep chatting.
        </div>
      )}

      <form class="card pair-form" onSubmit={submit}>
        <p class="hint">In the game, open the ChatTerror window and choose to pair a new device. Scan its QR code or type the code below.</p>

        {scanning ? (
          <QrScanner onResult={onScanned} onCancel={() => setScanning(false)} onError={(m) => { setScanning(false); setError(m); }} />
        ) : (
          <button type="button" class="btn secondary wide" onClick={() => { setError(""); setScanning(true); }}>
            <CameraIcon /> Scan QR code
          </button>
        )}

        <label class="field">
          <span>Pairing code</span>
          <input
            class="code-input"
            value={code}
            onInput={(e) => setCode(formatCodeInput(e.currentTarget.value))}
            placeholder="ABCD-EFGH"
            autocomplete="one-time-code"
            autocapitalize="characters"
            spellcheck={false}
            inputMode="text"
            maxLength={9}
          />
        </label>

        <label class="field">
          <span>Device name</span>
          <input value={name} onInput={(e) => setName(e.currentTarget.value)} maxLength={40} />
          <small>Shown in the plugin so you can tell your devices apart.</small>
        </label>

        {error && <p class="error" role="alert">{error}</p>}

        <button type="submit" class="btn primary wide" disabled={busy}>
          {busy ? "Pairing..." : revoked ? "Pair again" : "Pair this device"}
        </button>
      </form>
    </main>
  );
}

function QrScanner({ onResult, onCancel, onError }: { onResult: (text: string) => void; onCancel: () => void; onError: (message: string) => void }) {
  const videoRef = useRef<HTMLVideoElement>(null);

  useEffect(() => {
    let stream: MediaStream | null = null;
    let frame = 0;
    let stopped = false;
    const canvas = document.createElement("canvas");
    const ctx = canvas.getContext("2d", { willReadFrequently: true });

    let decode: typeof import("jsqr").default | null = null;

    function tick() {
      if (stopped) return;
      const video = videoRef.current;
      if (decode && video && ctx && video.readyState >= video.HAVE_ENOUGH_DATA) {
        const scale = Math.min(1, 640 / video.videoWidth);
        canvas.width = Math.round(video.videoWidth * scale);
        canvas.height = Math.round(video.videoHeight * scale);
        ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
        const image = ctx.getImageData(0, 0, canvas.width, canvas.height);
        const found = decode(image.data, image.width, image.height, { inversionAttempts: "dontInvert" });
        if (found?.data) {
          onResult(found.data);
          return;
        }
      }
      frame = requestAnimationFrame(tick);
    }

    if (!navigator.mediaDevices?.getUserMedia) {
      onError("This browser can't use the camera here. Type the code instead.");
      return;
    }
    navigator.mediaDevices
      .getUserMedia({ video: { facingMode: "environment" }, audio: false })
      .then(async (s) => {
        stream = s;
        decode = (await import("jsqr")).default;
        if (stopped || !videoRef.current) return;
        videoRef.current.srcObject = s;
        await videoRef.current.play();
        frame = requestAnimationFrame(tick);
      })
      .catch((e: DOMException) => {
        if (stopped) return;
        onError(e.name === "NotAllowedError" ? "Camera access was blocked. Allow it in your browser settings or type the code." : "Couldn't start the camera. Type the code instead.");
      });

    return () => {
      stopped = true;
      cancelAnimationFrame(frame);
      stream?.getTracks().forEach((t) => t.stop());
    };
  }, []);

  return (
    <div class="scanner">
      <video ref={videoRef} playsInline muted />
      <div class="scanner-frame" aria-hidden="true" />
      <button type="button" class="btn ghost scanner-cancel" onClick={onCancel}>Cancel</button>
    </div>
  );
}

function CameraIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <path d="M4 8V5a1 1 0 0 1 1-1h3M16 4h3a1 1 0 0 1 1 1v3M20 16v3a1 1 0 0 1-1 1h-3M8 20H5a1 1 0 0 1-1-1v-3" />
      <rect x="8" y="8" width="8" height="8" rx="1" />
    </svg>
  );
}
