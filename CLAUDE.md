# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A personal digital-photo-frame app: an ASP.NET Core backend proxies a Synology NAS's Photos API, and a React frontend renders a full-screen rotating slideshow from shared albums. See README.md for the feature list and end-user controls.

## Solution layout

- **SynologySlideshow.Core** — no ASP.NET dependency. `SynologyAlbumSource` talks directly to the Synology DSM `entry.cgi`/`auth.cgi` endpoints (session login via `_sid`, then `SYNO.Foto.Browse.Album` / `SYNO.Foto.Browse.Item` calls) and maps DSM JSON into `Models/` (`PhotoAlbum`, `PhotoSlide`, etc.), including the location-string logic (prefers Danish county/state/country names, falls back to landmark/village/town/city elsewhere) and image URL building for thumbnails/photos.
- **SynologySlideshow.Api** — ASP.NET Core Web API. `Services/SlideShowService.cs` owns a singleton `SlideShow` that logs into Synology once at startup (`InitAsync`, called from `Program.cs` after `app.Build()`), fetches all albums/slides eagerly, and shuffles them into an in-memory `Dictionary<PhotoAlbum, PhotoSlide[]>`. There is no re-fetch/refresh trigger currently wired up beyond `SlideShow.Refresh()` being callable. `Controllers/ApiController.cs` exposes `/api/albums`, `/api/albums/{id}`, `/api/albums/{id}/thumbnail.jpg`, `/api/albums/{id}/slides`, `/api/albums/{id}/slides/{slide}.jpg` — the two `.jpg` routes stream the image bytes through from Synology rather than redirecting, so the NAS credentials/session never reach the browser. Also serves the built React app from `wwwroot` via `MapFallbackToFile("index.html")`.
- **SynologySlideshow.Web** — Vite + React 18 + TypeScript SPA. `services/api.ts` calls the API under `/api` (proxied to the backend in dev via `vite.config.ts`, same-origin in production since the API serves the built static files).

## Frontend architecture

- `Home.tsx` is the central component: owns album/slide selection, the current/previous slide pair (for cross-fade transitions via `SlideLayer`), the slide-advance timer, and image preloading. Preloaded `Image` objects are kept in refs (`imageObjectsRef`) specifically so the browser doesn't re-fetch them when they're swapped back into view.
- The slide timer is rebuilt whenever pause state, slide speed, tab visibility, or the slides array changes (`useTabVisibility` pauses the show when the tab isn't active; `useScreenWakeLock` keeps the screen on while visible).
- Settings (zoom mode, blurred background, Ken Burns effect, slide speed) and the last-viewed album ID persist to `localStorage` — see `useSettings.ts` and the `STORAGE_KEY`/`SETTINGS_STORAGE_KEY` constants in `Home.tsx`/`useSettings.ts`.
- `useVersionCheck.ts` polls `/version.json` every 5 minutes and compares against the hash captured at first load; the Vite `generate-version` plugin (in `vite.config.ts`) stamps a fresh random hash into `dist/version.json` on every build, which is how `UpdateNotification` detects that a new deployment has shipped.
- Routing (`react-router-dom`) is single-route (`/` and `/album/:albumId` both render `Home`); album selection updates the URL and `localStorage` in that priority order (URL param → saved album → first album).

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

There are no automated test suites in this repo currently.

## Docker / deployment

`Dockerfile.production` is a 3-stage build: Node builds the frontend into `dist/`, .NET SDK publishes the API, and the final Alpine runtime image copies the frontend build into `wwwroot` and runs the API — so in production there is a single container serving both. Configuration is passed as `Synology__Uri` / `Synology__Username` / `Synology__Password` env vars (see `docker-compose.yml` for local testing against the `synology-slideshow:test` image and `docker-compose.server.yml` for the published `ghcr.io/jesperll/synology-slideshow` image). CI (`.github/workflows/docker-build.yml`) builds and publishes this image.
