namespace ProtoTest.Core.Tests;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Contract of the one once-only registration helper (Audit 5 A5-17): the internal match-delegate rule
/// is the implementation, and the public guard is its façade - first registration wins, later calls are
/// no-ops, for both a service collection and a builder whose marker lives in a weak table.
/// </summary>
[TestFixture]
public sealed class ProtoRegistrationTests
{
    [Test]
    public void TryAdd_WithAMatchDelegate_ShouldKeepTheFirstMatchingRegistration()
    {
        var services = new ServiceCollection();
        var first = new Marker("named");
        var second = new Marker("named");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ProtoRegistration.TryAdd(services, first, marker => marker.Name == "named"),
                Is.True);
            Assert.That(
                ProtoRegistration.TryAdd(services, second, marker => marker.Name == "named"),
                Is.False,
                "the first matching registration wins");
            Assert.That(
                ProtoRegistration.Find(services, (Marker marker) => marker.Name == "named"),
                Is.SameAs(first),
                "the helper finds the winner by the same identity rule");
            Assert.That(services.Count, Is.EqualTo(1), "the losing call adds nothing");
        }
    }

    [Test]
    public void TryAdd_WithoutAMatch_ShouldRegisterTheNewMarker()
    {
        var services = new ServiceCollection();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ProtoRegistration.TryAdd(services, new Marker("first"), marker => marker.Name == "first"), Is.True);
            Assert.That(
                ProtoRegistration.TryAdd(services, new Marker("second"), marker => marker.Name == "second"),
                Is.True,
                "a marker the existing registration does not match does not block the new one");
            Assert.That(services.Count, Is.EqualTo(2));
        }
    }

    [Test]
    public void TryRegisterOnce_Facade_ShouldKeepTheTypeMarkerRegistration()
    {
        var services = new ServiceCollection();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ProtoRegistrationGuard.TryRegisterOnce<Marker>(services), Is.True);
            Assert.That(ProtoRegistrationGuard.TryRegisterOnce<Marker>(services), Is.False);
            Assert.That(
                services.Count(descriptor => descriptor.ServiceType == typeof(Marker)),
                Is.EqualTo(1),
                "the marker type is registered once");
        }
    }

    [Test]
    public void TryRegisterOnce_WeakTableFacade_ShouldKeepTheBuildersMarker()
    {
        var registrations = new ConditionalWeakTable<object, object>();
        var builder = new object();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ProtoRegistrationGuard.TryRegisterOnce(registrations, builder), Is.True);
            Assert.That(
                ProtoRegistrationGuard.TryRegisterOnce(registrations, builder),
                Is.False,
                "a builder already marked by another guard call cannot register again");
        }
    }

    private sealed record Marker(string Name);
}
