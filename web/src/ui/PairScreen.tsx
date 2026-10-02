import { useEffect, useRef, useState } from "preact/hooks";
import { formatPairCode, parsePairCode } from "../core/session";
import { defaultDeviceName, needsHomeScreen, offerAndroidApp } from "./format";
import { CloseIcon } from "./icons";

const MISSING_SECRET = "This code is missing its second half. Enter all 16 characters shown in the plugin, like ABCD-EFGH-JKMN-PQRS.";

function formatCodeInput(value: string): string {
  const raw = value.toUpperCase().replace(/[^0-9A-Z]/g, "").slice(0, 16);
  return raw.match(/.{1,4}/g)?.join("-") ?? "";
}

// Old-style or truncated codes have only the relay half.
function codeError(input: string): string {
  const fromUrl = input.match(/[#?&]pair=([^&\s]+)/i);
  const raw = (fromUrl ? decodeURIComponent(fromUrl[1]) : input).replace(/[^0-9A-Za-z]/g, "");
  return raw.length === 8 ? MISSING_SECRET : "Enter the 16-character code shown in the plugin, like ABCD-EFGH-JKMN-PQRS.";
}

function pairErrorText(error: unknown): string {
  // fetch rejects with a TypeError on network failure; the message differs per browser.
  if (error instanceof TypeError) {
    return "Can't reach the relay. Check your connection and try again.";
  }
  const code = String((error as { code?: string })?.code ?? (error as Error)?.message ?? error);
  const status = (error as { status?: number })?.status;
  if (code === "invalidName") {
    return "That device name can't be used. Try a shorter, plain name.";
  }
  if (code === "alreadyPaired") {
    return "This phone is already paired with that game client.";
  }
  if (code === "tooManyDevices" || status === 409) {
    return "This game client already has 10 paired devices. Remove one in the plugin, then try again.";
  }
  if (code === "rateLimited" || status === 429) {
    return "Too many attempts. Wait a minute, then try again.";
  }
  if (/notFound|expired|invalid/i.test(code) || status === 404) {
    return "That code is wrong or has expired. Codes last 10 minutes - make a new one in the plugin.";
  }
  return "Pairing failed. Make a new code in the plugin and try again.";
}

export function PairScreen({ revoked, pairAgain, onPair, onBack }: {
  revoked?: boolean;
  pairAgain?: boolean;
  onPair: (code: string, deviceName: string) => Promise<unknown>;
  onBack?: () => void;
}) {
  const [code, setCode] = useState("");
  const [name, setName] = useState(defaultDeviceName);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [scanning, setScanning] = useState(false);

  useEffect(() => {
    if (!location.hash.startsWith("#pair=")) return;
    const fromLink = parsePairCode(location.href);
    if (fromLink) setCode(formatPairCode(fromLink));
    else setError(codeError(location.href));
    history.replaceState(null, "", location.pathname + location.search);
  }, []);

  async function submit(event: Event) {
    event.preventDefault();
    if (!parsePairCode(code)) {
      setError(codeError(code));
      return;
    }
    setBusy(true);
    setError("");
    try {
      await onPair(code, name.trim() || defaultDeviceName());
    } catch (e) {
      setError(pairErrorText(e));
      setBusy(false);
    }
  }

  function onScanned(text: string) {
    setScanning(false);
    const parsed = parsePairCode(text);
    if (parsed) {
      setCode(formatPairCode(parsed));
      setError("");
    } else if (/[#?&]pair=/i.test(text)) {
      setError(codeError(text));
    } else {
      setError("That QR code isn't a ChatTerror pairing code.");
    }
  }

  return (
    <main class="pair-page">
      {onBack && (
        <button class="icon-btn pair-close" aria-label="Close" onClick={onBack}>
          <CloseIcon />
        </button>
      )}

      <div class="pair-column">
        {!onBack && (
          <header class="brand">
            <h1 class="wordmark">ChatTerror</h1>
            <p class="lede">Your FFXIV chat on your phone, end-to-end encrypted.</p>
          </header>
        )}

        {revoked && (
          <div class="banner" role="alert">
            <strong>This device was removed.</strong> Its messages were deleted from this phone. Pair again to keep chatting.
          </div>
        )}

        <form class="pair-card" onSubmit={submit} aria-labelledby="pair-title">
          <header class="pair-card-head">
            <h2 id="pair-title">Add a server</h2>
            <p class="hint">In the game, open the ChatTerror window and choose to pair a new device. Scan its QR code or type the code below.</p>
          </header>

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
              placeholder="ABCD-EFGH-JKMN-PQRS"
              autocomplete="one-time-code"
              autocapitalize="characters"
              spellcheck={false}
              inputMode="text"
              maxLength={19}
            />
          </label>

          <label class="field">
            <span>Device name</span>
            <input value={name} onInput={(e) => setName(e.currentTarget.value)} maxLength={40} />
            <small>Shown in the plugin so you can tell your devices apart.</small>
          </label>

          {error && <p class="error" role="alert">{error}</p>}

          <button type="submit" class="btn primary wide" disabled={busy}>
            {busy ? "Pairing..." : revoked || pairAgain ? "Pair again" : onBack ? "Add account" : "Pair this device"}
          </button>
        </form>

        {!onBack && needsHomeScreen() && (
          <p class="hint pair-app">
            On iPhone or iPad, tap Share, then Add to Home Screen, and pair from there. It stays paired more reliably and can show notifications.
          </p>
        )}

        {!onBack && offerAndroidApp() && (
          <p class="hint pair-app">
            On Android? <a class="link" href="/ChatTerror.apk" download>Get the app</a> - it stays paired more reliably than the browser.
          </p>
        )}
      </div>
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
    function stopCamera() {
      stream?.getTracks().forEach((t) => t.stop());
    }

    navigator.mediaDevices
      .getUserMedia({ video: { facingMode: "environment" }, audio: false })
      .then(async (s) => {
        stream = s;
        if (stopped) return stopCamera();
        decode = (await import("jsqr")).default;
        if (stopped || !videoRef.current) return stopCamera();
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
      stopCamera();
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
