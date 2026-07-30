using NzbWebDAV.Clients.Usenet.Contexts;
using NzbWebDAV.Extensions;
using NzbWebDAV.Services.Metrics;

namespace NzbWebDAV.Tests.Clients.Usenet;

public class MaintenanceDownloadContextTests
{
    [Fact]
    public void ContextualCancellationTokenSource_CopiesMaintenanceContext_OneToken()
    {
        using var parent = new CancellationTokenSource();
        using var scope = parent.Token.SetContext(MaintenanceDownloadContext.Instance);
        using var linked = ContextualCancellationTokenSource.CreateLinkedTokenSource(parent.Token);

        Assert.NotNull(linked.Token.GetContext<MaintenanceDownloadContext>());
        Assert.Equal(DownloadWorkload.Maintenance, DownloadWorkloadClassifier.Classify(linked.Token));
    }

    [Fact]
    public void ContextualCancellationTokenSource_CopiesMaintenanceContext_TwoTokens()
    {
        using var a = new CancellationTokenSource();
        using var b = new CancellationTokenSource();
        using var scope = a.Token.SetContext(MaintenanceDownloadContext.Instance);
        using var linked = ContextualCancellationTokenSource.CreateLinkedTokenSource(a.Token, b.Token);

        Assert.Equal(DownloadWorkload.Maintenance, DownloadWorkloadClassifier.Classify(linked.Token));
    }

    [Fact]
    public void PlainLinkedTokenSource_LosesMaintenanceContext()
    {
        using var parent = new CancellationTokenSource();
        using var scope = parent.Token.SetContext(MaintenanceDownloadContext.Instance);
        using var plain = CancellationTokenSource.CreateLinkedTokenSource(parent.Token);

        Assert.Null(plain.Token.GetContext<MaintenanceDownloadContext>());
        Assert.Equal(DownloadWorkload.Background, DownloadWorkloadClassifier.Classify(plain.Token));
    }
}
