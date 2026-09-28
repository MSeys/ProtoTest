namespace ProtoTest.AspNetCore.Tests;

using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.TestSupport;

/// <summary>
/// A client bound to an application that runs at a published address talks to a real socket, so its
/// primary handler owns the cookie container a sign-in writes into. The framework's HTTP client factory
/// pools one handler per client name for the whole handler lifetime, which would hand parallel tests of
/// one run the same cookie jar; each test must build over its own handler instead.
/// </summary>
[TestFixture]
public sealed class PublishedCookieIsolationTests
{
    private const string ApplicationName = "Published";
    private const string SessionCookie = "session";

    [Test]
    public async Task PublishedClients_WhenParallelTestsSignIn_ShouldKeepTheirOwnSessionsAndHandlers()
    {
        var pipelines = new ConcurrentQueue<CapturingHandler>();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddLoopbackApplication(ApplicationName, CreateCookieApplication);
        builder.AddApplication(ApplicationName, app => app.AddRest(rest => rest.AddClient(
            "Api",
            configure: http => http.AddHttpMessageHandler(() =>
            {
                var pipeline = new CapturingHandler();
                pipelines.Enqueue(pipeline);
                return pipeline;
            }))));
        await using var host = builder.Build();
        await host.StartAsync();

        var sessions = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var started = new AsyncGate(2);
        var signedIn = new AsyncGate(2);
        var first = RunAsync("first", "91001", started, signedIn, sessions);
        var second = RunAsync("second", "91002", started, signedIn, sessions);
        await Task.WhenAll(first, second);

        var primaryHandlers = pipelines.Select(pipeline => pipeline.Primary).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sessions["first"], Is.EqualTo("first"), "the first test reads its own session cookie");
            Assert.That(sessions["second"], Is.EqualTo("second"), "the second test reads its own session cookie");
            Assert.That(pipelines.ToArray(), Has.Length.EqualTo(2), "each test builds its own handler pipeline");
            Assert.That(primaryHandlers, Has.All.Not.Null, "every pipeline ends in a primary handler");
            Assert.That(
                primaryHandlers.Distinct(ReferenceEqualityComparer.Instance).ToArray(),
                Has.Length.EqualTo(2),
                "the primary handlers - and their cookie containers - are per test");
            Assert.That(
                pipelines.Select(pipeline => pipeline.Requests),
                Is.All.GreaterThan(0),
                "the named client's configured delegating handler serves its own test");
        }

        async Task RunAsync(
            string token,
            string testId,
            AsyncGate startGate,
            AsyncGate signInGate,
            ConcurrentDictionary<string, string> results)
        {
            await host.StartTestAsync(
                $"published {token}",
                testId,
                TestMethods.Placeholder,
                [new ApplicationAttribute(ApplicationName)]);
            await startGate.ArriveAsync();
            await Proto.Context.Rest("Api").PostAsync($"/sign-in/{token}");
            await signInGate.ArriveAsync();
            var response = await Proto.Context.Rest("Api").GetAsync("/session");
            results[token] = Encoding.UTF8.GetString(response.ContentBytes.Span);
            await host.CompleteTestAsync(ProtoTestResult.Passed);
        }
    }

    private static WebApplication CreateCookieApplication(string[] args)
    {
        var application = WebApplication.CreateBuilder(args).Build();
        application.MapPost("/sign-in/{token}", (HttpContext http, string token) =>
        {
            http.Response.Cookies.Append(SessionCookie, token, new CookieOptions { Path = "/" });
            return Results.Ok();
        });
        application.MapGet("/session", (HttpContext http) =>
            Results.Text(http.Request.Cookies[SessionCookie] ?? "none"));
        return application;
    }

    /// <summary>
    /// Releases both tests together at a point: both sign-ins complete before either session is read,
    /// so a shared cookie jar would answer both reads with the last sign-in.
    /// </summary>
    private sealed class AsyncGate(int parties)
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrived) == parties)
            {
                _release.TrySetResult();
            }

            await _release.Task.ConfigureAwait(false);
        }
    }

    /// <summary>Counts the requests that crossed it and exposes the primary handler it was built over.</summary>
    private sealed class CapturingHandler : DelegatingHandler
    {
        private int _requests;

        public HttpMessageHandler? Primary => InnerHandler;

        public int Requests => Volatile.Read(ref _requests);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
