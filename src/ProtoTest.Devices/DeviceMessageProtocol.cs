namespace ProtoTest.Devices;

/// <summary>
/// A protocol catalog built from message types: each type is one message kind, named after the type. A
/// matched <c>ExpectMessageAsync</c> counts toward its kind, and <see cref="DeviceCoverageCollector"/>
/// reports the types no test expected.
/// </summary>
public sealed class DeviceMessageProtocol : IProtoDeviceProtocol
{
    private readonly List<(Type Type, DeviceProtocolEntry Entry)> _messages = [];

    /// <summary>Creates an empty catalog named <paramref name="name"/>; add message types with <see cref="Add{TMessage}"/>.</summary>
    public DeviceMessageProtocol(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyList<DeviceProtocolEntry> Entries => [.. _messages.Select(message => message.Entry)];

    /// <summary>Adds <typeparamref name="TMessage"/> as a message kind, with its prefix as the description.</summary>
    public DeviceMessageProtocol Add<TMessage>()
    {
        var type = typeof(TMessage);
        if (_messages.Any(message => message.Type == type))
        {
            return this;
        }

        var prefix = DeviceMessage.DescribePrefix(type);
        _messages.Add((type, new DeviceProtocolEntry(type.Name, prefix.Length == 0 ? null : prefix)));
        return this;
    }

    /// <inheritdoc />
    public string? Classify(DeviceFrame frame)
        => _messages.FirstOrDefault(message => DeviceMessage.Matches(message.Type, frame)).Entry?.Kind;
}
