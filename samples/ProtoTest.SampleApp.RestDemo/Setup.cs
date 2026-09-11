namespace ProtoTest.SampleApp.RestDemo;

using Microsoft.Extensions.Configuration;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.OpenApi;
using ProtoTest.Reporting;
using ProtoTest.Rest;
using ProtoTest.SampleApp;
using ProtoTest.SampleApp.Testing;

[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        builder
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [$"ProtoTest:Clients:{SampleAppTargets.Api}:OpenApi:Specification"] = Path.Combine(
                        AppContext.BaseDirectory,
                        "sampleapp.openapi.json")
                }))
            .AddRest(rest => rest
                .AddClient(SampleAppTargets.Api)
                .WithCollector<RestCoverageCollector>()
                .WithCollector<OpenApiCoverageCollector>())
            .AddAspNetCoreServer<Program>(SampleAppTargets.Api)
            .AddSink<JsonReportSink>(sink => sink.OutputPath = Path.Combine(
                "TestResults", "ProtoTest.SampleApp", "report.json"))
            .AddSink<HtmlReportSink>(sink =>
            {
                sink.OutputPath = Path.Combine("TestResults", "ProtoTest.SampleApp", "report.html");
                sink.Title = "ProtoTest Sample SaaS";
            });
    }
}
