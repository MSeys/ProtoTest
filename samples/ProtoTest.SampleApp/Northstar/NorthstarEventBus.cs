namespace ProtoTest.SampleApp.Northstar;

using System.Collections.Concurrent;
using System.Threading.Channels;
using ProtoTest.SampleApp.Contracts;

/// <summary>Per-organization streams that back the GraphQL subscriptions.</summary>
internal sealed class NorthstarEventBus
{
    private readonly ConcurrentDictionary<string, Channel<DeploymentResponse>> _deployments = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Channel<InvoiceResponse>> _invoices = new(StringComparer.Ordinal);

    public void PublishDeployment(string organizationSlug, DeploymentResponse deployment)
        => Channel(_deployments, organizationSlug).Writer.TryWrite(deployment);

    public void PublishInvoice(string organizationSlug, InvoiceResponse invoice)
        => Channel(_invoices, organizationSlug).Writer.TryWrite(invoice);

    public IAsyncEnumerable<DeploymentResponse> Deployments(string organizationSlug, CancellationToken cancellationToken)
        => Channel(_deployments, organizationSlug).Reader.ReadAllAsync(cancellationToken);

    public IAsyncEnumerable<InvoiceResponse> Invoices(string organizationSlug, CancellationToken cancellationToken)
        => Channel(_invoices, organizationSlug).Reader.ReadAllAsync(cancellationToken);

    private static Channel<T> Channel<T>(ConcurrentDictionary<string, Channel<T>> channels, string key)
        => channels.GetOrAdd(key, _ => System.Threading.Channels.Channel.CreateUnbounded<T>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }));
}
