namespace ProtoTest.Core.Tests;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

[TestFixture]
public class ProtoExecutionContextTests
{
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;

    [SetUp]
    public void SetUp()
    {
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
    }

    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
    }

    [Test]
    public void SetAndGet_ShouldStoreAndRetrieveTypedContext()
    {
        // Arrange
        var context = new ProtoExecutionContext("TestMethod", _scope, "00001", TestMethods.Placeholder);
        var customState = new SampleContext("InitialData");

        // Act
        context.SetContext(customState);
        var retrieved = context.TryResolve<SampleContext>();

        // Assert
        Assert.That(retrieved, Is.SameAs(customState));
    }

    [Test]
    public void GetRequired_ShouldReturnInstance_WhenContextExists()
    {
        // Arrange
        var context = new ProtoExecutionContext("TestMethod", _scope, "00001", TestMethods.Placeholder);
        var customState = new SampleContext("Active");
        context.SetContext(customState);

        // Act
        var result = context.Resolve<SampleContext>();

        // Assert
        Assert.That(result, Is.SameAs(customState));
    }

    [Test]
    public void GetRequired_ShouldThrowInvalidOperationException_WhenContextIsMissing()
    {
        // Arrange
        var context = new ProtoExecutionContext("TestMethod", _scope, "00001", TestMethods.Placeholder);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => context.Resolve<SampleContext>());
        Assert.That(exception!.Message, Does.Contain(nameof(SampleContext)));
    }

    [Test]
    public void RecordObservation_ShouldStoreAndDispatchToEveryMatchingCollector()
    {
        var first = new RecordingCollector("Orders");
        var second = new RecordingCollector("orders");
        var ignored = new RecordingCollector("Customers");
        using var provider = new ServiceCollection()
            .AddSingleton<IProtoCollector>(first)
            .AddSingleton<IProtoCollector>(second)
            .AddSingleton<IProtoCollector>(ignored)
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = new ProtoExecutionContext(
            "TestMethod",
            scope,
            "00001",
            TestMethods.Placeholder);

        context.RecordObservation("Orders", "http.response", "GET /orders", data: 200);

        Assert.That(context.RecordedObservations, Has.Count.EqualTo(1));
        Assert.That(first.Observations, Has.Count.EqualTo(1));
        Assert.That(second.Observations, Has.Count.EqualTo(1));
        Assert.That(ignored.Observations, Is.Empty);
        Assert.That(first.Observations.Single().Kind, Is.EqualTo("http.response"));
        Assert.That(first.Observations.Single().Data, Is.EqualTo(200));
    }

    [Test]
    public void RecordObservation_ShouldRedactSensitiveMetadataBeforeTheReport()
    {
        var collector = new CoverageCollector("Orders");
        using var provider = new ServiceCollection()
            .AddSingleton<IProtoCollector>(collector)
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = new ProtoExecutionContext("TestMethod", scope, "00001", TestMethods.Placeholder);

        context.RecordObservation(
            "Orders",
            "http.response",
            "GET /orders",
            metadata: new Dictionary<string, object>
            {
                ["token"] = "hunter2",
                ["status"] = 200
            });

        var item = collector.GetReportItems().Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.Metadata!["token"], Is.EqualTo("[REDACTED]"));
            Assert.That(item.Metadata!["status"], Is.EqualTo(200));
        });
    }

    private sealed class CoverageCollector(string targetName) : ProtoCoverageCollector(targetName)
    {
        public override string Category => "Coverage";
    }

    private sealed record SampleContext(string Value) : IProtoContext;

    private sealed class RecordingCollector(string targetName) : IProtoCollector
    {
        public List<ProtoObservation> Observations { get; } = [];

        public bool CanCollect(ProtoObservation observation)
            => string.Equals(observation.TargetName, targetName, StringComparison.OrdinalIgnoreCase);

        public void Collect(ProtoObservation observation) => Observations.Add(observation);
    }
}
