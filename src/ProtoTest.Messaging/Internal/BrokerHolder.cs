namespace ProtoTest.Messaging.Internal;

/// <summary>
/// Holds the broker instance the DI container creates, so the run-scoped resource can release it. The
/// release runs at host disposal, when the provider may already be gone: holding the instance here keeps
/// it independent of that timing and works for adapters implementing either disposal interface.
/// </summary>
internal sealed class BrokerHolder
{
    private IProtoMessageBroker? _broker;

    public void Attach(IProtoMessageBroker broker) => _broker = broker;

    public ValueTask ReleaseAsync()
    {
        var broker = Interlocked.Exchange(ref _broker, null);
        return broker switch
        {
            IAsyncDisposable asyncDisposable => asyncDisposable.DisposeAsync(),
            IDisposable disposable => Dispose(disposable),
            _ => ValueTask.CompletedTask
        };

        static ValueTask Dispose(IDisposable disposable)
        {
            disposable.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
