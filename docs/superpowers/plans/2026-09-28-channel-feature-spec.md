# Multi-Client Channel Feature — Design Spec

Captured from an interview session on 2026-09-28. This is the design of record for the five implementation plans in this directory. It supersedes any earlier framing of "clients" as a persisted entity — that idea was raised and retired during the interview (see "Identity model" below).

## Motivation

The slideshow currently has no concept of more than one viewer: every browser is an independent, anonymous, self-contained instance (its own local album selection, its own local slide timer, its own local settings). The owner wants to:

- Give individual physical displays (tablets) a stable, human-readable identity.
- See all of them, and their current state, from one admin view — like an audio system's "now playing" list.
- Temporarily group displays so they show the same thing in sync (à la Sonos).
- See view statistics per display (total/most/least viewed images).
- Support a display that isn't a dedicated device at all — a Home Assistant dashboard iframe, viewed by an arbitrary and unbounded number of browsers that have never connected before, all of which must show the exact same thing.

## Foundation

- **Real-time transport:** SignalR (ASP.NET Core). It's native to the existing backend, requires no new backend NuGet package, and is used for both admin-dashboard live updates and channel-state push to viewers.
- **Persistence:** SQLite via EF Core. A single file, mounted as a Docker volume (same pattern as the existing `./logs` mount in `docker-compose.server.yml`). Only Channels (below) are persisted — never anything about an individual browser.

## Identity model — no server-side "Client" entity

The only persisted entity related to this feature is the **Channel** (`id`, `name`, server-authoritative current album/slide/pause state). There is no separate "Client" row, no browser-generated GUID sent to or stored by the server, and no concept of a device "connecting" independent of a Channel.

- A browser is either at `/` (anonymous — contributes only to an aggregate "N connected" count shown in admin) or at `/{channel-name}` (a live viewer of that Channel — contributes to that Channel's live viewer count).
- **Only the admin interface creates Channels** (name only; a Channel can exist with zero viewers ever attached — a "virtual" channel, which is exactly what the Home Assistant iframe case needs: create it in admin, point the iframe at its URL, no device ever has to "own" it).
- A browser attaches to a Channel by selecting it from a live-fetched list in its own Settings panel, which performs a client-side route navigation to `/{channel-name}`. This only ever *joins* an existing Channel — it never creates one. Requesting a Channel name that doesn't exist is an error.
- Because a browser might be a kiosk tablet whose browser always reloads a fixed root URL (`/`) on boot, the browser keeps the channel name it last joined in `localStorage`, purely as a local redirect memory — never sent to or known by the server. On a `/` load, if this value is set, the app immediately navigates to `/{that-name}`.
- A viewer can detach back to `/` (anonymous) at will. The Channel it leaves is untouched and keeps its history (same as any Channel with zero current viewers) until an admin explicitly deletes it.
- Channel names are unique, trivially enforced since only admin creates them.
- Any number of browsers (dedicated tablet, Home Assistant iframes, admin's own preview) can be attached to the same Channel simultaneously. This is a first-class, intended scenario — not an edge case to discourage.

## Server-owned playback

Because more than one browser can render the same Channel at once, the slide-advance timer cannot live client-side (two browsers each running their own timer would race/drift). Instead:

- The backend owns the advance-timer for every Channel and holds its authoritative current album, current slide, and paused/playing state.
- Every viewer of a named Channel is a pure renderer: it receives `ChannelStateChanged` pushes over SignalR and displays them. No browser runs its own `setInterval` for a Channel.
- A local interaction on *any* viewer of a Channel (swipe, keyboard, double-click-to-pause) is just shorthand for sending a control request to that Channel — it affects every other viewer of the same Channel identically. Locally, the pause/settings overlay showing on screen is a separate, purely-local UI concern from the shared paused state itself — pausing a Channel remotely (or from another viewer) must never pop open the settings/album overlay on a screen nobody is standing in front of.
- The anonymous (`/`) experience is untouched by any of this — it keeps its own fully local album selection, local shuffle, and local `setInterval` timer exactly as today.

## Channel control

From the admin interface, any Channel can be:
- Paused/unpaused (toggle).
- Switched to a different (Synology) album.
- Advanced to the next/previous slide, or jumped directly to an arbitrary specific slide (e.g. by clicking its thumbnail).

## Sync-linking

- Named Channels can be linked into a group of arbitrary size (≥2). A Channel can belong to at most one active group at a time.
- Once linked, there is no meaningful "leader" for control purposes: controlling *any* member (pause, album-switch, or slide-jump/step, whether from admin or from a local viewer interaction) propagates identically to every member of the group.
- Groups are temporary and not persisted — an admin unlink, or an incidental server restart, has the same effect: every member simply continues independently from wherever it landed. There is no snapshot/restore of "prior" state.

## Admin interface

- Lives at `/admin` inside the existing React SPA (same build, same Docker image).
- No authentication — consistent with the rest of the app's LAN-trust model (see README: no 2FA). No IP-range enforcement; this is a deployment assumption, not an enforced guard.
- Shows, per Channel: name, live viewer count, current album, current slide (thumbnail), paused/playing, and sync-group membership. Also shows one aggregate row for anonymous (`/`) connections.
- Lets the admin create and delete Channels, issue the control actions above, and link/unlink groups.

## Statistics

- Tracked per Channel only, starting from the moment that Channel exists (there is no "anonymous history" to retroactively attribute, since there's no anonymous identity at all).
- A "view" is counted on every transition to a new current slide, regardless of how long it was displayed (no minimum-dwell filter).
- Counters are all-time, with no automatic decay or reset — cleared only by deleting the Channel. The admin can see, per Channel, total views and the most/least viewed images.

## Implementation plans

This spec is implemented across five plans, each independently shippable, in dependency order:

1. `2026-09-28-channel-core-infrastructure.md` — SQLite/EF Core, the `Channel` entity, the SignalR hub, server-owned playback state, and the admin-only create/delete REST API.
2. `2026-09-28-channel-client-viewing.md` — the SPA's `/{channel-name}` route: a pure-renderer viewer, the "join a channel" picker, and the local redirect-memory.
3. `2026-09-28-admin-interface.md` — the `/admin` page: live channel list + anonymous count, create/delete, and the pause/album/slide controls.
4. `2026-09-28-channel-sync-linking.md` — linking/unlinking Channels into symmetric-control groups.
5. `2026-09-28-channel-statistics.md` — per-Channel view counting and the admin most/least-viewed display.
