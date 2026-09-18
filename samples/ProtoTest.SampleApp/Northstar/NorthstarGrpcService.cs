namespace ProtoTest.SampleApp.Northstar;

using global::Grpc.Core;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;
using ProtoTest.SampleApp.Grpc;

/// <summary>
/// The same domain over gRPC: metadata carries the bearer token exactly like the REST API's header, so
/// the service authenticates the same way and a gRPC client is just another view of the application.
/// </summary>
internal sealed class NorthstarGrpcService(NorthstarStore store) : Projects.ProjectsBase
{
    public override Task<ProjectReply> GetProject(GetProjectRequest request, ServerCallContext context)
        => Task.FromResult(ToReply(store.GetProject(Principal(context), request.ProjectId)));

    public override async Task ListProjects(
        ListProjectsRequest request,
        IServerStreamWriter<ProjectReply> responseStream,
        ServerCallContext context)
    {
        var page = store.ListProjects(
            Principal(context),
            cursor: null,
            limit: request.Limit <= 0 ? 25 : request.Limit);
        foreach (var project in page.Items)
        {
            await responseStream.WriteAsync(ToReply(project));
        }
    }

    private NorthstarPrincipal Principal(ServerCallContext context)
        => store.Authenticate(Token(context));

    private static string? Token(ServerCallContext context)
        => context.RequestHeaders.GetValue("authorization") is { } header
            && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;

    private static ProjectReply ToReply(ProjectResponse project) => new()
    {
        Id = project.Id,
        Name = project.Name,
        Slug = project.Slug,
        Status = project.Status,
        EnvironmentCount = project.EnvironmentCount
    };
}
