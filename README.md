# SingleStepViewer - Multi-User Video Playback System

A web-based video playback system where multiple users manage playlists via a web interface, with videos played automatically on a dedicated big-screen PC using VLC. The system intelligently balances video priority with fairness across users.

## ✅ Implementation Status

### Fully Implemented Features

- ✅ **Web Application Infrastructure** (ASP.NET Core 9.0 + Blazor Server)
- ✅ **Database Layer** (EF Core with SQLite, ASP.NET Identity)
- ✅ **Core Services** (Playlist, Video Download, Queue Management, VLC Playback)
- ✅ **Background Services** (Playback Engine, Video Downloader)
- ✅ **Smart Scheduling Algorithm** (Weighted fair queue with priority + fairness)
- ✅ **Real-time Updates** (SignalR for live playback status)
- ✅ **Complete Blazor UI** (Dashboard, Playlists, Queue visualization)
- ✅ **User Authentication** (ASP.NET Identity with roles)
- ✅ **Video Download** (yt-dlp integration via Process)
- ✅ **VLC Integration** (LibVLCSharp for playback control)

## Quick Start

### Prerequisites

1. **.NET 9.0 SDK** - [Download here](https://dotnet.microsoft.com/download/dotnet/9.0)
2. **yt-dlp** - [Download here](https://github.com/yt-dlp/yt-dlp/releases)
   - Place `yt-dlp.exe` in PATH or update `appsettings.json` with full path
3. **VLC Media Player** - [Download here](https://www.videolan.org/vlc/)
   - Install to default location: `C:\Program Files\VideoLAN\VLC`

### Installation Steps

```bash
# 1. Clone or navigate to the project directory
cd C:\Users\jespe\source\repos\SingleStepViewer

# 2. Restore packages (if needed)
dotnet restore

# 3. Build the project
dotnet build

# 4. Run the application
dotnet run
```

The application will start on `https://localhost:5001` (or the configured port).

### Default Admin Account

- **Email:** admin@singlestep.local
- **Password:** Admin123!

## Features Overview

### 1. Dashboard
- Real-time "Now Playing" display
- Queue preview (next 5 videos)
- System statistics (pending/ready counts)
- Automatic updates via SignalR

### 2. Playlist Management
- Create multiple playlists per user
- Add videos from YouTube and other sources
- Set priority for each video (1-10)
- Automatic metadata extraction (title, duration, thumbnail)
- Background video downloading

### 3. Smart Queue System
The weighted fair queue algorithm balances:
- **Priority**: User-assigned priority (1-10)
- **Fairness**: Time since user's last video played

**Score Formula:**
```
Score = (Priority × 10) + ((Minutes Since Last Played ÷ 10) × 5)
```

This ensures:
- High-priority videos get preference
- Users who haven't had videos played recently get a boost
- No single user can dominate the queue

### 4. Background Processing
- **Video Downloader Service**: Downloads pending videos concurrently (max 2 at a time)
- **Playback Engine Service**: Automatically plays videos from the queue
- **SignalR Broadcasting**: Real-time updates to all connected clients

### 5. VLC Integration
- Full-screen playback on dedicated display
- Automatic video transitions
- Volume control
- Event-driven architecture (media ended, errors)

## Configuration

Edit `appsettings.json` to configure the system:

### Database
```json
"ConnectionStrings": {
  "DefaultConnection": "Data Source=singlestepviewer.db"
}
```

### VLC Playback
```json
"Playback": {
  "VlcPath": "C:\\Program Files\\VideoLAN\\VLC",
  "DefaultVolume": 80,
  "EnableFullscreen": true,
  "EnableVideoOutput": true
}
```

**Note:** Set `EnableVideoOutput: false` for development/testing without VLC window.

### Scheduling Algorithm
```json
"Scheduling": {
  "PriorityWeight": 10.0,
  "FairnessWeight": 5.0,
  "UserCooldownMinutes": 15,
  "QueueCheckIntervalSeconds": 5
}
```

Adjust weights to change the balance between priority and fairness.

### Video Download
```json
"Video": {
  "StoragePath": "./videos",
  "MaxConcurrentDownloads": 2,
  "YtDlpPath": "yt-dlp.exe",
  "PreferredFormat": "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best"
}
```

## Architecture

### Technology Stack

- **Framework:** ASP.NET Core 9.0
- **UI:** Blazor Server (real-time via SignalR)
- **Database:** Entity Framework Core 9.0 with SQLite
- **Authentication:** ASP.NET Identity
- **Video Playback:** LibVLCSharp 3.9.5
- **Video Download:** yt-dlp (via Process)
- **Logging:** Serilog (console + file)

### Project Structure

```
SingleStepViewer/
├── Program.cs                   # Application entry point
├── appsettings.json             # Configuration
├── Data/
│   ├── ApplicationDbContext.cs  # EF Core DbContext
│   └── Entities/                # Database models
│       ├── ApplicationUser.cs
│       ├── Playlist.cs
│       ├── PlaylistItem.cs
│       ├── PlaybackHistory.cs
│       └── QueueState.cs
├── Services/
│   ├── Interfaces/              # Service contracts
│   ├── UserService.cs
│   ├── PlaylistService.cs
│   ├── PlaylistItemService.cs
│   ├── VideoService.cs          # Metadata extraction
│   ├── DownloadService.cs       # Video downloading
│   ├── PlaybackService.cs       # VLC control
│   ├── QueueManager.cs          # Scheduling algorithm
│   └── ConfigurationService.cs
├── BackgroundServices/
│   ├── PlaybackEngineService.cs # Main playback loop
│   └── VideoDownloaderService.cs# Background downloader
├── Components/
│   ├── Pages/                   # Blazor pages
│   │   ├── Index.razor          # Dashboard
│   │   ├── MyPlaylists.razor    # Playlist management
│   │   ├── PlaylistDetail.razor # Add/edit videos
│   │   ├── Queue.razor          # Queue visualization
│   │   └── Account/             # Login/Register
│   ├── Shared/                  # Shared components
│   │   ├── MainLayout.razor
│   │   └── NavMenu.razor
│   └── Hubs/
│       └── PlaybackHub.cs       # SignalR hub
├── Configuration/               # Configuration option classes
└── Migrations/                  # EF Core migrations
```

## Usage Workflow

### For Regular Users

1. **Register** an account at `/account/register`
2. **Login** with your credentials
3. **Create a Playlist** from "My Playlists" page
4. **Add Videos** by entering YouTube URLs
5. **Set Priorities** (1-10) for each video
6. Videos are automatically:
   - Downloaded in the background
   - Added to the queue
   - Played when their turn comes

### For Administrators

- Access admin panel at `/admin` (requires Admin role)
- View system statistics
- Manage users
- Adjust scheduling weights (future feature)

## How It Works

### Video Lifecycle

1. **User adds video URL** → Status: `Pending`
2. **Background downloader picks it up** → Status: `Downloading`
3. **Download completes** → Status: `Ready`
4. **Queue manager selects it** → Status: `Playing`
5. **Playback completes** → Status: `Played`

Errors at any stage set status to `Error` with details.

### Queue Selection Process

Every 5 seconds, the Playback Engine:
1. Checks if a video is currently playing
2. If not, asks Queue Manager for next video
3. Queue Manager:
   - Gets all `Ready` videos
   - Calculates score for each (priority + fairness)
   - Returns highest-scoring video
4. Playback Engine starts playback
5. Updates queue state for fairness tracking
6. Broadcasts status via SignalR

### Real-Time Updates

SignalR broadcasts these events:
- **VideoStarted**: When playback begins
- **VideoEnded**: When playback completes
- **VideoError**: When playback fails
- **QueueUpdated**: When queue changes
- **VideoDownloadStarted/Completed/Failed**: Download status

All connected clients receive updates and refresh automatically.

## Database Schema

### Main Tables
- **AspNetUsers**: User accounts (Identity)
- **AspNetRoles**: User roles (Admin, Regular)
- **Playlists**: User-owned playlists
- **PlaylistItems**: Videos in playlists
- **PlaybackHistory**: Record of played videos
- **QueueState**: Current playback state + fairness tracking

### Key Relationships
- User → Playlists (one-to-many)
- Playlist → PlaylistItems (one-to-many)
- PlaylistItem → PlaybackHistory (one-to-many)
- QueueState → CurrentPlaylistItem (one-to-one)

## Logging

Logs are written to:
- **Console**: Real-time logging during development
- **Files**: `logs/log-YYYY-MM-DD.txt` (rolls daily)

Log levels:
- **Debug**: Download progress, queue calculations
- **Information**: Video started/ended, user actions
- **Warning**: Download failures (non-critical)
- **Error**: Critical failures, exceptions

## Troubleshooting

### "yt-dlp not found"
- Ensure `yt-dlp.exe` is in PATH or update `YtDlpPath` in `appsettings.json`
- Test: `yt-dlp --version` in terminal

### "VLC initialization failed"
- Verify VLC is installed at configured path
- For development without video output: Set `EnableVideoOutput: false`
- Check logs for detailed error messages

### "Videos not downloading"
- Check `VideoDownloaderService` is running (logs show "Video Downloader Service starting")
- Verify video URL is supported by yt-dlp
- Check storage path exists and is writable
- View error message in video status

### "Queue not updating"
- Ensure SignalR connection is established (check browser console)
- Verify firewall allows WebSocket connections
- Check logs for SignalR errors

## Performance Considerations

- **Concurrent Downloads**: Limited to 2 by default (configurable)
- **Queue Calculation**: O(n) where n = ready videos (efficient for 100s of videos)
- **Database**: SQLite works well for small-medium deployments
  - For large deployments, consider PostgreSQL
- **Video Storage**: Videos kept permanently (manual cleanup required)

## Security

- **Authentication**: ASP.NET Identity with secure password hashing
- **Authorization**: Role-based (Admin vs Regular users)
- **File Upload**: Not supported (only URLs) to prevent malicious files
- **SQL Injection**: Protected by EF Core parameterized queries
- **XSS**: Protected by Blazor's automatic HTML encoding

## Future Enhancements

Potential improvements:
- [ ] Admin UI for adjusting scheduling weights
- [ ] Video retention policy (auto-delete old videos)
- [ ] Playlist import/export
- [ ] Video search and filtering
- [ ] User statistics and history
- [ ] Multiple video sources (Vimeo, direct URLs)
- [ ] Thumbnail generation for non-YouTube videos
- [ ] Playlist sharing between users
- [ ] Skip/pause controls from web UI
- [ ] Video preview before adding

## Development

### Running in Development Mode

```bash
# Run with hot reload
dotnet watch run

# View logs in console
# Set EnableVideoOutput: false to avoid VLC window

# Access Swagger API docs (not fully implemented)
# https://localhost:5001/swagger
```

### Database Migrations

```bash
# Add migration
dotnet ef migrations add MigrationName

# Apply migrations
dotnet ef database update

# Remove last migration
dotnet ef migrations remove
```

### Testing

- Test user accounts can be created via `/account/register`
- Use development mode (`EnableVideoOutput: false`) to test without VLC
- Monitor logs in `logs/` directory for debugging

## License

This project is for educational/personal use.

## Credits

Built with:
- ASP.NET Core 9.0
- Blazor Server
- Entity Framework Core
- LibVLCSharp
- yt-dlp
- Serilog

---

**Note**: This system is designed for trusted users in a controlled environment. Videos are automatically downloaded and played - ensure compliance with copyright laws and your organization's policies.
