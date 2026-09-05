namespace ProtoTest.AspNetCore.Demo;

using System.Net;
using ProtoTest.AspNetCore;
using ProtoTest.AspNetCore.SampleApi;
using ProtoTest.Core;
using ProtoTest.NUnit;

public class OrderApiTests
{
    [ProtoTest]
    public async Task GetPing_ReturnsOkResult()
    {
        // Act
        var client = Proto.Context.Client<HttpClient>("OrderApi");
        var response = await client.GetAsync("/ping");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [ProtoTest]
    public void ResolveService_ReturnsRegisteredDependency()
    {
        // Retrieve the service from the DI container from the OrderApi server
        var messageService = Proto.Context.ServerService<Program, ITestMessageService>("OrderApi");

        // Assert
        Assert.That(messageService, Is.Not.Null);
        Assert.That(messageService.GetMessage(), Is.EqualTo("Hello from AspNetCore DI!"));
    }
}