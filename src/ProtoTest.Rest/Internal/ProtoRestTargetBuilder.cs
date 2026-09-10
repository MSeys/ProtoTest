namespace ProtoTest.Rest.Internal;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

internal sealed class ProtoRestTargetBuilder(string targetName, IServiceCollection services)
    : IProtoTargetBuilder
{
    public string TargetName { get; } = targetName;
    public IServiceCollection Services { get; } = services;
}
