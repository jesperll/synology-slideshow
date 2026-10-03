# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A personal digital-photo-frame app: an ASP.NET Core backend proxies a Synology NAS's Photos API, and a React frontend renders a full-screen rotating slideshow from shared albums. See README.md for the feature list and end-user controls.

## Solution layout

- **SynologySlideshow.Core** — no ASP.NET dependency. `SynologyAlbumSource` talks directly to the Synology DSM `entry.cgi`/`auth.cgi` endpoints (session login via `_sid`, then `SYNO.Foto.Browse.Album` / `SYNO.Foto.Browse.Item` calls) and maps DSM JSON into `Models/` (`PhotoAlbum`, `PhotoSlide`, etc.), including the location-string logic (prefers Danish county/state/country names, falls back to landmark/village/town/city elsewhere). Each slide gets two Synology thumbnail URLs: `Uri` (`size=xl`, full quality, used for full-screen display) and `ThumbnailUri` (`size=sm`, ~23KB, used only by the admin page's grid/current-slide previews).
- **SynologySlideshow.Api** — ASP.NET Core Web API. `Services/SlideShowService.cs` owns a singleton `SlideShow` that logs into Synology once at startup (`InitAsync`, called from `Program.cs` after `app.Build()`), fetches all albums/slides eagerly, and shuffles them into an in-memory `Dictionary<PhotoAlbum, PhotoSlide[]>`. There is no re-fetch/refresh trigger currently wired up beyond `SlideShow.Refresh()` being callable. `Controllers/ApiController.cs` exposes `/api/albums`, `/api/albums/{id}`, `/api/albums/{id}/thumbnail.jpg`, `/api/albums/{id}/slides`, `/api/albums/{id}/slides/{slide}.jpg`, `/api/albums/{id}/slides/{slide}/thumbnail.jpg` — the `.jpg` routes stream image bytes through from Synology rather than redirecting, so the NAS credentials/session never reach the browser. Also serves the built React app from `wwwroot` via `MapFallbackToFile("index.html")`.
- **Channels** (SQLite-backed, EF Core, `Data/SlideshowDbContext.cs`) are the unit of playback: each `Channel` row has its own current album/slide/pause state, owned server-side by `Services/ChannelPlaybackService.cs` (a 30s advance timer per channel, sync-linked groups via `SyncGroupService`) and pushed to viewers over SignalR (`Realtime/SlideshowHub.cs` at `/hub/slideshow`). `Services/ChannelTimerStartup.cs` seeds a permanent, non-deletable `Channel` named `"Default"` (`IsDefault = true`) on first boot — every device is on *some* channel, named or default; there's no separate "anonymous" tier. `Controllers/ChannelsController.cs` is the REST surface for creating/listing/deleting channels and viewing per-channel stats (`Services/ViewStatsService.cs` records a view on every advance/jump).
- **SynologySlideshow.Web** — Vite + React 18 + TypeScript SPA. `services/api.ts` calls the API under `/api` (proxied to the backend in dev via `vite.config.ts`, same-origin in production since the API serves the built static files).

## Frontend architecture

- There is a single viewer implementation: `ChannelView.tsx`/`ChannelViewPresentation.tsx`, driven entirely by server-pushed `ChannelState` over SignalR (`useChannelConnection.ts`) — it owns no local slide-selection state. `RootRoute.tsx` redirects `/` to whichever channel this device last visited (`services/channelMemory.ts`, `localStorage`), or to `/Default` if nothing is remembered. `SlideLayer` renders the current slide (cross-fade via `fadeIn`/`fadeOut` props) with image preloading.
- Settings (zoom mode, blurred background, Ken Burns effect, slide speed) persist to `localStorage` per-device — see `useSettings.ts`. The Settings panel's footer is conditional: on the default channel it shows `JoinChannelPicker` (pick a different channel); on a named channel it shows a "Leave channel" button (returns to `/`, which redirects back to Default).
- `useVersionCheck.ts` polls `/version.json` every 5 minutes and compares against the hash captured at first load; the Vite `generate-version` plugin (in `vite.config.ts`) stamps a fresh random hash into `dist/version.json` on every build and also appends it as a `?v=` query param on `index.html`'s `app.css` link, so neither the JS bundle nor the stylesheet can be served stale from a browser cache after a deploy.
- `AdminPage.tsx` (`/admin`) lists every channel (including the protected Default one, which has no Delete button) with live viewer counts, playback controls, sync-link management, and per-channel view stats, all driven by `useAdminConnection.ts` over the same SignalR hub.
- Routing (`react-router-dom`): `/` → `RootRoute`, `/admin` → `AdminPage`, `/:channelName` → `ChannelView` (catch-all for everything else, including `Default`).

## Testing

Backend: xUnit (`SynologySlideshow.Api.Tests`) — run with `dotnet test SynologySlideshow.sln`. Test-class parallelism is disabled assembly-wide (`SynologySlideshow.Api.Tests/AssemblyInfo.cs`) because several fixtures call the process-global `SqliteConnection.ClearAllPools()`, which otherwise races across concurrently-running test classes.
Frontend: vitest + Testing Library (`SynologySlideshow.Web`) — run with `npm test`.

## Commands

Backend (from repo root or `SynologySlideshow.Api/`):
```bash
dotnet build                                   # build whole solution
dotnet run --project SynologySlideshow.Api     # run API (also serves built frontend if wwwroot exists)
```
Requires Synology credentials in `SynologySlideshow.Api/appsettings.Development.json` (or env vars `Synology__Uri`, `Synology__Username`, `Synology__Password`) under a `Synology` section — see `SlideShowService.SynologyOptions`.

Frontend (from `SynologySlideshow.Web/`):
```bash
npm install
npm run dev       # Vite dev server on :5173, proxies /api to http://localhost:5154
npm run build     # tsc typecheck + production build to dist/
npm run preview   # preview the production build
```

## Docker / deployment

`Dockerfile.production` is a 3-stage build: Node builds the frontend into `dist/`, .NET SDK publishes the API, and the final Alpine runtime image copies the frontend build into `wwwroot` and runs the API — so in production there is a single container serving both. Configuration is passed as `Synology__Uri` / `Synology__Username` / `Synology__Password` env vars (see `docker-compose.yml` for local testing against the `synology-slideshow:test` image and `docker-compose.server.yml` for the published `ghcr.io/jesperll/synology-slideshow` image). CI (`.github/workflows/docker-build.yml`) builds and publishes this image.
