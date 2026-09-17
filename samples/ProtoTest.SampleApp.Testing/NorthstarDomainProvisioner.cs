namespace ProtoTest.SampleApp.Testing;

using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;

/// <summary>
/// Creates projects through the domain instead of the API: the test composes the same domain over the
/// same database, so the application sees the result immediately and no HTTP call is involved.
/// </summary>
public sealed class NorthstarDomainProjectProvisioner : IProtoDataProvisioner<CreateProjectRequest, ProjectResponse>
{
    public ValueTask<ProtoDataProvisioningResult<ProjectResponse>> CreateAsync(
        CreateProjectRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        var store = context.Execution.Service<NorthstarStore>();
        var member = context.Execution.Resolve<NorthstarMemberContext>();
        var principal = store.Authenticate(member.Token);
        var project = store.CreateProject(principal, value.Name);
        return ValueTask.FromResult(new ProtoDataProvisioningResult<ProjectResponse>(project, project.Id));
    }
}
