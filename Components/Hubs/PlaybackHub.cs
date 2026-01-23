using Microsoft.AspNetCore.SignalR;

namespace SingleStepViewer.Components.Hubs;

public class PlaybackHub : Hub
{
    private readonly ILogger<PlaybackHub> _logger;

    public PlaybackHub(ILogger<PlaybackHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    // Methods for broadcasting playback events
    public async Task BroadcastVideoStarted(int playlistItemId, string title)
    {
        await Clients.All.SendAsync("VideoStarted", playlistItemId, title);
    }

    public async Task BroadcastVideoEnded(int playlistItemId)
    {
        await Clients.All.SendAsync("VideoEnded", playlistItemId);
    }

    public async Task BroadcastQueueUpdated()
    {
        await Clients.All.SendAsync("QueueUpdated");
    }
}
