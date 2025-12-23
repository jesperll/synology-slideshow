# SynologySlideshow - Personal Photo Slideshow

Something I threw together to be able to use an old tablet as a digital photo frame. 
I've added a user on my Synology NAS with limited permissions and any albums I share with that user is visible on the frame.
Obviously you can just use your normal Synology user if that's simpler. However, beware that I haven't implemented 2-factor auth.

## Overview

SynologySlideshow is a web-based slideshow viewer with a sleek React interface and ASP.NET Core backend. It connects directly to your Synology Photos API to display beautiful, rotating slideshows from your albums.

**Key Features:**
- 🖼️ Full-screen photo slideshow with smooth transitions
- 🎨 Cinematic blurred backgrounds (Fit mode)
- 🎬 Ken Burns effect animations (Fill mode)
- ⏱️ Configurable slide duration (15s - 120s)
- 📱 Touch/swipe gesture support
- ⌨️ Keyboard shortcuts
- 🌙 Automatic screen wake lock
- 💾 Settings persistence
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

# 65. Access at http://localhost:8080
```

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

## User Guide

### Controls

**Keyboard:**
- `→` / `←` - Next/Previous slide
- `Space` - Pause/Resume slideshow
- `Double-click` - Show/Hide menu

**Touch/Mouse:**
- Swipe left/right - Navigate slides
- Swipe down - Show menu
- Swipe up - Resume slideshow
- Double-click - Show/Hide menu

### Settings

Access settings by pausing (double-click or spacebar):

**Slideshow:**
- **Slide Duration** - Choose from 15s, 30s, 60s, or 120s intervals

**Image Display:**
- **Zoom to Fit** - Shows entire image (may have letterboxing)
  - Enable blurred background for cinematic effect
- **Zoom to Fill** - Fills screen (may crop image)
  - Enable Ken Burns effect for documentary-style animation

All settings are saved automatically to your browser's localStorage.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.