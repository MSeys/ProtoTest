namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

[TestFixture]
public class ProtoTests
{
    private ProtoHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITestService, TestService>();
        var rootProvider = services.BuildServiceProvider();

        _host = new ProtoHost(rootProvider, Array.Empty<IProtoHook>());
    }

    [TearDown]
    public async Task TearDown()
    {
        await _host.DisposeAsync();
    }

    [Test]
    public async Task Service_ShouldResolveRegisteredService()
    {
        // Arrange
        _host.BeginTestContext("HelperTest", "id-789");
        await _host.ExecuteBeforeHooksAsync();

        try
        {
            // Act
            var service = Proto.Service<ITestService>();

            // Assert
            Assert.That(service, Is.Not.Null);
            Assert.That(service.Execute(), Is.EqualTo("Executed"));
        }
        finally
        {
            await _host.ExecuteAfterHooksAsync();
            _host.EndTestContext();
        }
    }

    [Test]
    public async Task TryService_ShouldReturnNull_WhenServiceIsNotRegistered()
    {
        // Arrange
        _host.BeginTestContext("HelperTest", "id-789");
        await _host.ExecuteBeforeHooksAsync();

        try
        {
            // Act
            var service = Proto.TryService<IUnregisteredService>();

            // Assert
            Assert.That(service, Is.Null);
        }
        finally
        {
            await _host.ExecuteAfterHooksAsync();
            _host.EndTestContext();
        }
    }

    [Test]
    public async Task ContextAndSetContext_ShouldManageTestState()
    {
        // Arrange
        _host.BeginTestContext("HelperTest", "id-789");
        await _host.ExecuteBeforeHooksAsync();

        try
        {
            var state = new CustomTestState("ActiveUser");

            // Act
            Proto.SetContext(state);
            var retrieved = Proto.Context<CustomTestState>();

            // Assert
            Assert.That(retrieved, Is.SameAs(state));
        }
        finally
        {
            await _host.ExecuteAfterHooksAsync();
            _host.EndTestContext();
        }
    }

    private interface ITestService
    {
        string Execute();
    }

    private sealed class TestService : ITestService
    {
        public string Execute() => "Executed";
    }

    private interface IUnregisteredService { }

    private sealed record CustomTestState(string Username) : IProtoContext;
}