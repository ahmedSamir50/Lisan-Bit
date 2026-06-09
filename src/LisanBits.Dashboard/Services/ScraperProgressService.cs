namespace LisanBits.Dashboard.Services;

public class ScraperProgressService
{
    public event Action<string, int>? OnProgressUpdated;

    public void UpdateProgress(string sourceName, int newIndex)
    {
        OnProgressUpdated?.Invoke(sourceName, newIndex);
    }
}
