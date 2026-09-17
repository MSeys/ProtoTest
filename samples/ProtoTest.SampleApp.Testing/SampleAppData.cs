namespace ProtoTest.SampleApp.Testing;

using System.Net;
using ProtoTest.Data;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;

public sealed class SampleAppDataDefaults : IProtoDataDefaultsModule
{
    public void Configure(ProtoDataConfiguration data)
    {
        data.For<CreateUserRequest>()
            .Default(
                request => request.Email,
                context => $"user-{context.TestId}-{context.ObjectSequence:D4}@example.test");
    }
}

public sealed class SampleUserProvisioner : IProtoDataProvisioner<CreateUserRequest, UserResponse>
{
    public async ValueTask<ProtoDataProvisioningResult<UserResponse>> CreateAsync(
        CreateUserRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        var environment = context.Execution.Resolve<SampleEnvironmentContext>();
        using var response = await context.Execution.Rest()
            .WithoutAuth()
            .Body(value)
            .PostAsync(
                "/test-support/environments/{tenant}/users",
                new { environment.Tenant },
                cancellationToken);
        response.ShouldHaveHttpStatus(HttpStatusCode.Created);
        var user = response.ReadAsJson<UserResponse>()
            ?? throw new InvalidOperationException("The sample app returned no provisioned user.");
        return new ProtoDataProvisioningResult<UserResponse>(user, user.Id);
    }
}
