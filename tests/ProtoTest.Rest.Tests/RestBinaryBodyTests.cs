namespace ProtoTest.Rest.Tests;

using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.TestSupport;

[TestFixture]
public sealed class RestBinaryBodyTests
{
    [Test]
    public async Task BinaryRequestBody_ShouldAttachExactBytes()
    {
        var expected = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0xFF, 0x00, 0x01, 0x80 };
        var handler = new TestHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            }
        };
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.CaptureAttachments().AddClient(
            "Files",
            "https://files.test",
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        await using var host = builder.Build();
        var context = await host.StartTestAsync("binary request", "20030", TestMethods.Placeholder);

        try
        {
            using var response = await context.Rest("Files")
                .Body(expected, "application/octet-stream")
                .PostAsync("/upload");

            response.Should.HaveHttpStatus(HttpStatusCode.OK);

            var requestAttachment = context.Attachments.Single(attachment => attachment.Name.EndsWith("-request"));
            Assert.That(requestAttachment.MediaType, Is.EqualTo("application/octet-stream"));
            Assert.That(await requestAttachment.ReadAllBytesAsync(), Is.EqualTo(expected));
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task BinaryResponseBody_ShouldNotRecordJsonCodeSection()
    {
        var expected = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0xFF, 0x00, 0x01, 0x80 };
        var handler = new TestHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expected)
            }
        };
        handler.ResponseToReturn.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.CaptureAttachments().AddClient(
            "Files",
            "https://files.test",
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        await using var host = builder.Build();
        var context = await host.StartTestAsync("binary response", "20031", TestMethods.Placeholder);

        try
        {
            using var response = await context.Rest("Files").GetAsync("/image.png");

            response.Should.HaveHttpStatus(HttpStatusCode.OK);
            Assert.That(response.ReadAsBytes(), Is.EqualTo(expected));

            var operation = host.Trace.Snapshot().Tests.Single()
                .Entries.Single(entry => entry.Kind == "http.request");
            var responseFields = operation.Sections!.Single(section => section.Label == "Response");
            Assert.Multiple(() =>
            {
                Assert.That(responseFields.Items!.Any(item => item.Label == "type" && item.Value == "image/png"), Is.True);
                Assert.That(responseFields.Items!.Any(item => item.Label == "length" && item.Value == $"{expected.Length} B"), Is.True);
                Assert.That(operation.Sections!.Where(section => section.Label == "Body"), Is.Empty);
            });

            var responseAttachment = context.Attachments.Single(attachment => attachment.Name.EndsWith("-response"));
            Assert.Multiple(async () =>
            {
                Assert.That(responseAttachment.MediaType, Is.EqualTo("image/png"));
                Assert.That(await responseAttachment.ReadAllBytesAsync(), Is.EqualTo(expected));
            });
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task TextBodies_ShouldKeepSanitizedAttachmentAndJsonCodeSection()
    {
        const string json = """{"name":"Matthias"}""";
        var handler = new TestHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id": 1}""", Encoding.UTF8, "application/json")
            }
        };
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.CaptureAttachments().AddClient(
            "Orders",
            "https://orders.test",
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        await using var host = builder.Build();
        var context = await host.StartTestAsync("text bodies", "20032", TestMethods.Placeholder);

        try
        {
            using var response = await context.Rest("Orders")
                .Body(json, "application/json")
                .PostAsync("/users");

            response.Should.HaveHttpStatus(HttpStatusCode.OK);

            var requestAttachment = context.Attachments.Single(attachment => attachment.Name.EndsWith("-request"));
            Assert.Multiple(async () =>
            {
                Assert.That(requestAttachment.MediaType, Is.EqualTo("application/json"));
                Assert.That(Encoding.UTF8.GetString(await requestAttachment.ReadAllBytesAsync()), Is.EqualTo(json));
            });

            var operation = host.Trace.Snapshot().Tests.Single()
                .Entries.Single(entry => entry.Kind == "http.request");
            var body = operation.Sections!.Single(section => section.Label == "Body");
            Assert.Multiple(() =>
            {
                Assert.That(body.Kind, Is.EqualTo(ProtoTraceSectionKind.Code));
                Assert.That(body.Language, Is.EqualTo("json"));
                Assert.That(body.Content, Does.Contain("\"id\": 1"));
            });
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }
}
