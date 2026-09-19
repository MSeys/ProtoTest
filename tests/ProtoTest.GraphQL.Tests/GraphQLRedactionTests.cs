namespace ProtoTest.GraphQL.Tests;

using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

[TestFixture]
public sealed class GraphQLRedactionTests
{
    [Test]
    public async Task InlineSensitiveLiteral_ShouldBeRedactedInRecordedDocumentAndAttachments()
    {
        string? outgoing = null;
        await using var host = CreateHost(request =>
        {
            outgoing = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""{"data":{"login":{"token":"server-secret"}}}""");
        });
        await host.StartTestAsync("inline redaction", "1", Method());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Mutation("Login", mutation => mutation
                    .Field("login", field => field
                        .Argument("password", "hunter2")
                        .Argument("username", "ada")
                        .Fields("token")))
                .ExecuteAsync();

            var observation = (GraphQLResponseData)Proto.Context.RecordedObservations
                .Single(item => item.Kind == "graphql.response").Data!;
            var attachment = await ReadRequestAttachmentAsync();
            var outgoingQuery = ReadQuery(outgoing!);
            var attachmentQuery = ReadQuery(attachment);

            using (Assert.EnterMultipleScope())
            {
                // The request that leaves the process still carries the real value.
                Assert.That(outgoingQuery, Does.Contain("password: \"hunter2\""));
                Assert.That(outgoingQuery, Does.Contain("username: \"ada\""));

                Assert.That(observation.Document, Does.Not.Contain("hunter2"));
                Assert.That(observation.Document, Does.Contain("password: \"[REDACTED]\""));
                Assert.That(observation.Document, Does.Contain("username: \"ada\""));

                Assert.That(attachment, Does.Not.Contain("hunter2"));
                Assert.That(attachmentQuery, Does.Contain("password: \"[REDACTED]\""));
                Assert.That(attachmentQuery, Does.Contain("username: \"ada\""));
            }
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task VariableReference_ShouldKeepItsShapeWhileTheVariableValueIsRedacted()
    {
        string? outgoing = null;
        await using var host = CreateHost(request =>
        {
            outgoing = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""{"data":{"login":{"token":"server-secret"}}}""");
        });
        await host.StartTestAsync("variable redaction", "2", Method());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Mutation("Login", mutation => mutation
                    .Variable("password", GqlType.String.NonNull())
                    .Field("login", field => field
                        .Argument("password", Gql.Var("password"))
                        .Fields("token")))
                .Variables(new { password = "hunter2" })
                .ExecuteAsync();

            var observation = (GraphQLResponseData)Proto.Context.RecordedObservations
                .Single(item => item.Kind == "graphql.response").Data!;
            var attachment = await ReadRequestAttachmentAsync();
            var outgoingQuery = ReadQuery(outgoing!);
            var attachmentQuery = ReadQuery(attachment);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(outgoingQuery, Does.Contain("password: $password"));
                Assert.That(outgoing, Does.Contain("hunter2"));

                Assert.That(observation.Document, Does.Contain("password: $password"));
                Assert.That(observation.Document, Does.Not.Contain("hunter2"));
                Assert.That(observation.VariablesJson, Does.Contain("[REDACTED]"));
                Assert.That(observation.VariablesJson, Does.Not.Contain("hunter2"));

                Assert.That(attachmentQuery, Does.Contain("password: $password"));
                Assert.That(attachment, Does.Not.Contain("hunter2"));
            }
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task NonSensitiveLiteral_ShouldBeLeftUntouched()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"search":[]}}"""));
        await host.StartTestAsync("non-sensitive literal", "3", Method());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query("Search", query => query.Field("search", field => field.Argument("term", "notebook")))
                .ExecuteAsync();

            var observation = (GraphQLResponseData)Proto.Context.RecordedObservations
                .Single(item => item.Kind == "graphql.response").Data!;
            var attachmentQuery = ReadQuery(await ReadRequestAttachmentAsync());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(observation.Document, Does.Contain("term: \"notebook\""));
                Assert.That(attachmentQuery, Does.Contain("term: \"notebook\""));
            }
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task EscapedAndBlockStringLiterals_ShouldBeRedacted()
    {
        const string document = """"mutation { login(password: """hunter2""", note: "say \"hi\"") { token } }"""";
        await using var host = CreateHost(_ => Json("""{"data":{"login":{"token":"server-secret"}}}"""));
        await host.StartTestAsync("escaped literals", "4", Method());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Request(document)
                .ExecuteAsync();

            var observation = (GraphQLResponseData)Proto.Context.RecordedObservations
                .Single(item => item.Kind == "graphql.response").Data!;
            var attachmentQuery = ReadQuery(await ReadRequestAttachmentAsync());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(observation.Document, Does.Not.Contain("hunter2"));
                Assert.That(observation.Document, Does.Contain("password: \"[REDACTED]\""));
                Assert.That(observation.Document, Does.Contain("note: \"say \\\"hi\\\"\""));
                Assert.That(attachmentQuery, Does.Not.Contain("hunter2"));
                Assert.That(attachmentQuery, Does.Contain("password: \"[REDACTED]\""));
            }
        }
        finally { await host.CompleteTestAsync(); }
    }

    private static ProtoHost CreateHost(Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL =>
        {
            graphQL.CaptureAttachments();
            graphQL.AddClient("Default", "https://example.test/graphql", http =>
                http.ConfigurePrimaryHttpMessageHandler(() => new StubHandler(response)));
        });
        return builder.Build();
    }

    private static async Task<string> ReadRequestAttachmentAsync()
    {
        var attachment = Proto.Context.Attachments.Single(item =>
            item.Name.EndsWith("graphql-01-request", StringComparison.Ordinal));
        return Encoding.UTF8.GetString(await attachment.ReadAllBytesAsync());
    }

    private static string ReadQuery(string envelopeJson)
    {
        using var envelope = JsonDocument.Parse(envelopeJson);
        return envelope.RootElement.GetProperty("query").GetString()!;
    }

    private static HttpResponseMessage Json(string content)
        => new(HttpStatusCode.OK) { Content = new StringContent(content) };

    private static MethodInfo Method() => typeof(GraphQLRedactionTests)
        .GetMethod(nameof(Dummy), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static void Dummy() { }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }
}
