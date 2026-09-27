namespace ProtoTest.AspNetCore;

using ProtoTest.Core;

/// <summary>
/// The low-ceremony composition for a suite whose subject is one ASP.NET Core application: register
/// it in-process under the default application name, and compose its protocol registrations on the
/// same call.
/// </summary>
/// <example>
/// <code>
/// protected override void Configure(IProtoHostBuilder builder) => ProtoTestHost.For&lt;Program&gt;(builder);
///
/// // or with the application's clients declared in the same line:
/// protected override void Configure(IProtoHostBuilder builder) => ProtoTestHost.For&lt;Program&gt;(
///     builder,
///     configure: app => app.AddRest(rest => rest.AddClient()));
/// </code>
/// </example>
public static class ProtoTestHost
{
    /// <summary>The application name <see cref="For{TProgram}"/> registers when the caller names none.</summary>
    public const string DefaultApplicationName = "Api";

    /// <summary>
    /// Registers <typeparamref name="TProgram"/> as the suite's application, hosted in-process, exactly
    /// like <c>AddApplication(applicationName, app =&gt; app.AddAspNetCoreServer&lt;TProgram&gt;())</c>
    /// with <see cref="DefaultApplicationName"/> when no name is given. The callback composes the
    /// application's protocol registrations - its REST, GraphQL or gRPC clients - after the server.
    /// </summary>
    /// <typeparam name="TProgram">The entry point class of the ASP.NET Core application under test.</typeparam>
    /// <param name="builder">The host builder to compose.</param>
    /// <param name="applicationName">The application name; defaults to <see cref="DefaultApplicationName"/>.</param>
    /// <param name="configure">Optional application registrations, such as <c>app.AddRest(rest =&gt; rest.AddClient())</c>.</param>
    /// <returns>The same host builder, so further composition chains.</returns>
    public static IProtoHostBuilder For<TProgram>(
        IProtoHostBuilder builder,
        string applicationName = DefaultApplicationName,
        Action<IProtoApplicationBuilder>? configure = null)
        where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        return builder.AddApplication(applicationName, application =>
        {
            application.AddAspNetCoreServer<TProgram>();
            configure?.Invoke(application);
        });
    }
}
