namespace ProtoTest.Devices;

using ProtoTest.Core;

/// <summary>Skips a test when the environment does not provide a device of this type.</summary>
/// <example>
/// <code>
/// [RequiresDevice&lt;AcCharger&gt;]
/// public async Task ...() { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresDeviceAttribute<TDevice> : RequiresCapabilityAttribute
    where TDevice : ProtoDevice
{
    public RequiresDeviceAttribute()
        : base(ProtoCapabilityKinds.Device)
    {
        CapabilityName = typeof(TDevice).Name;
    }

    protected override string DefaultReason
        => $"This test requires the '{typeof(TDevice).Name}' device, which this environment does not provide.";
}
