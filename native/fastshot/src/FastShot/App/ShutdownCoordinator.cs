namespace FastShot;

/// <summary>Finishes pending recordings before shutdown or handing control to an installer.</summary>
internal sealed class ShutdownCoordinator(Func<Task> prepare, Action shutdown)
{
    Task? preparation, completion;
    public Task PrepareAsync() => preparation ??= prepare();
    public Task QuitAsync() => completion ??= CompleteAsync();
    public bool ResetPreparation()
    {
        if (completion is not null) return false;
        preparation = null;
        return true;
    }

    async Task CompleteAsync()
    {
        await PrepareAsync();
        shutdown();
    }
}
