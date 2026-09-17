namespace ProtoTest.Data;

/// <summary>Creates deterministic, explainable data builders and resolves what they provisioned.</summary>
public interface IProtoData
{
    /// <summary>Starts building an instance of <typeparamref name="T"/>.</summary>
    ProtoDataObjectBuilder<T> For<T>();

    /// <summary>
    /// Resolves an object provisioned earlier in this test so a value can reference it, for example a
    /// foreign key. Pass <paramref name="identity"/> when several objects of the type were provisioned.
    /// </summary>
    T Ref<T>(string? identity = null);
}
