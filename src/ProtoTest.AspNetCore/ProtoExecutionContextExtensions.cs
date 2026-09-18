namespace ProtoTest.AspNetCore;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AspNetCore.Internal;
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
    /// <param name="name">
    /// The registered server name. Defaults to the application selected for the test, or "Default".
    /// </param>
    public static WebApplicationFactory<TProgram> ServerFactory<TProgram>(
        this ProtoExecutionContext context,
        string? name = null) where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(context);
        var key = name ?? context.TryResolve<ProtoApplicationState>()?.ApplicationName ?? "Default";
        return context.Client<WebApplicationFactory<TProgram>>(AspNetCoreClientInitializer<TProgram>.FactoryName(key));
    }

    /// <summary>
    /// Creates a dedicated <see cref="IServiceScope"/> from the specified ASP.NET Core server's DI container.
    /// The caller owns the returned scope and must dispose it.
    /// </summary>
    /// <typeparam name="TProgram">The entry point class of the ASP.NET Core application.</typeparam>
    /// <param name="context">The active test execution context.</param>
    /// <param name="name">
    /// The registered server name. Defaults to the application selected for the test, or "Default".
    /// </param>
    public static IServiceScope CreateServerScope<TProgram>(
        this ProtoExecutionContext context,
        string? name = null) where TProgram : class
    {
        return context.ServerFactory<TProgram>(name).Services.CreateScope();
    }

    /// <summary>
    /// Gets the test's scope over the application under test, created on first use and disposed when the
    /// test ends. Scoped domain services - repositories, command handlers, a <c>DbContext</c> - resolve
    /// from it, so a provisioner can create data through the application's own logic.
    /// </summary>
    /// <typeparam name="TProgram">The entry point class of the ASP.NET Core application.</typeparam>
    /// <param name="context">The active test execution context.</param>
    /// <param name="name">
    /// The registered server name. Defaults to the application selected for the test, or "Default".
    /// </param>
    public static IServiceProvider ApplicationServices<TProgram>(
        this ProtoExecutionContext context,
        string? name = null) where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(context);
        var key = name ?? context.TryResolve<ProtoApplicationState>()?.ApplicationName ?? "Default";
        var scope = context.Services.GetKeyedService<ApplicationServicesScope<TProgram>>(key)
            ?? throw new InvalidOperationException(
                $"No in-process ASP.NET Core server is registered under '{key}', so its services cannot be reached. " +
                $"Register one with AddAspNetCoreServer<{typeof(TProgram).Name}>(\"{key}\").");
        return scope.Services;
    }

    /// <summary>
    /// Resolves a required service from the application under test through the test's own scope, so
    /// scoped services work and are disposed with the test.
    /// </summary>
    /// <typeparam name="TProgram">The entry point class of the ASP.NET Core application.</typeparam>
    /// <typeparam name="TService">The service type to resolve.</typeparam>
    /// <param name="context">The active test execution context.</param>
    /// <param name="name">
    /// The registered server name. Defaults to the application selected for the test, or "Default".
    /// </param>
    public static TService ServerService<TProgram, TService>(
        this ProtoExecutionContext context,
        string? name = null)
        where TProgram : class
        where TService : notnull
    {
        return context.ApplicationServices<TProgram>(name).GetRequiredService<TService>();
    }
}
