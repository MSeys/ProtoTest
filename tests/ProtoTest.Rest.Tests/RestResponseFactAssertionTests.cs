namespace ProtoTest.Rest.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Rest.Exceptions;

[TestFixture]
public sealed class RestResponseFactAssertionTests
{
    private ProtoExecutionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        _context = new ProtoExecutionContext(
            "TestContext",
            services.BuildServiceProvider().CreateScope(),
            "00002",
            TestMethods.Placeholder);
    }

    [TearDown]
    public async Task TearDown() => await _context.DisposeAsync();

    [Test]
    public void HaveContentType_ShouldCompareTheMediaTypeWithoutParameters()
    {
        var response = Response("{}", configure: raw =>
        {
            raw.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Should.HaveContentType("application/json"), Is.SameAs(response));
            response.Should.HaveContentType("APPLICATION/JSON");
            response.ShouldNot.HaveContentType("text/plain");

            var exception = Assert.Throws<RestAssertionException>(
                () => response.Should.HaveContentType("text/plain"));
            Assert.That(exception!.Message, Is.EqualTo(
                "GET /orders/42 — Expected content type 'text/plain', but received 'application/json'."));

            var negated = Assert.Throws<RestAssertionException>(
                () => response.ShouldNot.HaveContentType("application/json"));
            Assert.That(negated!.Message, Does.Contain("Expected content type not 'application/json'"));
        }
    }

    [Test]
    public void HaveContentType_ShouldNameTheMissingMediaType()
    {
        var response = Response("raw text", configure: raw =>
            raw.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("raw text")));

        var exception = Assert.Throws<RestAssertionException>(
            () => response.Should.HaveContentType("application/json"));

        Assert.That(exception!.Message, Does.Contain("but received none"));
    }

    [Test]
    public void HaveHeader_ShouldAssertPresenceAndValue()
    {
        var response = Response("{}", configure: raw => raw.Headers.TryAddWithoutValidation("X-Correlation", "abc"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Should.HaveHeader("x-correlation"), Is.SameAs(response));
            response.Should.HaveHeader("X-Correlation", "abc");
            response.ShouldNot.HaveHeader("X-Missing");
            response.ShouldNot.HaveHeader("X-Correlation", "other");

            var missing = Assert.Throws<RestAssertionException>(() => response.Should.HaveHeader("X-Missing"));
            Assert.That(missing!.Message, Is.EqualTo(
                "GET /orders/42 — Expected header 'X-Missing' to be present, but the response carried none."));

            var negated = Assert.Throws<RestAssertionException>(() => response.ShouldNot.HaveHeader("X-Correlation"));
            Assert.That(negated!.Message, Does.Contain("Expected header 'X-Correlation' not to be present"));

            var wrongValue = Assert.Throws<RestAssertionException>(
                () => response.Should.HaveHeader("X-Correlation", "other"));
            Assert.That(wrongValue!.Message, Is.EqualTo(
                "GET /orders/42 — Expected header 'X-Correlation' to have value 'other', but it was abc."));
        }
    }

    [Test]
    public void HaveHeader_ShouldReadContentHeaders()
    {
        var response = Response("{}", configure: raw =>
            raw.Content.Headers.TryAddWithoutValidation("X-Content-Custom", "v"));

        response.Should.HaveHeader("X-Content-Custom", "v");
    }

    [Test]
    public void HaveHeader_ShouldRedactSensitiveValuesInTheFailure()
    {
        var response = Response("{}", configure: raw =>
            raw.Headers.TryAddWithoutValidation("Authorization", "Bearer hunter2"));

        var exception = Assert.Throws<RestAssertionException>(
            () => response.Should.HaveHeader("Authorization", "Bearer other"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("[REDACTED]"));
            Assert.That(exception.Message, Does.Not.Contain("hunter2"));
        }
    }

    [Test]
    public void HaveCookie_ShouldAssertPresenceAndValue()
    {
        var response = Response("{}", configure: raw =>
            raw.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc123; Path=/; HttpOnly"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Should.HaveCookie("session"), Is.SameAs(response));
            response.Should.HaveCookie("session", "abc123");
            response.ShouldNot.HaveCookie("session", "other");
            response.ShouldNot.HaveCookie("missing");

            var missing = Assert.Throws<RestAssertionException>(() => response.Should.HaveCookie("missing"));
            Assert.That(missing!.Message, Is.EqualTo(
                "GET /orders/42 — Expected cookie 'missing' to be set, but the response set none."));

            var wrongValue = Assert.Throws<RestAssertionException>(
                () => response.Should.HaveCookie("session", "other"));
            Assert.That(wrongValue!.Message, Does.StartWith(
                "GET /orders/42 — Expected cookie 'session' to have value 'other', but it was "));
        }
    }

    [Test]
    public void HaveCookie_ShouldRedactTheSetCookieValueInTheFailure()
    {
        var response = Response("{}", configure: raw =>
            raw.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc123; Path=/"));

        var exception = Assert.Throws<RestAssertionException>(
            () => response.Should.HaveCookie("session", "other"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("[REDACTED]"));
            Assert.That(exception.Message, Does.Not.Contain("abc123"));
        }
    }

    [Test]
    public void HaveRedirectLocation_ShouldMatchTheLocationAsItArrived()
    {
        var relative = Response("{}", configure: raw =>
            raw.Headers.Location = new Uri("/orders/1", UriKind.Relative));
        var absolute = Response("{}", configure: raw =>
            raw.Headers.Location = new Uri("https://api.prototest.dev/orders/1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(relative.Should.HaveRedirectLocation("/orders/1"), Is.SameAs(relative));
            relative.ShouldNot.HaveRedirectLocation("/orders/2");
            absolute.Should.HaveRedirectLocation("https://api.prototest.dev/orders/1");

            var wrong = Assert.Throws<RestAssertionException>(
                () => relative.Should.HaveRedirectLocation("/orders/2"));
            Assert.That(wrong!.Message, Is.EqualTo(
                "GET /orders/42 — Expected redirect location '/orders/2', but received '/orders/1'."));

            var negated = Assert.Throws<RestAssertionException>(
                () => relative.ShouldNot.HaveRedirectLocation("/orders/1"));
            Assert.That(negated!.Message, Does.Contain("Expected redirect location not '/orders/1'"));
        }
    }

    [Test]
    public void HaveRedirectLocation_ShouldNameTheMissingLocation()
    {
        var response = Response("{}");

        var exception = Assert.Throws<RestAssertionException>(
            () => response.Should.HaveRedirectLocation("/orders/1"));

        Assert.That(exception!.Message, Does.Contain("but received none"));
    }

    [Test]
    public void HaveAssertions_ShouldChain()
    {
        var response = Response("{}", configure: raw =>
        {
            raw.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            raw.Headers.TryAddWithoutValidation("X-Correlation", "abc");
            raw.Headers.TryAddWithoutValidation("Set-Cookie", "session=abc123; Path=/");
            raw.Headers.Location = new Uri("/orders/1", UriKind.Relative);
        });

        var returned = response.Should.HaveContentType("application/json")
            .Should.HaveHeader("X-Correlation", "abc")
            .Should.HaveCookie("session", "abc123")
            .Should.HaveRedirectLocation("/orders/1");

        Assert.That(returned, Is.SameAs(response));
    }

    [Test]
    public async Task HaveHeader_ShouldRecordTheFailedAssertionNamingTheRequest()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rest header assertion", TestMethods.Placeholder);
        var response = new RestResponse(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") },
            "{}",
            TimeSpan.Zero,
            new ProtoHttpResponseContext(
                Execution: context,
                TargetName: "Orders",
                Identifier: "GET /orders/42"));

        var exception = Assert.Throws<RestAssertionException>(() => response.Should.HaveHeader("X-Missing"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.http.header");
        Assert.Multiple(() =>
        {
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(operation.Attributes["http.header.name"], Is.EqualTo("X-Missing"));
            Assert.That(operation.Attributes["request.identifier"], Is.EqualTo("GET /orders/42"));
            Assert.That(operation.Sections![0].Kind, Is.EqualTo(ProtoTraceSectionKind.Checks));
            Assert.That(operation.Sections![0].Items![0].Tone, Is.EqualTo(ProtoTraceSectionTone.Error));
        });
        await host.StopAsync();
    }

    private RestResponse Response(string content, Action<HttpResponseMessage>? configure = null)
    {
        var raw = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
        configure?.Invoke(raw);
        return new RestResponse(
            raw,
            content,
            TimeSpan.Zero,
            new ProtoHttpResponseContext(
                Execution: _context,
                TargetName: "Orders",
                Identifier: "GET /orders/42"));
    }
}
