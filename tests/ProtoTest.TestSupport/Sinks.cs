namespace ProtoTest.TestSupport;

using ProtoTest.Core;

/// <summary>An <see cref="IProtoSink"/> that keeps the last exported snapshot.</summary>
public sealed class CapturingSink : IProtoSink
{
    public IReadOnlyList<ProtoReportItem> Items { get; private set; } = [];

    public Task ExportAsync(IEnumerable<ProtoReportItem> items, CancellationToken cancellationToken = default)
    {
        Items = [.. items];
        return Task.CompletedTask;
    }
}

/// <summary>An <see cref="IProtoSink"/> that counts how often it was exported.</summary>
public sealed class CountingSink : IProtoSink
{
    public int ExportCount { get; private set; }

    public Task ExportAsync(IEnumerable<ProtoReportItem> items, CancellationToken cancellationToken = default)
    {
        ExportCount++;
        return Task.CompletedTask;
    }
}

/// <summary>A counting sink that also records what was configured against it.</summary>
public sealed class ConfigurableSink : IProtoSink
{
    public List<string> Configured { get; } = [];

    public int ExportCount { get; private set; }

    public Task ExportAsync(IEnumerable<ProtoReportItem> items, CancellationToken cancellationToken = default)
    {
        ExportCount++;
        return Task.CompletedTask;
    }
}
