namespace ProtoTest.Data;

/// <summary>Creates deterministic, explainable data builders.</summary>
public interface IProtoData
{
    /// <summary>Starts building an instance of <typeparamref name="T"/>.</summary>
    ProtoDataObjectBuilder<T> For<T>();
}
