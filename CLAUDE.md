# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SingleStepViewer is a web-based video playback system built with ASP.NET Core 9.0 and Blazor Server. Multiple users manage playlists via a web interface, with videos played automatically on a dedicated display using VLC. The system uses a smart weighted fair queue algorithm that balances video priority with fairness across users.

**Key Technologies:**
- ASP.NET Core 9.0 with Blazor Server (interactive UI)
- Entity Framework Core 9.0 with SQLite
- ASP.NET Identity (authentication & authorization)
- SignalR (real-time updates)
- LibVLCSharp (video playback)
- yt-dlp via Ytdlp.NET (video downloading)
- Serilog (structured logging)

## Build and Run Commands

Build the project:
```bash
dotnet build
```

Run the application:
```bash
dotnet run
```

Run with hot reload (development):
```bash
dotnet watch run
```

Build for release:
```bash
dotnet build -c Release
```

Clean build artifacts:
```bash
dotnet clean
```

Restore NuGet packages:
```bash
dotnet restore
```

## Database Commands

The application uses Entity Framework Core with SQLite. Database migrations are automatically applied on startup, but you can manage them manually:

Add a new migration:
```bash
dotnet ef migrations add MigrationName
```

Apply migrations manually:
```bash
dotnet ef database update
```

Remove the last migration:
```bash
dotnet ef migrations remove
```

View migration SQL:
```bash
dotnet ef migrations script
```

## Project Structure

### Core Files
- **Program.cs** - Application entry point, service registration, middleware configuration
- **appsettings.json** - Configuration for database, VLC, scheduling, video download
- **SingleStepViewer.csproj** - Project file targeting .NET 9.0 (Web SDK)
- **SingleStepViewer.sln** - Visual Studio solution file

### Main Directories
- **Data/** - Entity Framework DbContext and entity models
  - `ApplicationDbContext.cs` - Main EF Core context
  - `Entities/` - Database models (ApplicationUser, Playlist, PlaylistItem, PlaybackHistory, QueueState)

- **Services/** - Core business logic services
  - `Interfaces/` - Service contracts
  - `UserService.cs` - User management
  - `PlaylistService.cs` - Playlist CRUD operations
  - `PlaylistItemService.cs` - Video management within playlists
  - `VideoService.cs` - Video metadata extraction (yt-dlp)
  - `DownloadService.cs` - Video downloading (yt-dlp wrapper)
  - `PlaybackService.cs` - VLC integration and playback control (singleton)
  - `QueueManager.cs` - Weighted fair queue algorithm
  - `ConfigurationService.cs` - Runtime configuration management

- **BackgroundServices/** - Long-running hosted services
  - `PlaybackEngineService.cs` - Main playback loop, monitors queue, starts videos
  - `VideoDownloaderService.cs` - Background video downloader (max 2 concurrent)

- **Components/** - Blazor UI components
  - `Pages/` - Routable pages (Index, MyPlaylists, PlaylistDetail, Queue, History, Admin, Account)
  - `Shared/` - Shared layout components (MainLayout, NavMenu, LoginDisplay)
  - `Hubs/PlaybackHub.cs` - SignalR hub for real-time updates
  - `App.razor` - Root component
  - `Routes.razor` - Routing configuration
  - `_Imports.razor` - Global using statements

- **Configuration/** - Strongly-typed configuration classes
  - `PlaybackOptions.cs` - VLC settings
  - `SchedulingOptions.cs` - Queue algorithm weights
  - `VideoOptions.cs` - Download settings

- **Migrations/** - EF Core database migrations

## Key Configuration Settings

Edit `appsettings.json` to configure the application:

### Database
- `ConnectionStrings:DefaultConnection` - SQLite database path (default: `singlestepviewer.db`)

### VLC Playback (`Playback` section)
- `VlcPath` - Path to VLC installation (default: `C:\Program Files\VideoLAN\VLC`)
- `DefaultVolume` - Initial volume (0-100, default: 80)
- `EnableFullscreen` - Start videos in fullscreen (default: true)
- `EnableVideoOutput` - Show VLC window (set false for development, default: true)

### Scheduling Algorithm (`Scheduling` section)
- `PriorityWeight` - Weight for user-assigned priority (default: 10.0)
- `FairnessWeight` - Weight for time since last played (default: 5.0)
- `UserCooldownMinutes` - Baseline for fairness calculation (default: 15)
- `QueueCheckIntervalSeconds` - How often to check for next video (default: 5)

### Video Download (`Video` section)
- `StoragePath` - Where to save downloaded videos (default: `./videos`)
- `MaxConcurrentDownloads` - Concurrent download limit (default: 2)
- `YtDlpPath` - Path to yt-dlp executable (default: `yt-dlp.exe`)
- `PreferredFormat` - Video format preference

## Architecture Overview

### Application Flow
1. **User adds video** → Playlist item created with status `Pending`
2. **VideoDownloaderService** picks up pending videos → Downloads → Status: `Ready`
3. **PlaybackEngineService** queries `QueueManager` for next video
4. **QueueManager** calculates scores based on priority + fairness → Selects highest
5. **PlaybackService** starts video in VLC → Status: `Playing`
6. **On completion** → Status: `Played`, record added to `PlaybackHistory`
7. **SignalR broadcasts** updates to all connected clients

### Weighted Fair Queue Algorithm
```
Score = (Priority × PriorityWeight) + ((MinutesSinceLastPlayed ÷ UserCooldownMinutes) × FairnessWeight)
```

This ensures high-priority videos are preferred while preventing any user from dominating the queue.

### Dependency Injection Lifetimes
- **Scoped**: All services except PlaybackService (per-request lifetime)
- **Singleton**: PlaybackService (maintains VLC state across app lifetime)
- **Hosted**: PlaybackEngineService, VideoDownloaderService (long-running background tasks)

### Real-Time Communication
SignalR hub (`PlaybackHub`) broadcasts events:
- `VideoStarted` / `VideoEnded` / `VideoError`
- `QueueUpdated`
- `VideoDownloadStarted` / `VideoDownloadCompleted` / `VideoDownloadFailed`

## Default Admin Account

Created automatically on first run:
- **Username:** admin
- **Email:** admin@singlestep.local
- **Password:** Admin123!
- **Role:** Admin

## Common Development Tasks

### Testing Without VLC Output
In `appsettings.json`, set:
```json
"Playback": {
  "EnableVideoOutput": false
}
```

### Viewing Logs
Logs are written to:
- Console (all environments)
- `logs/log-YYYY-MM-DD.txt` (rolling daily)

### Testing Video Download
Ensure `yt-dlp.exe` is in PATH or update `YtDlpPath` in config. Test with:
```bash
yt-dlp --version
```

### Adding New Services
1. Create interface in `Services/Interfaces/`
2. Implement in `Services/`
3. Register in `Program.cs` with appropriate lifetime
4. Inject via constructor where needed

### Adding New Blazor Pages
1. Create `.razor` file in `Components/Pages/`
2. Add `@page "/route"` directive
3. Optionally add `@attribute [Authorize]` for auth
4. Update `NavMenu.razor` if navigation link needed

### Database Schema Changes
1. Modify entity classes in `Data/Entities/`
2. Update `ApplicationDbContext.cs` if needed
3. Run `dotnet ef migrations add MigrationName`
4. Migration is auto-applied on next app start

## External Dependencies

Required for full functionality:
1. **VLC Media Player** - Install to `C:\Program Files\VideoLAN\VLC` or configure path
2. **yt-dlp** - Download from GitHub releases, place in PATH or configure path

## Development Environment

This project is configured for Visual Studio 2022 (v17) but can be developed using:
- Visual Studio 2022
- Visual Studio Code (with C# extension)
- JetBrains Rider
- .NET CLI only

Requires .NET 9.0 SDK or later.
