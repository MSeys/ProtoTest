namespace ProtoTest.AspNetCore.Tests;

using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// A substituting test runs against a dedicated server built with its substitutions before it is
/// built: the run's shared server never sees them, the next test starts clean, and parallel tests
/// that substitute differently never meet.
/// </summary>
[TestFixture]
public sealed class ServiceSubstitutionTests
{
    [Test]
    public async Task Override_WithAnInstance_ShouldServeTheReplacementToTheApplication()
    {
        // Arrange
        await using var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("SubstituteApi")
            .Build();
        await host.StartTestAsync("Override instance", "00101", TestMethods.Placeholder);

        // Act
        Proto.Context.Override<SampleApi.ITestMessageService>(
            new StubMessageService("substituted"),
            "SubstituteApi");
        var body = await Proto.Context.Client<HttpClient>("SubstituteApi").GetStringAsync("/message");
        var service = Proto.Context.ServerService<SampleApi.Program, SampleApi.ITestMessageService>("SubstituteApi");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body, Does.Contain("substituted"), "the application's request saw the replacement");
            Assert.That(service.GetMessage(), Is.EqualTo("substituted"), "the test scope resolves the replacement");
        }
    }

    [Test]
    public async Task Override_WithAnImplementationType_ShouldResolveItFromTheApplicationContainer()
    {
        // Arrange
        await using var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("SubstituteApi")
            .Build();
        await host.StartTestAsync("Override implementation type", "00102", TestMethods.Placeholder);

        // Act
        Proto.Context.Override<SampleApi.ITestMessageService, AnotherMessageService>("SubstituteApi");
        var body = await Proto.Context.Client<HttpClient>("SubstituteApi").GetStringAsync("/message");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Assert
        Assert.That(body, Does.Contain("attribute replacement"));
    }

    [Test]
    public async Task Override_WithAFactory_ShouldServeWhatTheFactoryBuilds()
    {
        // Arrange
        await using var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("SubstituteApi")
            .Build();
        await host.StartTestAsync("Override factory", "00103", TestMethods.Placeholder);

        // Act
        Proto.Context.Override<SampleApi.ITestMessageService>(
            () => new StubMessageService("from factory"),
            "SubstituteApi");
        var body = await Proto.Context.Client<HttpClient>("SubstituteApi").GetStringAsync("/message");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Assert
        Assert.That(body, Does.Contain("from factory"));
    }

    [Test]
    public async Task ReplaceServiceAttribute_ShouldServeTheReplacementToTheApplication()
    {
        // Arrange
        await using var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("SubstituteApi")
            .Build();
        await host.StartTestAsync(
            "ReplaceService attribute",
            "00104",
            TestMethods.Placeholder,
            [new ReplaceServiceAttribute<SampleApi.ITestMessageService>(typeof(AnotherMessageService))
            {
                Server = "SubstituteApi"
            }]);

        // Act
        var body = await Proto.Context.Client<HttpClient>("SubstituteApi").GetStringAsync("/message");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Assert
        Assert.That(body, Does.Contain("attribute replacement"));
    }

    [Test]
    public async Task ReplaceServiceAttribute_WithoutAServer_ShouldFollowTheSelectedApplication()
    {
        // Arrange
        await using var host = new ProtoHostBuilder()
            .AddApplication("Api", app => app.AddAspNetCoreServer<SampleApi.Program>())
            .Build();
        await host.StartTestAsync(
            "Application-selected substitution",
            "00116",
            TestMethods.Placeholder,
            [
                new ApplicationAttribute("Api"),
                new ReplaceServiceAttribute<SampleApi.ITestMessageService>(typeof(AnotherMessageService))
            ]);

        // Act
        var body = await Proto.Context.Client<HttpClient>("Api").GetStringAsync("/message");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Assert
        Assert.That(body, Does.Contain("attribute replacement"));
    }

    [Test]
    public async Task ReplaceServiceAttribute_FollowedByAnOverride_ShouldServeTheOverride()
    {
        // Arrange
        await using var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("SubstituteApi")
            .Build();
        await host.StartTestAsync(
            "Attribute then override",
            "00115",
            TestMethods.Placeholder,
            [new ReplaceServiceAttribute<SampleApi.ITestMessageService>(typeof(AnotherMessageService))
            {
                Server = "SubstituteApi"
            }]);

        // Act: the body override joins the attribute's substitution, and the later one wins.
        Proto.Context.Override<SampleApi.ITestMessageService>(
            new StubMessageService("body wins"),
            "SubstituteApi");
        var body = await Proto.Context.Client<HttpClient>("SubstituteApi").GetStringAsync("/message");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Assert
        Assert.That(body, Does.Contain("body wins"));
    }

    [Test]
    public async Task FailDependencyAttribute_ShouldFailRequestsThatResolveTheService()
    {
        // Arrange
        await using var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("FailApi")
            .Build();
        await host.StartTestAsync(
            "FailDependency attribute",
            "00105",
            TestMethods.Placeholder,
            [new FailDependencyAttribute<SampleApi.ITestMessageService> { Server = "FailApi" }]);

        // Act
        using var response = await Proto.Context.Client<HttpClient>("FailApi").GetAsync("/message");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(
                host.Trace.Snapshot().Tests.Single().Entries
                    .Any(entry => entry.Kind == "service.fail"),
                Is.True,
                "the failure is traced as a service.fail operation");
        }
    }

    [Test]
    public async Task PerRunServer_WhenOneTestOverrides_ShouldNotLeakIntoTheNext()
    {
        // Arrange
        await using var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("SharedApi")
            .Build();
        var method = TestMethods.Placeholder;

        // Act
        await host.StartTestAsync("Overriding test", "00106", method);
        Proto.Context.Override<SampleApi.ITestMessageService>(
            new StubMessageService("one test only"),
            "SharedApi");
        var overridden = await Proto.Context.Client<HttpClient>("SharedApi").GetStringAsync("/message");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        await host.StartTestAsync("Plain test", "00107", method);
        var plain = await Proto.Context.Client<HttpClient>("SharedApi").GetStringAsync("/message");
        var shared = Proto.Context.ServerService<SampleApi.Program, SampleApi.ITestMessageService>("SharedApi");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(overridden, Does.Contain("one test only"));
            Assert.That(plain, Does.Contain("Hello from AspNetCore DI!"), "the next test sees the original service");
            Assert.That(
                shared.GetMessage(),
                Is.EqualTo("Hello from AspNetCore DI!"),
                "the shared per-run server was never reconfigured");
        }
    }

    [Test]
    public async Task PerTestServer_WhenOneTestOverrides_ShouldNotLeakIntoTheNext()
    {
        // Arrange
        await using var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>(
                "IsolatedApi",
                lifetime: AspNetCoreServerLifetime.PerTest)
            .Build();
        var method = TestMethods.Placeholder;

        // Act
        await host.StartTestAsync(
            "Overriding test",
            "00108",
            method,
            [new ReplaceServiceAttribute<SampleApi.ITestMessageService>(typeof(AnotherMessageService))
            {
                Server = "IsolatedApi"
            }]);
        var overridden = await Proto.Context.Client<HttpClient>("IsolatedApi").GetStringAsync("/message");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        await host.StartTestAsync("Plain test", "00109", method);
        var plain = await Proto.Context.Client<HttpClient>("IsolatedApi").GetStringAsync("/message");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(overridden, Does.Contain("attribute replacement"));
            Assert.That(plain, Does.Contain("Hello from AspNetCore DI!"), "the next test starts an unsubstituted server");
        }
    }

    [Test]
    public async Task PerRunServer_WhenParallelTestsOverrideDifferently_ShouldNotCross()
    {
        // Arrange: the run's tests substitute on their own threads, released together, exactly like
        // a parallel runner shares one per-run server.
        const int testCount = 4;
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("ParallelSubApi")
            .Build();
        var failures = new ConcurrentQueue<Exception>();
        var answers = new ConcurrentDictionary<int, string>();
        using var gate = new ManualResetEventSlim();
        var threads = Enumerable.Range(0, testCount).Select(index => new Thread(() =>
        {
            try
            {
                gate.Wait();
                host.StartTestAsync($"Parallel substitute {index}", $"{index + 1:D5}", TestMethods.Placeholder)
                    .GetAwaiter().GetResult();
                Proto.Context.Override<SampleApi.ITestMessageService>(
                    new StubMessageService($"parallel-{index}"),
                    "ParallelSubApi");
                answers[index] = Proto.Context.Client<HttpClient>("ParallelSubApi")
                    .GetStringAsync("/message").GetAwaiter().GetResult();
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
            using (Assert.EnterMultipleScope())
            {
                Assert.That(failures, Is.Empty, "every parallel test was served its own substitution");
                foreach (var index in Enumerable.Range(0, testCount))
                {
                    Assert.That(
                        answers[index],
                        Does.Contain($"parallel-{index}"),
                        $"test {index} saw its own replacement, not another test's");
                }
            }

            await host.StartTestAsync("Plain test after parallel substitutes", "00110", TestMethods.Placeholder);
            var plain = await Proto.Context.Client<HttpClient>("ParallelSubApi").GetStringAsync("/message");
            await host.CompleteTestAsync(ProtoTestResult.Passed);

            Assert.That(
                plain,
                Does.Contain("Hello from AspNetCore DI!"),
                "the shared server still serves the original service");
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task ReplaceServiceAttribute_WhenApplicationIsPublished_ShouldSkip()
    {
        // Arrange
        await using var published = PublishedHost("Api");
        await published.StartAsync();
        await using var live = new ProtoHostBuilder()
            .ConfigureTracing(options => options.Enabled = false)
            .AddAspNetCoreServer<SampleApi.Program>()
            .Build();
        await live.StartAsync();
        var substitution = new ReplaceServiceAttribute<SampleApi.ITestMessageService>(typeof(AnotherMessageService));
        var otherServer = new ReplaceServiceAttribute<SampleApi.ITestMessageService>(typeof(AnotherMessageService))
        {
            Server = "Other"
        };
        var failure = new FailDependencyAttribute<SampleApi.ITestMessageService>();

        // Act
        var publishedReason = substitution.GetSkipReason(published);
        var liveReason = substitution.GetSkipReason(live);
        var otherReason = otherServer.GetSkipReason(live);
        var failureReason = failure.GetSkipReason(published);

        await live.StopAsync();
        await published.StopAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(publishedReason, Does.Contain("Default"), "an unnamed gate follows the fallback application");
            Assert.That(liveReason, Is.Null, "a live in-process server runs it");
            Assert.That(otherReason, Does.Contain("Other"), "an unknown server names itself");
            Assert.That(failureReason, Does.Contain("Default"), "failing a dependency skips the same way");
        }
    }

    [Test]
    public async Task Override_AfterCompletionStarted_ShouldThrowBeforeTheDedicatedServerStarts()
    {
        // Arrange: the override races teardown after the context sealed its client registry. The
        // dedicated server is owned before it starts, so the refused registration cannot leak one.
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddAspNetCoreServer<SampleApi.Program>("LateApi");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("Late override", "00117", TestMethods.Placeholder);
        var context = Proto.Context;
        var release = new TaskCompletionSource();
        context.RegisterResource(
            "test:completion-gate",
            "test",
            "Completion gate",
            _ => new ValueTask(release.Task));

        var disposal = context.DisposeAsync();

        // Act: the sealed registry refuses the registration before the dedicated server starts.
        var exception = Assert.Throws<ObjectDisposedException>(
            () => context.Override<SampleApi.ITestMessageService>(
                new StubMessageService("too late"),
                "LateApi"));

        release.SetResult();
        await disposal;
        await host.StopAsync();

        // Assert
        Assert.That(
            exception!.ObjectName,
            Does.Contain("ProtoClientRegistry"),
            "the sealed registry refuses the late registration");
    }

    [Test]
    public async Task ReplaceServiceAttribute_WhenTheSelectedApplicationIsPublished_ShouldSkipEvenWithAnotherServerLive()
    {
        // Arrange: application Api is published (its capability drops), application Live is in-process.
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = "https://published.example.test"
            }));
        builder
            .AddApplication("Api", app => app.AddAspNetCoreServer<SampleApi.Program>())
            .AddAspNetCoreServer<SampleApi.Program>("Live");
        await using var host = builder.Build();
        await host.StartAsync();

        // Act: the adapter's own entry point resolves the selection and the skip in one place.
        var publishedSelected = Prepare(nameof(SelectedSubstitutionMethods.PublishedSelected), host);
        var liveSelected = Prepare(nameof(SelectedSubstitutionMethods.LiveSelected), host);
        var namedLive = Prepare(nameof(SelectedSubstitutionMethods.NamedLive), host);
        var namedPublished = Prepare(nameof(SelectedSubstitutionMethods.NamedPublished), host);

        await host.StopAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(publishedSelected.CanRun, Is.False, "the unnamed attribute follows the selected application");
            Assert.That(publishedSelected.SkipReason, Does.Contain("Api"), "the skip names the published application");
            Assert.That(liveSelected.CanRun, Is.True, "the live selected application runs the substitution");
            Assert.That(namedLive.CanRun, Is.True, "a named live server runs the substitution");
            Assert.That(namedPublished.CanRun, Is.False, "a named published server skips");
            Assert.That(namedPublished.SkipReason, Does.Contain("Api"), "the named skip names the server");
        }
    }

    [Test]
    public async Task ReplaceServiceAttribute_WhenApplicationIsPublished_ShouldThrowFromSetup()
    {
        // Arrange: adapters skip before the lifecycle starts, but a directly driven test that
        // reaches setup fails naming the address instead of substituting a remote application.
        await using var host = PublishedHost("Api");
        await host.StartAsync();

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await host.StartTestAsync(
                "Published substitution",
                "00114",
                TestMethods.Placeholder,
                [new ReplaceServiceAttribute<SampleApi.ITestMessageService>(typeof(AnotherMessageService))
                {
                    Server = "Api"
                }]));

        await host.StopAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("runs at"));
            Assert.That(exception.Message, Does.Contain("ProtoTest:Applications:Api:BaseUrl"));
        }
    }

    [Test]
    public async Task Override_WhenApplicationIsPublished_ShouldThrowNamingTheAddress()
    {
        // Arrange
        await using var host = PublishedHost("Api");
        await host.StartAsync();
        await host.StartTestAsync("Published override", "00111", TestMethods.Placeholder);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(
            () => Proto.Context.Override<SampleApi.ITestMessageService>(
                new StubMessageService("nowhere to serve it"),
                "Api"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("runs at"));
            Assert.That(exception.Message, Does.Contain("ProtoTest:Applications:Api:BaseUrl"));
        }
    }

    [Test]
    public async Task Override_WhenNoServerIsRegistered_ShouldThrowNamingTheRegistration()
    {
        // Arrange
        await using var host = new ProtoHostBuilder()
            .ConfigureTracing(options => options.Enabled = false)
            .AddAspNetCoreServer<SampleApi.Program>("Api")
            .Build();
        await host.StartTestAsync("Unknown server", "00112", TestMethods.Placeholder);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(
            () => Proto.Context.Override<SampleApi.ITestMessageService>(
                new StubMessageService("nowhere to serve it"),
                "Missing"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("Missing"));
            Assert.That(exception.Message, Does.Contain("AddAspNetCoreServer"));
        }
    }

    [Test]
    public void ReplaceServiceAttribute_WithAnUnusableImplementation_ShouldThrow()
    {
        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Assert.Throws<ArgumentException>(() =>
                    new ReplaceServiceAttribute<SampleApi.ITestMessageService>(typeof(string))),
                Has.Message.Contains("cannot serve"));
            Assert.That(
                Assert.Throws<ArgumentException>(() =>
                    new ReplaceServiceAttribute<SampleApi.ITestMessageService>(typeof(SampleApi.ITestMessageService))),
                Has.Message.Contains("concrete type"));
        }
    }

    [Test]
    public async Task Override_ShouldTraceTheSubstitutionOnTheServerEntity()
    {
        // Arrange
        await using var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("TracedApi")
            .Build();
        await host.StartTestAsync("Traced substitution", "00113", TestMethods.Placeholder);

        // Act
        Proto.Context.Override<SampleApi.ITestMessageService>(
            new StubMessageService("traced"),
            "TracedApi");
        await Proto.Context.Client<HttpClient>("TracedApi").GetStringAsync("/message");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Assert
        var test = host.Trace.Snapshot().Tests.Single();
        var substitution = test.Entries.Single(entry => entry.Kind == "service.substitute");
        var server = test.Entities!.Single(entity => entity.Kind == ProtoTraceEntityKinds.Server);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                substitution.Attributes["service.type"],
                Does.Contain(nameof(SampleApi.ITestMessageService)));
            Assert.That(server.State["aspnetcore.server.substituted"], Is.EqualTo("true"));
            Assert.That(
                server.State["aspnetcore.server.substitutions"],
                Does.Contain(nameof(SampleApi.ITestMessageService)));
        }
    }

    [Test]
    public async Task Override_AfterResolvingApplicationServices_ShouldLeaveTheEarlyScopeOnTheSharedServer()
    {
        // Arrange: the scope is resolved before the override, so it is pinned to the shared server.
        await using var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("SubstituteApi")
            .Build();
        await host.StartTestAsync("Override after services", "00115", TestMethods.Placeholder);
        var early = Proto.Context.ApplicationServices<SampleApi.Program>("SubstituteApi");

        // Act
        Proto.Context.Override<SampleApi.ITestMessageService>(
            new StubMessageService("substituted"),
            "SubstituteApi");
        var earlyMessage = early.GetRequiredService<SampleApi.ITestMessageService>().GetMessage();
        var body = await Proto.Context.Client<HttpClient>("SubstituteApi").GetStringAsync("/message");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                earlyMessage,
                Is.Not.EqualTo("substituted"),
                "a scope resolved before the override still serves the shared server");
            Assert.That(
                body,
                Does.Contain("substituted"),
                "requests after the override serve the replacement from the dedicated server");
        }
    }

    private static ProtoHost PublishedHost(string name)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"ProtoTest:Applications:{name}:BaseUrl"] = "https://published.example.test"
            }));
        builder.AddAspNetCoreServer<SampleApi.Program>(name);
        return builder.Build();
    }

    private static ProtoTestPreparation Prepare(string methodName, ProtoHost host)
        => ProtoTestAdapter.Prepare(
            typeof(SelectedSubstitutionMethods).GetMethod(methodName)!,
            host);

    private static class SelectedSubstitutionMethods
    {
        [Application("Api")]
        [ReplaceService<SampleApi.ITestMessageService>(typeof(AnotherMessageService))]
        public static void PublishedSelected()
        {
        }

        [Application("Live")]
        [ReplaceService<SampleApi.ITestMessageService>(typeof(AnotherMessageService))]
        public static void LiveSelected()
        {
        }

        [ReplaceService<SampleApi.ITestMessageService>(typeof(AnotherMessageService), Server = "Live")]
        public static void NamedLive()
        {
        }

        [ReplaceService<SampleApi.ITestMessageService>(typeof(AnotherMessageService), Server = "Api")]
        public static void NamedPublished()
        {
        }
    }

    private sealed class StubMessageService(string message) : SampleApi.ITestMessageService
    {
        public string GetMessage() => message;
    }

    private sealed class AnotherMessageService : SampleApi.ITestMessageService
    {
        public string GetMessage() => "attribute replacement";
    }
}
