namespace ProtoTest.Http.Tests;

using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

[TestFixture]
public sealed class ProtoHttpOptionsResolverTests
{
    private static IConfiguration EmptyConfiguration => new ConfigurationBuilder().Build();

    [Test]
    public async Task ResolveResponseOptions_ShouldNotFallBackToAnUnkeyedRegistration()
    {
        var services = new ServiceCollection()
            .AddSingleton(EmptyConfiguration)
            .AddSingleton(new ProtoHttpResponseOptions { MaxResponseBodyBytes = 4 })
            .BuildServiceProvider();
        await using var context = new ProtoExecutionContext(
            "unkeyed response options",
            services.CreateScope(),
            "00001",
            (MethodInfo)MethodInfo.GetCurrentMethod()!);

        var options = context.ResolveResponseOptions("GraphQL");

        Assert.That(options.MaxResponseBodyBytes, Is.EqualTo(new ProtoHttpResponseOptions().MaxResponseBodyBytes));
    }

    [Test]
    public async Task ResolveAttachmentOptions_ShouldNotFallBackToAnUnkeyedRegistration()
    {
        var services = new ServiceCollection()
            .AddSingleton(EmptyConfiguration)
            .AddSingleton(new ProtoHttpAttachmentOptions())
            .BuildServiceProvider();
        await using var context = new ProtoExecutionContext(
            "unkeyed attachment options",
            services.CreateScope(),
            "00002",
            (MethodInfo)MethodInfo.GetCurrentMethod()!);

        Assert.That(context.ResolveAttachmentOptions("GraphQL"), Is.Null);
    }

    [Test]
    public async Task ConfigureResponseOptions_CalledTwice_ShouldApplyBothCallbacksInOrder()
    {
        var services = new ServiceCollection().AddSingleton(EmptyConfiguration);
        ProtoHttpOptionsRegistration.ConfigureResponseOptions(
            services, "Rest", "ProtoTest:Rest:Responses",
            options => options.MaxResponseBodyBytes = 2048);
        ProtoHttpOptionsRegistration.ConfigureResponseOptions(
            services, "Rest", "ProtoTest:Rest:Responses",
            options => options.MaxDiagnosticBodyLength = 64);
        await using var provider = services.BuildServiceProvider();
        await using var context = new ProtoExecutionContext(
            "composed response options",
            provider.CreateScope(),
            "00003",
            (MethodInfo)MethodInfo.GetCurrentMethod()!);

        var options = context.ResolveResponseOptions("Rest");

        Assert.Multiple(() =>
        {
            Assert.That(options.MaxResponseBodyBytes, Is.EqualTo(2048));
            Assert.That(options.MaxDiagnosticBodyLength, Is.EqualTo(64));
        });
    }

    [Test]
    public async Task ConfigureAttachmentOptions_CalledTwice_ShouldApplyBothCallbacksInOrder()
    {
        var services = new ServiceCollection().AddSingleton(EmptyConfiguration);
        ProtoHttpOptionsRegistration.ConfigureAttachmentOptions(
            services, "GraphQL", "ProtoTest:GraphQL:Attachments",
            options => options.CaptureResponses = false);
        ProtoHttpOptionsRegistration.ConfigureAttachmentOptions(
            services, "GraphQL", "ProtoTest:GraphQL:Attachments",
            options => options.MaxDiagnosticBodyLength = 128);
        await using var provider = services.BuildServiceProvider();
        await using var context = new ProtoExecutionContext(
            "composed attachment options",
            provider.CreateScope(),
            "00004",
            (MethodInfo)MethodInfo.GetCurrentMethod()!);

        var options = context.ResolveAttachmentOptions("GraphQL");

        Assert.Multiple(() =>
        {
            Assert.That(options, Is.Not.Null);
            Assert.That(options!.CaptureResponses, Is.False);
            Assert.That(options.MaxDiagnosticBodyLength, Is.EqualTo(128));
        });
    }

    [Test]
    public async Task ConfigureResponseOptions_AfterTryAdd_ShouldApplyConfigurationAndBindTheSection()
    {
        var services = new ServiceCollection().AddSingleton(EmptyConfiguration);
        ProtoHttpOptionsRegistration.TryAddResponseOptions(services, "Rest", "ProtoTest:Rest:Responses");
        ProtoHttpOptionsRegistration.ConfigureResponseOptions(
            services, "Rest", "ProtoTest:Rest:Responses",
            options => options.MaxDiagnosticBodyLength = 64);
        await using var provider = services.BuildServiceProvider();
        await using var context = new ProtoExecutionContext(
            "configured after try-add",
            provider.CreateScope(),
            "00005",
            (MethodInfo)MethodInfo.GetCurrentMethod()!);

        var options = context.ResolveResponseOptions("Rest");

        Assert.That(options.MaxDiagnosticBodyLength, Is.EqualTo(64));
    }
}
