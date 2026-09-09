namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Reflection;

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

        _host = new ProtoHost(rootProvider);
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
        await _host.StartTestAsync("HelperTest", "00789", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            // Act
            var service = Proto.Context.Service<ITestService>();

            // Assert
            Assert.That(service, Is.Not.Null);
            Assert.That(service.Execute(), Is.EqualTo("Executed"));
        }
        finally
        {
            await _host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task TryService_ShouldReturnNull_WhenServiceIsNotRegistered()
    {
        // Arrange
        await _host.StartTestAsync("HelperTest", "00789", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            // Act
            var service = Proto.Context.TryService<IUnregisteredService>();

            // Assert
            Assert.That(service, Is.Null);
        }
        finally
        {
            await _host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task ContextAndSetContext_ShouldManageTestState()
    {
        // Arrange
        await _host.StartTestAsync("HelperTest", "00789", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            var state = new CustomTestState("ActiveUser");

            // Act
            Proto.Context.SetContext(state);
            var retrieved = Proto.Context.Context<CustomTestState>();

            // Assert
            Assert.That(retrieved, Is.SameAs(state));
        }
        finally
        {
            await _host.CompleteTestAsync();
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
