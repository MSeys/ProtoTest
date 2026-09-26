namespace ProtoTest.AspNetCore.Tests;

using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Testing;
using ProtoTest.Core;
using ProtoTest.TestSupport;
using SampleApi = ProtoTest.AspNetCore.SampleApi;

/// <summary>
/// The in-process HTTP client is a test resource: the test context owns it and disposes it when the
/// test completes. The shared per-run server factory must not also collect it in its own client
/// ledger, because the tests of a run mutate that list in parallel and the factory enumerates it
/// during teardown - a torn entry there crashes the run's disposal.
/// </summary>
[TestFixture]
public sealed class ClientOwnershipTests
{
    [Test]
    public async Task InProcessClient_ShouldBeOwnedByItsTest_NotTrackedByTheSharedPerRunFactory()
    {
        // Arrange: one per-run server shared by two tests.
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("OwnershipApi")
            .Build();
        await using var ownedHost = host;
        var method = TestMethods.Placeholder;
        WebApplicationFactory<SampleApi.Program>? sharedFactory = null;
        var clients = new List<HttpClient>();

        for (var index = 0; index < 2; index++)
        {
            await host.StartTestAsync($"ClientOwnership {index}", $"0004{index}", method);
            sharedFactory ??= Proto.Context.ServerFactory<SampleApi.Program>("OwnershipApi");
            var client = Proto.Context.Client<HttpClient>("OwnershipApi");
            clients.Add(client);
            var response = await client.GetAsync("/ping");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            await host.CompleteTestAsync(ProtoTestResult.Passed);
        }

        // Assert: both tests ran against the same factory, and each client was owned and disposed by
        // its own test - the factory's disposal ledger holds none of them.
        Assert.Multiple(() =>
        {
            Assert.That(ClientLedger(sharedFactory!), Is.Empty,
                "the shared factory must not track the in-process clients its tests created");
            Assert.That(clients, Has.Count.EqualTo(2), "both tests made their own client");
            Assert.That(
                Assert.ThrowsAsync<ObjectDisposedException>(async () => await clients[0].GetAsync("/ping")),
                Is.Not.Null,
                "the first test's client was disposed when that test completed");
        });
    }

    [Test]
    public async Task InProcessServer_WhenParallelTestsShareOneRun_ShouldServeEveryTestAndDisposeCleanly()
    {
        // Arrange: the run's tests are started from their own threads, released together, exactly like
        // a parallel test runner shares one per-run server.
        const int testCount = 16;
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("ParallelApi")
            .Build();
        var failures = new ConcurrentQueue<Exception>();
        var factories = new ConcurrentBag<WebApplicationFactory<SampleApi.Program>>();
        using var gate = new ManualResetEventSlim();
        var threads = Enumerable.Range(0, testCount).Select(index => new Thread(() =>
        {
            try
            {
                gate.Wait();
                host.StartTestAsync($"Parallel {index}", $"{index + 1:D5}", TestMethods.Placeholder)
                    .GetAwaiter().GetResult();
                factories.Add(Proto.Context.ServerFactory<SampleApi.Program>("ParallelApi"));
                var response = Proto.Context.Client<HttpClient>("ParallelApi")
                    .GetAsync("/ping").GetAwaiter().GetResult();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new InvalidOperationException($"Test {index} answered {response.StatusCode}.");
                }

                host.CompleteTestAsync(ProtoTestResult.Passed).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        })).ToArray();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        gate.Set();
        foreach (var thread in threads)
        {
            thread.Join();
        }

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(failures, Is.Empty, "every parallel test was served by the shared server");
                Assert.That(factories.Distinct(ReferenceEqualityComparer.Instance).Count(), Is.EqualTo(1),
                    "a per-run server is one factory for the whole run");
            });
        }
        finally
        {
            // The run's teardown releases the shared factory once; corrupt factory state surfaced here.
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// The framework's ledger of the clients it created and disposes with the factory. ProtoTest's
    /// in-process clients are owned by their test contexts, so the ledger stays empty.
    /// </summary>
    private static ICollection ClientLedger(WebApplicationFactory<SampleApi.Program> factory)
        => (ICollection)typeof(WebApplicationFactory<SampleApi.Program>)
            .GetField("_clients", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(factory)!;
}
