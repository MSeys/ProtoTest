namespace ProtoTest.Rest.Tests;

using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Http.Authenticators;
using ProtoTest.Rest.Exceptions;

[TestFixture]
public sealed class RestDiagnosticsAndExtensibilityTests
{
    [Test]
    public async Task AddClient_ShouldApplyHttpClientFactoryConfiguration()
    {
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.AddClient(
            "Orders",
            "https://example.test",
            http => http.ConfigureHttpClient(client =>
                client.DefaultRequestHeaders.Add("X-Configured", "true"))));
        await using var host = builder.Build();
        await host.StartTestAsync("Configured", "20001", CurrentMethod());

        try
        {
            var client = Proto.Context.Client<HttpClient>("Orders");
            Assert.That(client.DefaultRequestHeaders.GetValues("X-Configured").Single(), Is.EqualTo("true"));
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task AuthAttribute_ShouldResolveAuthenticatorDependenciesFromTestScope()
    {
        var handler = new AuthorizationCaptureHandler();
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services => services.AddScoped<AuthTokenProvider>());
        builder.AddRest(rest => rest.AddClient(
            "Default",
            "https://example.test",
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        await using var host = builder.Build();
        var method = typeof(AuthenticationCases).GetMethod(nameof(AuthenticationCases.UsesDependency))!;
        await host.StartTestAsync("DI auth", "20002", method);

        try
        {
            using var response = await Proto.Context.Rest().GetAsync("/protected");
            Assert.That(handler.Authorization, Is.EqualTo("Bearer resolved-from-di"));
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task Authenticator_ShouldBeCreatedAfterProtoAttributesAndReceiveExplicitContext()
    {
        var handler = new AuthorizationCaptureHandler();
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.AddClient(
            "Default",
            "https://example.test",
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        await using var host = builder.Build();
        var method = typeof(AuthenticationCases).GetMethod(nameof(AuthenticationCases.UsesUserContext))!;
        await host.StartTestAsync(
            "Context auth",
            "20008",
            method,
            [new SetUserContextAttribute()]);

        try
        {
            using var response = await Proto.Context.Rest().GetAsync("/protected");
            Assert.That(handler.Authorization, Is.EqualTo("Bearer per-test-user"));
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task PerTestBaseAddress_ShouldResolveFromContextAtRequestTime()
    {
        var handler = new AuthorizationCaptureHandler();
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.AddClient(
            "Environment",
            context => context.Resolve<EnvironmentContext>().BaseUri,
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        await using var host = builder.Build();
        await host.StartTestAsync(
            "Per-test base address",
            "20009",
            CurrentMethod(),
            [new SetEnvironmentContextAttribute()]);

        try
        {
            using var response = await Proto.Context.Rest("Environment").GetAsync("/orders/42");
            Assert.That(handler.RequestUri, Is.EqualTo(new Uri("https://environment-42.example/orders/42")));
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task AbsoluteRequestUri_ShouldUseExplicitAddressInsteadOfRegisteredBaseAddress()
    {
        var baseAddressWasResolved = false;
        var handler = new AuthorizationCaptureHandler();
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.AddClient(
            "Environment",
            _ =>
            {
                baseAddressWasResolved = true;
                return new Uri("https://environment.example");
            },
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        await using var host = builder.Build();
        await host.StartTestAsync("Absolute address", "20012", CurrentMethod());

        try
        {
            using var response = await Proto.Context.Rest("Environment")
                .GetAsync("https://override.example/orders/42");

            Assert.That(handler.RequestUri, Is.EqualTo(new Uri("https://override.example/orders/42")));
            Assert.That(baseAddressWasResolved, Is.False);
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task BaseAddressResolverFailure_ShouldEmitFailureObservation()
    {
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.AddClient(
            "Environment",
            _ => throw new InvalidOperationException("Environment was not prepared.")));
        await using var host = builder.Build();
        var context = await host.StartTestAsync("Missing environment", "20011", CurrentMethod());

        try
        {
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await context.Rest("Environment").GetAsync("/orders"));

            var failure = (RestFailureData)context.RecordedObservations.Single().Data!;
            Assert.That(exception!.Message, Is.EqualTo("Environment was not prepared."));
            Assert.That(failure.RouteTemplate, Is.EqualTo("/orders"));
            Assert.That(failure.RequestUri, Is.Null);
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task WithoutAuth_ShouldAllowProvisioningRequestsBeforeUserContextExists()
    {
        var handler = new AuthorizationCaptureHandler();
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.AddClient(
            "Default",
            "https://example.test",
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        await using var host = builder.Build();
        var method = typeof(AuthenticationCases).GetMethod(nameof(AuthenticationCases.UsesUserContext))!;
        await host.StartTestAsync("Provisioning", "20010", method);

        try
        {
            using var response = await Proto.Context.Rest().WithoutAuth().PostAsync("/users");
            Assert.That(handler.Authorization, Is.Null);
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task MultipleAuthAttributes_ShouldComposeInOrder()
    {
        var handler = new AuthorizationCaptureHandler();
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.AddClient(
            "Default",
            "https://example.test",
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        await using var host = builder.Build();
        var method = typeof(AuthenticationCases).GetMethod(nameof(AuthenticationCases.UsesMultipleSchemes))!;
        await host.StartTestAsync("Composite auth", "20005", method);

        try
        {
            using var response = await Proto.Context.Rest().GetAsync("/protected");
            Assert.That(handler.Authorization, Is.EqualTo("Bearer bearer-token"));
            Assert.That(handler.ApiKey, Is.EqualTo("api-secret"));
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task HeaderTrace_ShouldRecordCountAndNamesWithoutValues()
    {
        const string secret = "super-secret-value";
        var handler = new TestHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }
        };
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.AddClient(
            "Default",
            "https://example.test",
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        await using var host = builder.Build();
        await host.StartTestAsync("header trace", "20013", CurrentMethod());

        try
        {
            using var response = await Proto.Context.Rest()
                .Header("Authorization", $"Bearer {secret}")
                .Header("X-Trace", "first")
                .Header("x-trace", "second")
                .GetAsync("/orders");

            response.Should.HaveHttpStatus(HttpStatusCode.OK);

            var snapshot = host.Trace.Snapshot();
            var entries = snapshot.Tests.Single().Entries;
            var request = entries.Single(entry => entry.Kind == "http.request");
            var headerEvents = entries.Where(entry => entry.Kind == "http.header.configure").ToArray();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(request.Attributes["http.request.header_count"], Is.EqualTo("2"));
                Assert.That(headerEvents, Has.Length.EqualTo(2));
                Assert.That(headerEvents.Select(entry => entry.Attributes["http.header.name"]),
                    Is.EquivalentTo(new[] { "Authorization", "X-Trace" }));
                Assert.That(headerEvents.Select(entry => entry.Attributes["http.header.value_recorded"]),
                    Is.All.EqualTo("false"));
                Assert.That(headerEvents.Select(entry => entry.ParentId), Is.All.EqualTo(request.Id));
                Assert.That(handler.LastRequest!.Headers.GetValues("x-trace").Single(), Is.EqualTo("second"));
                Assert.That(JsonSerializer.Serialize(snapshot), Does.Not.Contain(secret));
            }
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task HeaderTrace_ShouldEmitOneEventPerHeaderWhenTheBuilderIsReused()
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }
        };
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.AddClient(
            "Default",
            "https://example.test",
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        await using var host = builder.Build();
        await host.StartTestAsync("reused header trace", "20015", CurrentMethod());

        try
        {
            var rest = Proto.Context.Rest().Header("X-Trace", "first");
            using var first = await rest.GetAsync("/orders");
            using var second = await rest.GetAsync("/orders");

            var entries = host.Trace.Snapshot().Tests.Single().Entries;
            var headerEvents = entries.Where(entry => entry.Kind == "http.header.configure").ToArray();

            Assert.That(headerEvents, Has.Length.EqualTo(1));
            Assert.That(headerEvents.Single().Attributes["http.header.name"], Is.EqualTo("X-Trace"));
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task Diagnostics_ShouldRedactSensitiveDataAndLimitBodyLength()
    {
        const string secret = "super-secret-value";
        var options = new ProtoHttpAttachmentOptions { MaxDiagnosticBodyLength = 80 };
        var services = new ServiceCollection()
            .AddKeyedSingleton(ProtoRestBuilder.ProtocolName, options)
            .BuildServiceProvider();
        await using var context = new ProtoExecutionContext(
            "Diagnostics",
            services.CreateScope(),
            "20004",
            CurrentMethod());
        var handler = new TestHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    $"{{\"token\":\"{secret}\",\"message\":\"{new string('x', 100)}\"}}",
                    Encoding.UTF8,
                    "application/json")
            }
        };
        handler.ResponseToReturn.Headers.Add("Set-Cookie", $"session={secret}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };

        using var response = await new RestRequestBuilder(client, context, "Orders").GetAsync("/secure");
        var observation = (RestResponseData)context.RecordedObservations.Single().Data!;
        var exception = Assert.Throws<RestStatusAssertionException>(() =>
            response.Should.HaveHttpStatus(HttpStatusCode.OK));
        var attachmentBytes = await context.Attachments.Single().ReadAllBytesAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Content, Does.Contain(secret), "The caller still receives the unmodified response.");
            Assert.That(observation.ResponseBody, Does.Not.Contain(secret));
            Assert.That(observation.ResponseBody, Does.Contain("truncated"));
            Assert.That(observation.Headers["Set-Cookie"], Is.EqualTo("[REDACTED]"));
            Assert.That(exception!.ResponseBody, Does.Not.Contain(secret));
            Assert.That(attachmentBytes, Is.Not.Empty);
        }
    }

    [Test]
    public async Task StatusAssertion_ShouldHonorTheConfiguredResponseBodyLimitWithoutAttachments()
    {
        const string secret = "super-secret-value";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Rest:Responses:MaxDiagnosticBodyLength"] = "32"
            }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration);
        ProtoHttpOptionsRegistration.TryAddResponseOptions(
            services,
            ProtoRestBuilder.ProtocolName,
            ProtoRestBuilder.ResponsesConfigurationSectionName);
        await using var provider = services.BuildServiceProvider();
        await using var context = new ProtoExecutionContext(
            "Status limit",
            provider.CreateScope(),
            "20014",
            CurrentMethod());
        var handler = new TestHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    $"{{\"token\":\"{secret}\",\"message\":\"{new string('x', 200)}\"}}",
                    Encoding.UTF8,
                    "application/json")
            }
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };

        using var response = await new RestRequestBuilder(client, context, "Orders").GetAsync("/secure");
        var exception = Assert.Throws<RestStatusAssertionException>(() =>
            response.Should.HaveHttpStatus(HttpStatusCode.OK));

        Assert.Multiple(() =>
        {
            Assert.That(context.ResolveAttachmentOptions(ProtoRestBuilder.ProtocolName), Is.Null);
            Assert.That(context.ResolveResponseOptions(ProtoRestBuilder.ProtocolName).MaxDiagnosticBodyLength, Is.EqualTo(32));
            Assert.That(exception!.ResponseBody, Does.Contain("truncated"));
            Assert.That(exception.ResponseBody.Length, Is.LessThan(120));
        });
    }

    [Test]
    public void RestCoverageCollector_ShouldOnlyCountResponseObservations()
    {
        var collector = new RestCoverageCollector("Orders");
        var response = new ProtoObservation("Orders", "http.response", "GET /orders");
        var shape = new ProtoObservation("Orders", "http.contract.shape", "GET /orders");

        Assert.That(collector.CanCollect(response), Is.True);
        Assert.That(collector.CanCollect(shape), Is.False);
        collector.Collect(response);

        Assert.That(collector.GetReportItems().Single().Count, Is.EqualTo(1));
    }

    [Test]
    public async Task ResponseBuffering_ShouldEnforceConfiguredLimit()
    {
        var services = new ServiceCollection()
            .AddKeyedSingleton(
                ProtoRestBuilder.ProtocolName,
                new ProtoHttpResponseOptions { MaxResponseBodyBytes = 4 })
            .BuildServiceProvider();
        await using var context = new ProtoExecutionContext(
            "Large response",
            services.CreateScope(),
            "20006",
            CurrentMethod());
        var handler = new TestHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("response is too large")
            }
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };

        var exception = Assert.ThrowsAsync<ProtoResponseTooLargeException>(async () =>
            await new RestRequestBuilder(client, context, "Orders").GetAsync("/large"));

        Assert.That(exception!.MaximumBytes, Is.EqualTo(4));
        Assert.That(context.RecordedObservations.Single().Kind, Is.EqualTo("http.failure"));
    }

    [Test]
    public async Task Response_ShouldPreserveBinaryContent()
    {
        var expected = new byte[] { 0, 1, 2, 128, 255 };
        var services = new ServiceCollection().BuildServiceProvider();
        await using var context = new ProtoExecutionContext(
            "Binary response",
            services.CreateScope(),
            "20007",
            CurrentMethod());
        var handler = new TestHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expected)
            }
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };

        using var response = await new RestRequestBuilder(client, context, "Files").GetAsync("/file");

        Assert.That(response.ReadAsBytes(), Is.EqualTo(expected));
    }

    private static MethodInfo CurrentMethod() => (MethodInfo)MethodInfo.GetCurrentMethod()!;

    private sealed class AuthTokenProvider
    {
        public string Token => "resolved-from-di";
    }

    private sealed class DependencyAuthenticator(AuthTokenProvider tokenProvider) : IProtoHttpAuthenticator
    {
        public ValueTask AuthenticateAsync(ProtoHttpAuthenticationContext context, CancellationToken cancellationToken = default)
        {
            context.Request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {tokenProvider.Token}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AuthenticationCases
    {
        [Auth<DependencyAuthenticator>]
        public void UsesDependency() { }

        [Auth<BearerTokenAuthenticator>("bearer-token", Order = 10)]
        [Auth<ApiKeyAuthenticator>("X-Api-Key", "api-secret", ApiKeyLocation.Header, Order = 20)]
        public void UsesMultipleSchemes() { }

        [Auth<UserContextAuthenticator>]
        public void UsesUserContext() { }
    }

    private sealed class UserContext(string token) : IProtoContext
    {
        public string Token { get; } = token;
    }

    private sealed class SetUserContextAttribute : ProtoAttribute
    {
        public override Task BeforeTestAsync(ProtoExecutionContext context)
        {
            context.SetContext(new UserContext("per-test-user"));
            return Task.CompletedTask;
        }
    }

    private sealed class UserContextAuthenticator(ProtoExecutionContext constructedWith) : IProtoHttpAuthenticator
    {
        public ValueTask AuthenticateAsync(
            ProtoHttpAuthenticationContext context,
            CancellationToken cancellationToken = default)
        {
            Assert.That(context.Test, Is.SameAs(constructedWith));
            var user = context.Test.Resolve<UserContext>();
            context.Request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.Token);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EnvironmentContext(Uri baseUri) : IProtoContext
    {
        public Uri BaseUri { get; } = baseUri;
    }

    private sealed class SetEnvironmentContextAttribute : ProtoAttribute
    {
        public override Task BeforeTestAsync(ProtoExecutionContext context)
        {
            context.SetContext(new EnvironmentContext(new Uri("https://environment-42.example")));
            return Task.CompletedTask;
        }
    }

    private sealed class AuthorizationCaptureHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public string? ApiKey { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            if (Authorization is null
                && request.Headers.TryGetValues("Authorization", out var authorizationValues))
            {
                Authorization = authorizationValues.SingleOrDefault();
            }
            ApiKey = request.Headers.TryGetValues("X-Api-Key", out var values)
                ? values.SingleOrDefault()
                : null;
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
