namespace Northstar.ProtoTest;

using System.Net;
using global::ProtoTest.Data;
using global::ProtoTest.Rest;

/// <summary>
/// The one shape every API provisioner shares: POST the request, require the created status, read the
/// response and return it with its id. A subclass names the URL, the body, the route values and the id,
/// so the API provisioners cannot drift from each other.
/// </summary>
public abstract class NorthstarApiProvisioner<TRequest, TResponse> : IProtoDataProvisioner<TRequest, TResponse>
{
    protected abstract string Url { get; }

    protected abstract object Body(TRequest value);

    protected virtual object? RouteValues(TRequest value) => null;

    protected abstract string IdOf(TResponse response);

    public async ValueTask<ProtoDataProvisioningResult<TResponse>> CreateAsync(
        TRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        using var response = await context.Execution.Rest()
            .Body(Body(value))
            .PostAsync(Url, RouteValues(value), ct: cancellationToken);
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        var created = response.ReadAsJson<TResponse>()
            ?? throw new InvalidOperationException(
                $"The sample app returned no provisioned {typeof(TResponse).Name}.");
        return new ProtoDataProvisioningResult<TResponse>(created, IdOf(created));
    }
}
