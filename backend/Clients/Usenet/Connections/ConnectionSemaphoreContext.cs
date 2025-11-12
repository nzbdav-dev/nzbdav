namespace NzbWebDAV.Clients.Usenet.Connections;

public class ConnectionSemaphoreContext(int maxConnections): IDisposable
{
    public int MaxConnections => maxConnections;
    public readonly SemaphoreSlim Semaphore = new(maxConnections, maxConnections);

    public void Dispose()
    {
        Semaphore.Dispose();
    }
}