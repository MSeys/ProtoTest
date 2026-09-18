namespace ProtoTest.Grpc.Clients;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>Shared <see cref="IProtoTargetBuilder"/> for the gRPC integration.</summary>
public sealed class ProtoGrpcTargetBuilder(string targetName, IServiceCollection services) : IProtoTargetBuilder
{
    public string TargetName { get; } = targetName;
    public IServiceCollection Services { get; } = services;
}
