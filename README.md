# ChatTerror

Read and answer your FFXIV chat from your phone.

ChatTerror is three pieces:

- a Dalamud plugin that captures chat and sends chat lines typed on the phone,
- a small relay server (ASP.NET Core) that routes messages and sends push notifications,
- a web app (PWA) served by the relay that you open on your phone.

Messages are end-to-end encrypted between the plugin and each paired phone. The relay only ever sees ciphertext.

## Installing the plugin

ChatTerror is not in the official Dalamud repository. Add it as a custom repository:

1. In game, open `/xlsettings` and go to **Experimental**.
2. Under **Custom Plugin Repositories** add `https://raw.githubusercontent.com/MTVirux/ChatTerror/master/repo.json`, tick it and save.
3. Open `/xlplugins`, search for ChatTerror and install it.

Open the settings with `/chatterror` (or `/ct`).

## Verifying a release

The only official plugin repository is `https://raw.githubusercontent.com/MTVirux/ChatTerror/master/repo.json`. Releases after 0.1.0.0 are built from a version tag by GitHub Actions ([release.yml](.github/workflows/release.yml)), which attaches `ChatTerror.zip`, a `SHA256SUMS` file and a signed build provenance attestation.

To check a downloaded zip came from that workflow and this repository:

```sh
gh attestation verify ChatTerror.zip --repo MTVirux/ChatTerror
sha256sum -c SHA256SUMS
```

The Android app (`ChatTerror.apk`) is signed with a certificate whose SHA-256 fingerprint is:

```
8B:C6:EA:31:2A:AA:C9:E1:14:F0:63:19:80:BE:08:83:DE:50:2D:BF:E8:E7:10:66:AA:06:01:E1:36:65:0D:B1
```

Check it with `apksigner verify --print-certs ChatTerror.apk` and compare the `SHA-256 digest` line (printed in lowercase without colons). The same fingerprint is published in [assetlinks.json](web/public/.well-known/assetlinks.json).

The plugin uses the hosted relay at `https://chatterror.mtvirux.app` by default. To use your own relay (see below), set its URL in the **Connection** tab. Plain `http://` is only accepted for a relay on the same computer (`localhost`, `127.0.0.1`, `::1`).

## Pairing a phone

1. In the plugin, go to **Devices** and click **Pair new device**. A 16 character code (`XXXX-XXXX-XXXX-XXXX`), a QR code and a link appear. They are hidden until you click **Show**, so the secret part stays off streams and screenshots.
2. Scan the QR code with your phone, or open the link. You can also open the relay in the phone browser and type the full code. Only the first 8 characters are sent to the relay; the last 8 are a secret that stays between the plugin and the phone.
3. The phone shows a 6 digit code, and the plugin shows the same code next to the device name. Approve only if they match. A request that did not come from the code currently shown in the plugin is marked unverified and can only be rejected.
4. On the phone, add the page to your home screen and enable notifications in its settings if you want push for tells and mentions.

To follow several game clients from one phone, pair each one separately: tap **+** in the server list and pair with the code from the other client. Each paired client shows up as a server, with its characters and their chats listed under it, and a Home view that merges them. All clients must use the same relay as the phone app.

Pairing codes expire after 10 minutes. Each game client can have up to 10 paired devices. Revoking a device in the plugin wipes its data the next time it connects.

## What gets relayed

You choose per channel what is relayed, what triggers a notification and what you can send to from the phone. Tells, mentions of your name and keywords can notify. Quiet hours, ignored senders and per-phone muted channels are supported.

The plugin only ever sends plain chat lines with a channel prefix (`/p`, `/fc`, `/tell Name Surname@World` and so on). Text starting with `/`, containing line breaks or control characters, or longer than the limit is refused and the phone gets an error. Sends while logged out or zoning fail with an explicit error instead of being dropped.

## Pairing with friends

Tells to and from a friend who also uses ChatTerror can be relayed through ChatTerror, end-to-end encrypted. You pair your installs once with a code.

1. In the plugin, go to **Friends**. Under **Add friend**, pick what to share: **Account** (every character on this install, including ones you add later) or one of your characters. Click **Create code**.
2. The code (`XXXX-XXXX-XXXX-XXXX-XXXX-XXXX`) is hidden until you click **Show**. Use **Copy** and send it to your friend yourself, for example in an in-game tell or on Discord.
3. Your friend pastes it under **Enter code**, picks what they share in the same way and clicks **Pair**. Your plugin checks the code and completes the pairing.

Codes are single use and expire after 24 hours. You can have up to 10 active codes and 100 paired friends. Unused codes can be cancelled.

Relayed tells only flow between paired installs, and only between characters inside both scopes. Both characters must also still have each other on their in-game friend list. Scopes can't be changed later: **Remove** the friend and pair again. Removing a friend stops relayed tells between you right away.

## Self-hosting the relay

The PWA always talks to the relay that served it, so self-hosting means opening the PWA from your own relay and pointing the plugin at the same URL.

Web Push and WebCrypto only work over HTTPS (localhost excepted), so put the relay behind a TLS reverse proxy for anything beyond local testing.

### Docker

```sh
docker build -t chatterror-relay .
docker run -d --name chatterror -p 8080:8080 -v chatterror-data:/data chatterror-relay
```

The image stores its SQLite database at `/data/relay.db`. Keep `/data` on a volume: the relay also writes `vapid.json` next to the database, which holds the VAPID private key used for push. Losing it breaks every existing push subscription. If you bind mount a host directory, it must be writable by the container user (uid 1654).

### Without Docker

```sh
cd web && npm ci && npm run build && cd ..
dotnet run -c Release --project src/ChatTerror.Server --no-launch-profile
```

This listens on `http://localhost:5000`. The web build is written to `src/ChatTerror.Server/wwwroot`, which the relay serves.

### Reverse proxy

- Forward WebSocket upgrades for `/ws`.
- Forward the original `Host` header. The relay compares the browser `Origin` with `Host` to block cross-site requests. If you cannot forward it, list your public origin in `Relay__AllowedOrigins`.
- Set `Relay__TrustedProxies` to the proxy address so rate limits see the real client IP. Without it every client looks like the proxy.

A minimal Caddy example (Caddy forwards `Host` and WebSockets by default):

```
chat.example.com {
    reverse_proxy localhost:8080
}
```

With nginx, add `proxy_set_header Host $host;`, `proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;`, `proxy_set_header X-Forwarded-Proto $scheme;`, `proxy_http_version 1.1;`, `proxy_set_header Upgrade $http_upgrade;` and `proxy_set_header Connection "upgrade";`.

### Configuration

All settings live under `Relay` and default to the values below. Set them as environment variables with a `Relay__` prefix, or add a `Relay` section to `appsettings.json`. Durations use `hh:mm:ss`, or `d.hh:mm:ss` for days.

| Variable | Default | Meaning |
|---|---|---|
| `Relay__DbPath` | `chatterror.db` (`/data/relay.db` in Docker) | SQLite database path. `vapid.json` is written in the same directory. |
| `Relay__VapidPublicKey` | empty | VAPID public key. When this or the private key is empty, keys are loaded from or generated into `vapid.json`. |
| `Relay__VapidPrivateKey` | empty | VAPID private key. |
| `Relay__VapidSubject` | `mailto:admin@localhost` | Contact sent to push services. Set it to your own address. |
| `Relay__AllowedOrigins` | empty | Extra allowed browser origins, comma separated. Same host as the request is always allowed. |
| `Relay__TrustedProxies` | empty | Proxy IPs or CIDRs whose `X-Forwarded-For`/`X-Forwarded-Proto` are trusted, comma separated. Empty ignores those headers. |
| `Relay__FramesPerSecond` | `20` | WebSocket frames per second per connection. |
| `Relay__FrameBurst` | `40` | WebSocket frame burst per connection. |
| `Relay__AuthTimeout` | `00:00:05` | Time a new socket has to authenticate. |
| `Relay__MaxSocketsPerClient` | `20` | Open WebSocket connections per IP (IPv6 per /64). |
| `Relay__SocketConnectsPerMinute` | `60` | New WebSocket connections per minute per IP. |
| `Relay__PairingRequestsPerMinute` | `10` | Pairing lookups and claims per minute per IP. |
| `Relay__InstallsPerHour` | `5` | Plugin registrations per hour per IP (IPv6 per /56). |
| `Relay__MaxInstallsPerDay` | `2000` | Plugin registrations per 24 hours across the whole relay. Further registrations get a 503. |
| `Relay__PendingDeviceTtl` | `01:00:00` | How long an unapproved device is kept. |
| `Relay__PairingsPerInstallPerHour` | `10` | Pairing codes a plugin install can create per hour. Each new code replaces the previous one. |
| `Relay__InstallTtl` | `30.00:00:00` | Plugin installs with no devices are deleted after not connecting for this long. |
| `Relay__InactiveInstallTtl` | `90.00:00:00` | Plugin installs are deleted with all their devices after not connecting for this long. |
| `Relay__InactiveDeviceTtl` | `90.00:00:00` | Devices are deleted after not connecting for this long. |
| `Relay__PushServiceHosts` | `fcm.googleapis.com,*.push.services.mozilla.com,*.push.apple.com,*.notify.windows.com` | Push service hosts a device may subscribe with, comma separated. `*.` matches any subdomain. Endpoints must also be https on port 443. |
| `Relay__TellRequestsPerMinute` | `60` | Tell and friend pairing API requests per minute per IP. |
| `Relay__TellsPerRecipientPerMinute` | `30` | Relayed tells per minute from one plugin install to another. |
| `Relay__TellPushCooldown` | `00:00:10` | Minimum time between tell notifications from one plugin install to one device. Tells inside the cooldown are still delivered. |
| `Relay__MaxQueuedTellsPerSender` | `20` | Undelivered tells kept per sending install for each recipient plugin or device. The oldest are dropped first. |
| `Relay__PushSubscriptionsPerMinute` | `10` | Push subscription updates per minute per IP. |
| `Relay__MaxConcurrentPushes` | `32` | Push requests in flight across the relay. |
| `Relay__MaxPushesPerInstall` | `4` | Push requests in flight per plugin install. |
| `Relay__PushTimeout` | `00:00:10` | Timeout for one push request. |
| `Relay__MaxConcurrentConnections` | `10000` | Kestrel connection limit. |
| `Relay__MaxConcurrentUpgradedConnections` | `5000` | Kestrel WebSocket connection limit. |
| `Relay__MaxRequestBodyBytes` | `16384` | Maximum REST request body size. |

## Privacy

- Each phone and the plugin share a key derived from P-256 ECDH. Chat, backlog, settings and sends are encrypted with AES-256-GCM before they leave the plugin or the phone.
- The relay stores the plugin and device public keys, device names, approval status, last seen times and push subscriptions. Tokens are stored as SHA-256 hashes. It can't read messages.
- With relayed tells on, the relay also stores which installs are paired, a sealed list of your in-scope characters for each friend that only that friend can open, each install's signed key bundle, and sealed tell envelopes queued for up to 7 days. It no longer stores character hashes or friend lists.
- The secret half of a friend code never reaches the relay, so it can't complete a pairing or swap a key or scope in one.
- Push notifications carry the same end-to-end encrypted payload, decrypted on the phone by the service worker. The push service (Google, Apple, Mozilla) sees only ciphertext.
- The relay can still see metadata: when you are online, which phone receives how much traffic, and IP addresses.
- The 6 digit pairing code is derived from both public keys and the secret half of the pairing code, which the relay never sees, so a malicious relay cannot swap keys during pairing without the numbers differing. Compare it every time.
- The plugin keeps a history for backlog (500 messages per character by default), saved encrypted with Windows data protection so phones catch up after a reload or game restart. Phones cache history locally; you can clear it or unpair from the phone settings.

## License

```
ChatTerror - Read and answer your FFXIV chat from your phone.
Copyright (C) 2026  MTVirux

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as
published by the Free Software Foundation, either version 3 of the
License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
```

The full license text is in [LICENSE](LICENSE).
