namespace ProtoTest.Demo;

using global::Grpc.Core;
using global::NUnit.Framework;
using Google.Protobuf;
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.Grpc;
using ProtoTest.Http;
using ProtoTest.NUnit;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;
using ProtoTest.SampleApp.Grpc;
using ProtoTest.SampleApp.Testing;

/// <summary>
/// The same application over gRPC: the shared <c>[Auth]</c> bearer token travels as metadata, the call
/// is traced as a grpc.call span, and the reply is asserted like any other value.
/// </summary>
[Application(NorthstarTargets.Api)]
[NorthstarTenant(PlanIds.Growth)]
[Auth<NorthstarAuthenticator>]
public sealed class GrpcJourney
{
    private static readonly Marshaller<GetProjectRequest> GetRequestMarshaller = Marshallers.Create<GetProjectRequest>(
        (request, context) => context.Complete(request.ToByteArray()),
        context => GetProjectRequest.Parser.ParseFrom(context.PayloadAsNewBuffer()));
    private static readonly Marshaller<ListProjectsRequest> ListRequestMarshaller = Marshallers.Create<ListProjectsRequest>(
        (request, context) => context.Complete(request.ToByteArray()),
        context => ListProjectsRequest.Parser.ParseFrom(context.PayloadAsNewBuffer()));
    private static readonly Marshaller<ProjectReply> ReplyMarshaller = Marshallers.Create<ProjectReply>(
        (reply, context) => context.Complete(reply.ToByteArray()),
        context => ProjectReply.Parser.ParseFrom(context.PayloadAsNewBuffer()));
    private static readonly Method<GetProjectRequest, ProjectReply> GetProjectMethod = new(
        MethodType.Unary, "northstar.v1.Projects", "GetProject", GetRequestMarshaller, ReplyMarshaller);
    private static readonly Method<ListProjectsRequest, ProjectReply> ListProjectsMethod = new(
        MethodType.ServerStreaming, "northstar.v1.Projects", "ListProjects", ListRequestMarshaller, ReplyMarshaller);

    [ProtoTest]
    [SignedInAs]
    public async Task AProjectIsReadableOverGrpc()
    {
        // Arrange over the API, read back over gRPC.
        var project = await Proto.Context.Data()
            .For<CreateProjectRequest>()
            .With(request => request.Name, "grpc-atlas")
            .CreateAsync<ProjectResponse>();

        // Act: unary, then the server stream.
        var client = Proto.Context.Grpc();
        var reply = await client.UnaryAsync(GetProjectMethod, new GetProjectRequest { ProjectId = project.Id });
        var listed = await client.ServerStreamingAsync(
            ListProjectsMethod,
            new ListProjectsRequest { Limit = 10 });

        // Assert: same identity, same state, from the other protocol.
        Assert.Multiple(() =>
        {
            reply.ShouldMatchShape(new
            {
                id = project.Id,
                name = "grpc-atlas",
                status = ProjectStatuses.Active
            });
            Assert.That(listed.Any(item => item.Id == project.Id), Is.True,
                "The project created over REST is visible on the gRPC stream.");
        });
    }
}
