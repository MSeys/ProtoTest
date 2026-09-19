namespace ProtoTest.AspNetCore.Tests;

using System.Diagnostics;
using System.Net;
using System.Reflection;
using ProtoTest.Core;

[TestFixture]
public sealed class ClientHandlerTests
{
    [Test]
    public async Task DefaultClient_ShouldFollowRedirectsThroughTheMirroredHandlers()
    {
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("Default")
            .Build();
        await host.StartTestAsync("Client_Redirects", "00014", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            var client = Proto.Context.Client<HttpClient>("Default");

            using var response = await client.GetAsync("/redirect");

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(response.RequestMessage!.RequestUri!.AbsolutePath, Is.EqualTo("/ping"),
                    "the client followed the redirect before returning the final response");
            });
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task DefaultClient_ShouldCarryCookiesThroughTheMirroredHandlers()
    {
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("Default")
            .Build();
        await host.StartTestAsync("Client_Cookies", "00015", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            var client = Proto.Context.Client<HttpClient>("Default");
            using (var set = await client.GetAsync("/cookies/set"))
            {
                set.EnsureSuccessStatusCode();
            }

            var cookie = await client.GetStringAsync("/cookies/read");

            Assert.That(cookie, Is.EqualTo("chocolate"),
                "the cookie handler stored the Set-Cookie value and replayed it on the next request");
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task ProtoTraceContextHandler_ShouldInjectTheCurrentTestTraceContext()
    {
        var host = new ProtoHostBuilder()
            .ConfigureTracing(options => options.ActivitySources.Add("ProtoTest.AspNetCore.Tests"))
            .AddAspNetCoreServer<SampleApi.Program>("Default")
            .Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "Client_TraceContext", "00016", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            using var operation = context.Trace
                .Operation("test.probe", "Trace context probe", "ProtoTest.AspNetCore.Tests")
                .Begin();
            var activity = Activity.Current;
            Assert.That(activity, Is.Not.Null, "the probe operation opens the test's W3C trace context");

            var traceparent = await Proto.Context.Client<HttpClient>("Default").GetStringAsync("/traceparent");

            Assert.Multiple(() =>
            {
                Assert.That(traceparent, Does.StartWith(
                    $"00-{activity!.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-"));
                Assert.That(ProtoHost.FindTraceWriter(activity.TraceId), Is.SameAs(context.Trace),
                    "the outgoing traceparent uses the current test's trace id");
            });
            operation.Succeed();
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task ProtoTraceContextHandler_ShouldNotOverwriteAnExistingTraceparent()
    {
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("Default")
            .Build();
        await host.StartTestAsync("Client_ExistingTraceparent", "00017", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            const string existing = "00-11111111111111111111111111111111-2222222222222222-01";
            using var request = new HttpRequestMessage(HttpMethod.Get, "/traceparent");
            request.Headers.TryAddWithoutValidation("traceparent", existing);

            using var response = await Proto.Context.Client<HttpClient>("Default").SendAsync(request);
            var traceparent = await response.Content.ReadAsStringAsync();

            Assert.That(traceparent, Is.EqualTo(existing),
                "an explicitly set traceparent must survive the ProtoTest handler");
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            await host.DisposeAsync();
        }
    }
}
