namespace ProtoTest.Http;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>Shared, protocol-neutral <see cref="IProtoTargetBuilder"/> for HTTP-based protocol integrations.</summary>
public sealed class ProtoHttpTargetBuilder(string targetName, IServiceCollection services) : IProtoTargetBuilder
{
    public string TargetName { get; } = targetName;
    public IServiceCollection Services { get; } = services;
}
