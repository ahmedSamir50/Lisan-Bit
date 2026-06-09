using Microsoft.AspNetCore.SignalR;
using LisanBits.Dashboard.Services;

namespace LisanBits.Dashboard.Hubs;

public class ScraperHub : Hub
{
    private readonly ScraperProgressService _progressService;

    public ScraperHub(ScraperProgressService progressService)
    {
        _progressService = progressService;
    }

    // The worker will call this method to broadcast progress
    public async Task BroadcastProgress(string sourceName, int newIndex)
    {
        await Clients.All.SendAsync("ProgressUpdated", sourceName, newIndex);
        _progressService.UpdateProgress(sourceName, newIndex);
    }
}
