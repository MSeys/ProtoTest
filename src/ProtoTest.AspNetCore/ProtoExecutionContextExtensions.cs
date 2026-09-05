namespace ProtoTest.AspNetCore;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// Provides ASP.NET Core extension methods on <see cref="ProtoExecutionContext"/>.
/// </summary>
public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Retrieves the underlying <see cref="WebApplicationFactory{TEntryPoint}"/> registered under the specified name.
    /// </summary>
    /// <typeparam name="TProgram">The entry point class of the ASP.NET Core application.</typeparam>
    /// <param name="context">The active test execution context.</param>
    /// <param name="name">The registered server name. Defaults to "Default".</param>
    public static WebApplicationFactory<TProgram> Server<TProgram>(
        this ProtoExecutionContext context,
        string name = "Default") where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Client<WebApplicationFactory<TProgram>>($"{name}:Factory");
    }

    /// <summary>
    /// Creates a dedicated <see cref="IServiceScope"/> from the specified ASP.NET Core server's DI container.
    /// </summary>
    /// <typeparam name="TProgram">The entry point class of the ASP.NET Core application.</typeparam>
    /// <param name="context">The active test execution context.</param>
    /// <param name="name">The registered server name. Defaults to "Default".</param>
    public static IServiceScope CreateServerScope<TProgram>(
        this ProtoExecutionContext context,
        string name = "Default") where TProgram : class
    {
        return context.Server<TProgram>(name).Services.CreateScope();
    }

    /// <summary>
    /// Resolves a required service directly from the root Service Provider of the specified ASP.NET Core server.
    /// </summary>
    /// <typeparam name="TProgram">The entry point class of the ASP.NET Core application.</typeparam>
    /// <typeparam name="TService">The service type to resolve.</typeparam>
    /// <param name="context">The active test execution context.</param>
    /// <param name="name">The registered server name. Defaults to "Default".</param>
    public static TService ServerService<TProgram, TService>(
        this ProtoExecutionContext context,
        string name = "Default")
        where TProgram : class
        where TService : notnull
    {
        return context.Server<TProgram>(name).Services.GetRequiredService<TService>();
    }
}