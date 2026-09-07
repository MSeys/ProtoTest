namespace ProtoTest.Rest.Demo;

using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.Rest.Authenticators;

[RestClient("Inventory")]
public sealed class InventoryApiTests
{
    [ProtoTest]
    public async Task GetInventoryItem_UsesFluentAuthentication()
    {
        var response = await Proto.Context.Rest()
            .Auth<BearerTokenAuthenticator>("inventory-token")
            .GetAsync("/inventory/{sku}", new { sku = "notebook" });

        response
            .ShouldHaveStatus(HttpStatusCode.OK)
            .ShouldMatchShape(new
            {
                sku = "notebook",
                available = 27
            });
    }

    [ProtoTest]
    public async Task GetInventoryItem_RejectsWrongFluentToken()
    {
        var response = await Proto.Context.Rest()
            .Auth(new BearerTokenAuthenticator("wrong-token"))
            .GetAsync("/inventory/{sku}", new { sku = "notebook" });

        response.ShouldHaveStatus(HttpStatusCode.Unauthorized);
    }

}
