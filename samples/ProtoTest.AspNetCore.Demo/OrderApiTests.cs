namespace ProtoTest.AspNetCore.Demo;

using System.Net.Http.Json;
using ProtoTest.AspNetCore;
using ProtoTest.AspNetCore.SampleApi;
using ProtoTest.Core;
using ProtoTest.NUnit;

public class OrderApiTests
{
    [ProtoTest]
    public async Task GetPing_ReturnsExpectedPayload()
    {
        // Act
        var client = Proto.Context.Client<HttpClient>("OrderApi");
        var response = await client.GetFromJsonAsync<PingResponse>("/ping");

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Message, Is.EqualTo("pong"));
    }

    [ProtoTest]
    public async Task GetMessage_UsesApplicationDependencyInjection()
    {
        var client = Proto.Context.Client<HttpClient>("OrderApi");
        var response = await client.GetFromJsonAsync<MessageResponse>("/message");

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Message, Is.EqualTo("Hello from AspNetCore DI!"));
    }

    [ProtoTest]
    public void ResolveService_ReturnsRegisteredDependency()
    {
        var messageService = Proto.Context.ServerService<Program, ITestMessageService>("OrderApi");

        Assert.That(messageService, Is.Not.Null);
        Assert.That(messageService.GetMessage(), Is.EqualTo("Hello from AspNetCore DI!"));
    }

    private sealed record PingResponse(string Message);

    private sealed record MessageResponse(string Message);
}