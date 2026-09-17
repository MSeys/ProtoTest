namespace ProtoTest.Data;

/// <summary>Implemented by feature-local collections of data defaults.</summary>
public interface IProtoDataDefaultsModule
{
    void Configure(ProtoDataConfiguration data);
}
