namespace ProtoTest.Data;

using System.Security.Cryptography;
using System.Text;

/// <summary>Per-member context supplied to configured value providers.</summary>
public sealed class ProtoDataValueContext
{
    private int _generatedValueIndex;

    private readonly IProtoData _data;

    internal ProtoDataValueContext(
        IServiceProvider services,
        IProtoData data,
        string testId,
        long objectSequence,
        Type targetType,
        Type valueType,
        string memberName)
    {
        Services = services;
        _data = data;
        TestId = testId;
        ObjectSequence = objectSequence;
        TargetType = targetType;
        ValueType = valueType;
        MemberName = memberName;
    }

    public IServiceProvider Services { get; }
    public string TestId { get; }
    public long ObjectSequence { get; }
    public Type TargetType { get; }
    public Type ValueType { get; }
    public string MemberName { get; }

    /// <summary>Creates a reproducible GUID unique to this object member and call index.</summary>
    public Guid NextGuid()
    {
        var input = $"{TestId}|{ObjectSequence}|{TargetType.FullName}|{MemberName}|{_generatedValueIndex++}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(bytes.AsSpan(0, 16));
    }

    /// <summary>Creates a readable deterministic string for this member.</summary>
    public string NextString()
        => $"{TargetType.Name}.{MemberName}-{ObjectSequence:D4}-{_generatedValueIndex++:D2}";

    /// <summary>Resolves an object provisioned earlier in this test, so a default can reference it.</summary>
    public T Ref<T>(string? identity = null) => _data.Ref<T>(identity);
}
