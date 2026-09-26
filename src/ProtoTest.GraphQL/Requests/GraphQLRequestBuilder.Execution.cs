namespace ProtoTest.GraphQL;

using System.Collections;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.GraphQL.Internal;
using ProtoTest.Http;
using ProtoTest.Json;

public sealed partial class GraphQLRequestBuilder
{
    public async Task<GraphQLResponse> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var operation = _operation ?? throw new InvalidOperationException("Configure a query, mutation, or request before executing it.");
        if (operation.Kind == GraphQLOperationKind.Subscription)
            throw new InvalidOperationException("Subscriptions return a stream. Use SubscribeAsync instead of ExecuteAsync.");
        var identifier = $"{operation.Kind.WireName()} {operation.Name ?? "<anonymous>"}";
        var operationScope = Context.Trace
            .Operation("graphql.operation", $"GraphQL · {identifier}", ProtoGraphQLBuilder.Protocol.TraceSource)
            .ForClient(typeof(HttpClient), TargetName, ClientEntityName)
            .With("graphql.operation.type", operation.Kind.WireName())
            .With("graphql.operation.name", operation.Name);
        if (Headers.Count > 0)
        {
            operationScope = operationScope.With("http.request.header_count", Headers.Count.ToString());
        }

        using var traceOperation = operationScope.Begin();
        var stopwatch = Stopwatch.StartNew();
        Uri endpoint;
        using var resolveOperation = Context.Trace
            .Operation("graphql.endpoint.resolve", $"Resolve endpoint · {TargetName}", ProtoGraphQLBuilder.Protocol.TraceSource)
            .With("client.name", TargetName)
            .Begin();
        try
        {
            endpoint = await ProtoHttpEndpoint.ResolveBaseAddressAsync(
                Client,
                ProtoGraphQLBuilder.ProtocolName,
                TargetName,
                BaseAddressResolver,
                Context,
                cancellationToken);
            resolveOperation.SetAttribute("server.address", endpoint.GetLeftPart(UriPartial.Authority));
            resolveOperation.Succeed();
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            resolveOperation.Fail(exception);
            traceOperation.Fail(exception);
            TryRecordFailure(operation, identifier, null, stopwatch.Elapsed, exception, cancellationToken);
            throw;
        }
        try
        {
            var prepared = await PrepareRequestAsync(
                operation,
                identifier,
                endpoint,
                traceOperation,
                subscription: false,
                cancellationToken);
            using var request = prepared.Request;
            var attachmentOptions = prepared.AttachmentOptions;

            HttpResponseMessage? rawResponse = null;
            try
            {
                var exchange = await ProtoHttpExchange.SendAsync(
                    Client,
                    request,
                    ResolveResponseOptions().MaxResponseBodyBytes,
                    cancellationToken);
                rawResponse = exchange.Response;
                var content = exchange.Body;
                stopwatch.Stop();
                var response = new GraphQLResponse(
                    rawResponse,
                    content,
                    stopwatch.Elapsed,
                    new ProtoHttpResponseContext(
                        Execution: Context,
                        TargetName: TargetName,
                        Identifier: identifier,
                        AttachmentOptions: attachmentOptions,
                        AttachmentPrefix: prepared.AttachmentPrefix,
                        RequestTraceId: traceOperation.Id),
                    operation,
                    _shapePlan?.RootField);

                if (attachmentOptions?.CaptureResponses == true)
                    Context.AddAttachment(
                        $"{prepared.AttachmentPrefix}-response",
                        JsonDiagnosticSanitizer.Sanitize(
                            GraphQLDocumentRedactor.Redact(content, attachmentOptions),
                            attachmentOptions),
                        rawResponse.Content.Headers.ContentType?.MediaType ?? "application/json",
                        identifier);

                Context.RecordObservation(GraphQLObservations.Response(
                    TargetName,
                    identifier,
                    operation,
                    attachmentOptions,
                    (int)rawResponse.StatusCode,
                    response,
                    stopwatch.Elapsed,
                    prepared.VariablesJson));
                traceOperation
                    .SetAttribute("http.response.status_code", ((int)rawResponse.StatusCode).ToString())
                    .SetAttribute("graphql.error.count", response.Errors.Count.ToString());
                traceOperation.Succeed();
                rawResponse = null; // GraphQLResponse owns the response from here on.
                return response;
            }
            finally
            {
                rawResponse?.Dispose();
            }
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            traceOperation.Fail(exception);
            TryRecordFailure(operation, identifier, endpoint, stopwatch.Elapsed, exception, cancellationToken);
            throw;
        }
    }


    private sealed record PreparedGraphQLRequest(
        HttpRequestMessage Request,
        GraphQLRequestContent Content,
        string? VariablesJson,
        ProtoHttpAttachmentOptions? AttachmentOptions,
        string? AttachmentPrefix);

    /// <summary>
    /// Builds the request both execution paths send: the content envelope, headers and preflight flag,
    /// the authenticator, and the request-body attachment with the per-client sequence. A subscription
    /// adds its transport's Accept header; a query or mutation always asks for the GraphQL response
    /// media type.
    /// </summary>
    private async Task<PreparedGraphQLRequest> PrepareRequestAsync(
        GraphQLBuiltOperation operation,
        string identifier,
        Uri endpoint,
        ProtoTraceOperation? authOperation,
        bool subscription,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        try
        {
            if (!subscription)
            {
                request.Headers.Accept.Add(GraphQLMediaType);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json", 0.9));
            }
            else if (_subscriptionTransport == GraphQLSubscriptionTransport.Sse)
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            }

            var requestContent = GraphQLRequestContent.Create(operation.DocumentText, operation.Name, _variables);
            request.Content = requestContent.Content;
            ProtoHttpHeaders.Apply(request, Headers);
            if (requestContent.RequiresPreflight)
                request.Headers.TryAddWithoutValidation("GraphQL-preflight", "1");

            // A subscription has no request operation to attach the outcome to; an auth failure still
            // surfaces through the subscription's error path.
            ResolvedAuthenticator = await ProtoHttpAuthenticationApplier.ApplyAsync(
                AuthenticatorFactory,
                ResolvedAuthenticator,
                request,
                Context,
                TargetName,
                authOperation,
                cancellationToken);

            var attachmentOptions = Context.ResolveAttachmentOptions(ProtoGraphQLBuilder.ProtocolName);
            var requestNumber = attachmentOptions is null
                ? (int?)null
                : (Context.TryResolve<ProtoHttpContextState>(ProtoGraphQLBuilder.Protocol.Key)
                    ?? throw new InvalidOperationException("GraphQL context state was not initialized.")).NextRequestNumber();
            var attachmentPrefix = requestNumber is null ? null : $"graphql-{requestNumber:00}";
            if (attachmentOptions?.CaptureRequestBodies == true)
            {
                Context.AddAttachment(
                    $"{attachmentPrefix}-request",
                    JsonDiagnosticSanitizer.Sanitize(
                        GraphQLDocumentRedactor.RedactEnvelope(requestContent.DiagnosticJson, attachmentOptions),
                        attachmentOptions),
                    "application/json",
                    identifier);
            }

            var variablesJson = requestContent.VariablesJson is null
                ? null
                : JsonDiagnosticSanitizer.Sanitize(requestContent.VariablesJson, attachmentOptions);
            return new PreparedGraphQLRequest(request, requestContent, variablesJson, attachmentOptions, attachmentPrefix);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private void TryRecordFailure(
        GraphQLBuiltOperation operation,
        string identifier,
        Uri? requestUri,
        TimeSpan duration,
        Exception exception,
        CancellationToken cancellationToken)
        => ProtoObservationCapture.TryRecord(Context, ProtoGraphQLBuilder.Protocol, () =>
        {
            var attachmentOptions = Context.ResolveAttachmentOptions(ProtoGraphQLBuilder.ProtocolName);
            var diagnostics = ProtoFailureDiagnostics.From(
                requestUri,
                exception,
                cancellationToken,
                attachmentOptions,
                attachmentOptions?.SensitiveQueryParameters);
            return new ProtoObservation(
                TargetName,
                ProtoGraphQLBuilder.FailureObservationKind,
                identifier,
                new GraphQLFailureData(
                    operation.Kind.WireName(),
                    operation.Name,
                    diagnostics.RequestUri,
                    duration,
                    diagnostics.ExceptionType,
                    diagnostics.Message,
                    diagnostics.IsCanceled));
        });


}
