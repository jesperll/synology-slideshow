# SynologySlideshow - Personal Photo Slideshow

Something I threw together to be able to use an old tablet as a digital photo frame. 
I've added a user on my Synology NAS with limited permissions and any albums I share with that user is visible on the frame.
Obviously you can just use your normal Synology user if that's simpler. However, beware that I haven't implemented 2-factor auth.

## Overview

SynologySlideshow is a web-based slideshow viewer with a sleek React interface and ASP.NET Core backend. It connects directly to your Synology Photos API to display beautiful, rotating slideshows from your albums.

**Key Features:**
- 🖼️ Full-screen photo slideshow with smooth cross-fade transitions
- 🎨 Cinematic blurred backgrounds (Fit mode)
- 🎬 Ken Burns effect animations (Fill mode)
- ⏱️ Configurable slide duration (15s - 120s)
- 📱 Touch/swipe gesture support
- ⌨️ Keyboard shortcuts
- 🌙 Automatic screen wake lock
- 💾 Per-device settings persistence
- 📺 Multi-device "channels" — point any number of screens at a shared, synchronized slideshow
- 🛠️ Admin dashboard — manage channels, control playback remotely, link channels for synced playback, view stats
- 📊 Per-channel view statistics (total/most/least-viewed photos)
- 🔄 Automatic (nightly) and on-demand photo library refresh
- 🐳 Docker deployment ready

## Quick Start with Docker

### Using Pre-Built Image (Recommended)

```bash
# 1. Create directory for your deployment
mkdir synology-slideshow && cd synology-slideshow

# 2. Copy docker-compose.server.yml and rename to docker-compose.yml
# Copy .env.server.example and rename to .env

# 3. Edit .env file with your credentials
SYNOLOGY_URI=https://your-synology-nas.local:5001/webapi
SYNOLOGY_USERNAME=your_actual_username
SYNOLOGY_PASSWORD=your_actual_password

# 4. Deploy
docker-compose pull
docker-compose up -d

# 5. Access at http://localhost:8080
```

Channels, their playback state, and view stats live in a small SQLite database at `/app/data/slideshow.db` inside the container — both compose files already mount `./data:/app/data` for this. Keep that volume mounted across redeploys/updates, or you'll lose every channel back to a freshly-reseeded "Default" one (a plain `docker restart` of the same container is unaffected either way).

### Building Locally

```bash
# Build the Docker image
docker build -f Dockerfile.production -t synology-slideshow .

# Run the container
docker run -d \
  -p 8080:8080 \
  -e Synology__Uri=https://your-synology-nas.local:5001/webapi \
  -e Synology__Username=your_username \
  -e Synology__Password=your_password \
  synology-slideshow
```

## Development Setup

### Prerequisites
- .NET 10 SDK
- Node.js 20+
- Access to a Synology NAS with Photos app

### Running Locally

```bash
# 1. Clone the repository
git clone https://github.com/jesperll/synology-slideshow.git
cd synology-slideshow

# 2. Configure backend (API)
cd SynologySlideshow.Api
# Edit appsettings.Development.json with your Synology credentials

# 3. Start backend API (in one terminal)
dotnet run

# 4. Start frontend dev server (in another terminal)
cd ../SynologySlideshow.Web
npm install
npm run dev

# 5. Access at http://localhost:5173
```

## Channels & Admin

Every device that opens the app is watching a **channel** — a shared, server-managed slideshow that any number of screens can watch in sync at once. A fresh device watches the built-in "Default" channel unless you point it elsewhere:

- Open `/your-channel-name` directly, or
- Open Settings and pick a different channel

Whichever channel a device last watched is remembered (in that browser's `localStorage`), so refreshing or reopening the page goes straight back to it.

The **Admin page** (`/admin`) is the control room for all of this:
- Create and delete channels (the Default channel can't be renamed or deleted)
- See every channel's live viewer count, status, and current slide, and switch its album, jump to a slide, or step through images remotely
- **Link** two or more channels so they play in sync, like a multi-room audio system — handy for keeping several screens around the house showing exactly the same photo at the same time
- View each channel's total/most/least-viewed photo stats
- Refresh the photo library on demand (it also refreshes automatically every night) to pick up photos you've just added on the NAS

A channel pauses itself automatically once nobody is watching it, and resumes the moment someone connects — so an idle screen doesn't burn resources or skew the view stats. You can still pause/play a channel manually from the Admin page regardless.

There's no login on `/admin` (same caveat as the NAS credentials above) — anyone who can reach the server can control every channel.

## User Guide

### Controls

**Keyboard:**
- `→` / `←` - Next/Previous slide
- `Space` - Show/Hide settings
- `Double-click` - Show/Hide settings

**Touch/Mouse:**
- Swipe left/right - Previous/Next slide
- Swipe down - Show settings
- Swipe up - Hide settings
- Double-click - Show/Hide settings

The slideshow keeps playing behind the settings panel — showing it is purely local to your device and never affects other screens watching the same channel.

### Settings

Access settings via double-click or spacebar:

**Slideshow:**
- **Slide Duration** - Choose from 15s, 30s, 60s, or 120s intervals

**Image Display:**
- **Zoom to Fit** - Shows entire image (may have letterboxing)
  - Enable blurred background for cinematic effect
- **Zoom to Fill** - Fills screen (may crop image)
  - Enable Ken Burns effect for documentary-style animation

All settings are per-device, saved automatically to your browser's localStorage.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.