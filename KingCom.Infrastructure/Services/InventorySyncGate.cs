namespace KingCom.Infrastructure.Services;

public sealed class InventorySyncGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<T> RunExclusiveAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (!await _semaphore.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Dong bo ton kho dang chay, vui long doi hoan tat.");
        }

        try
        {
            return await work();
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
