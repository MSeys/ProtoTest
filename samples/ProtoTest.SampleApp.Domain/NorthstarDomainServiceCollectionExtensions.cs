namespace ProtoTest.SampleApp.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public static class NorthstarDomainServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Northstar domain: its store, its event bus, the clock they run on, and the
    /// database context factory the store persists through. The application calls this for its own
    /// host, and a test can call it too - against the same database - to arrange and verify data
    /// through the domain instead of only through the API.
    /// </summary>
    public static IServiceCollection AddNorthstarDomain(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDatabase)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureDatabase);
        services.AddDbContextFactory<NorthstarDbContext>(configureDatabase);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<NorthstarEventBus>();
        services.AddSingleton<NorthstarStore>();
        return services;
    }
}
