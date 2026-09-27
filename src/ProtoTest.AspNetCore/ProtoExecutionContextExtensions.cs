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
        var factory = context.TryClient<WebApplicationFactory<TProgram>>(AspNetCoreClientInitializer<TProgram>.FactoryName(key));
        if (factory is not null)
        {
            return factory;
        }

        var address = ProtoApplication.BaseUrl(context.Configuration, key);
        if (!string.IsNullOrWhiteSpace(address))
        {
            throw new InvalidOperationException(
                $"Application '{key}' runs at '{address}', so it has no in-process server to reach. " +
                $"ServerFactory and ApplicationServices need AddAspNetCoreServer without a configured " +
                $"'{ProtoApplication.SectionPath}:{key}:BaseUrl'.");
        }

        return context.Client<WebApplicationFactory<TProgram>>(AspNetCoreClientInitializer<TProgram>.FactoryName(key));
    }

    /// <summary>
    /// Gets the in-process server factory for an application, or <see langword="null"/> when the
    /// application is not hosted in-process (published or container-backed runs). This is the lookup an
    /// integration uses to prefer an in-process path only when one exists.
    /// </summary>
    public static WebApplicationFactory<TProgram>? TryServerFactory<TProgram>(
        this ProtoExecutionContext context,
        string? name = null) where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(context);
        var key = name ?? context.TryResolve<ProtoApplicationState>()?.ApplicationName ?? "Default";
        return context.TryClient<WebApplicationFactory<TProgram>>(AspNetCoreClientInitializer<TProgram>.FactoryName(key));
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
    /// Substitutes a service in the application under test for this test, serving the given instance
    /// to the application. The test runs against a dedicated server built with the substitution, so
    /// the run's shared server never sees it and the next test starts clean. The replacement is a
    /// singleton of the dedicated server: apply the override before the test first resolves
    /// application services.
    /// </summary>
    /// <typeparam name="TService">The service contract the application resolves.</typeparam>
    /// <param name="context">The active test execution context.</param>
    /// <param name="instance">The instance the application resolves for the test.</param>
    /// <param name="name">
    /// The server to substitute on. When omitted, the test's selected application is used, falling
    /// back to <c>Default</c> - the same rule the server accessors resolve by.
    /// </param>
    public static void Override<TService>(
        this ProtoExecutionContext context,
        TService instance,
        string? name = null)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(instance);
        var serverName = AspNetCoreSubstitution.ResolveServerName(context, name);
        var target = AspNetCoreSubstitution.ResolveTarget(context, serverName);
        target.ApplySubstitution(
            context,
            new InstanceSubstitution(typeof(TService), serverName, instance),
            ProtoTracePhase.Execution);
    }

    /// <summary>
    /// Substitutes a service in the application under test for this test, building it with the given
    /// factory when the application resolves it. The test runs against a dedicated server built with
    /// the substitution, so the run's shared server never sees it and the next test starts clean.
    /// </summary>
    /// <typeparam name="TService">The service contract the application resolves.</typeparam>
    /// <param name="context">The active test execution context.</param>
    /// <param name="factory">The factory the application resolves for the test.</param>
    /// <param name="name">
    /// The server to substitute on. When omitted, the test's selected application is used, falling
    /// back to <c>Default</c> - the same rule the server accessors resolve by.
    /// </param>
    public static void Override<TService>(
        this ProtoExecutionContext context,
        Func<TService> factory,
        string? name = null)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(factory);
        var serverName = AspNetCoreSubstitution.ResolveServerName(context, name);
        var target = AspNetCoreSubstitution.ResolveTarget(context, serverName);
        target.ApplySubstitution(
            context,
            new FactorySubstitution(typeof(TService), serverName, () => factory()!),
            ProtoTracePhase.Execution);
    }

    /// <summary>
    /// Substitutes a service in the application under test for this test, resolving an implementation
    /// type through the application's container. The test runs against a dedicated server built with
    /// the substitution, so the run's shared server never sees it and the next test starts clean.
    /// </summary>
    /// <typeparam name="TService">The service contract the application resolves.</typeparam>
    /// <typeparam name="TImplementation">The concrete replacement the container constructs.</typeparam>
    /// <param name="context">The active test execution context.</param>
    /// <param name="name">
    /// The server to substitute on. When omitted, the test's selected application is used, falling
    /// back to <c>Default</c> - the same rule the server accessors resolve by.
    /// </param>
    public static void Override<TService, TImplementation>(
        this ProtoExecutionContext context,
        string? name = null)
        where TService : class
        where TImplementation : class, TService
    {
        ArgumentNullException.ThrowIfNull(context);
        var serverName = AspNetCoreSubstitution.ResolveServerName(context, name);
        var target = AspNetCoreSubstitution.ResolveTarget(context, serverName);
        target.ApplySubstitution(
            context,
            new TypeSubstitution(typeof(TService), serverName, typeof(TImplementation)),
            ProtoTracePhase.Execution);
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
