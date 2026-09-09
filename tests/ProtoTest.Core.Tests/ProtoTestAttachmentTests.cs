namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Reflection;
using System.Text;

[TestFixture]
public sealed class ProtoTestAttachmentTests
{
    [Test]
    public async Task Context_ShouldStoreTextBinaryAndFileAttachments()
    {
        var filePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(filePath, "from-file");
        await using var context = CreateContext();

        context.AddAttachment("response", "{\"ok\":true}", "application/json");
        context.AddAttachment("image", new byte[] { 1, 2, 3 }, "image/png");
        context.AddAttachmentFile(filePath, "log");

        Assert.That(context.Attachments.Select(attachment => attachment.Name),
            Is.EqualTo(new[] { "00001-response", "00001-image", "00001-log" }));
        Assert.That(
            Encoding.UTF8.GetString(await context.Attachments[0].ReadAllBytesAsync()),
            Is.EqualTo("{\"ok\":true}"));
        Assert.That(context.Attachments[2].FilePath, Is.EqualTo(filePath));

        File.Delete(filePath);
    }

    [Test]
    public async Task CompleteTest_ShouldPublishAttachmentsAddedDuringTeardown()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProtoTestHook, AttachmentAfterHook>();
        await using var host = new ProtoHost(services.BuildServiceProvider());
        var publisher = new RecordingPublisher();

        var context = await host.StartTestAsync(
            "Attachments",
            "00001",
            (MethodInfo)MethodInfo.GetCurrentMethod()!,
            attachmentPublisher: publisher);
        context.AddAttachment("body", "response", "application/json");

        await host.CompleteTestAsync();

        Assert.That(publisher.Attachments.Select(attachment => attachment.Name),
            Is.EqualTo(new[] { "00001-body", "00001-after-hook" }));
    }

    [Test]
    public async Task Context_ShouldRejectDuplicateAttachmentNames()
    {
        await using var context = CreateContext();
        context.AddAttachment("response", "first");

        Assert.Throws<InvalidOperationException>(() => context.AddAttachment("RESPONSE", "second"));
    }

    [Test]
    public async Task PublisherFailure_ShouldStillDisposeContextAndClearAmbientState()
    {
        var services = new ServiceCollection();
        services.AddScoped<DisposableDependency>();
        await using var host = new ProtoHost(services.BuildServiceProvider());
        var context = await host.StartTestAsync(
            "Attachments",
            "00001",
            (MethodInfo)MethodInfo.GetCurrentMethod()!,
            attachmentPublisher: new ThrowingPublisher());
        var dependency = context.Service<DisposableDependency>();
        context.AddAttachment("body", "response");

        Assert.ThrowsAsync<InvalidOperationException>(async () => await host.CompleteTestAsync());

        Assert.That(dependency.IsDisposed, Is.True);
        Assert.Throws<InvalidOperationException>(() => _ = Proto.Context);
    }

    private static ProtoExecutionContext CreateContext()
        => new(
            "Attachments",
            new ServiceCollection().BuildServiceProvider().CreateScope(),
            "00001",
            (MethodInfo)MethodInfo.GetCurrentMethod()!);

    private sealed class AttachmentAfterHook : IProtoTestHook
    {
        public Task AfterTestAsync(ProtoExecutionContext context)
        {
            context.AddAttachment("after-hook", "created during teardown");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPublisher : IProtoTestAttachmentPublisher
    {
        public List<ProtoTestAttachment> Attachments { get; } = [];

        public ValueTask PublishAsync(
            ProtoTestAttachment attachment,
            CancellationToken cancellationToken = default)
        {
            Attachments.Add(attachment);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingPublisher : IProtoTestAttachmentPublisher
    {
        public ValueTask PublishAsync(
            ProtoTestAttachment attachment,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException("Publishing failed."));
    }

    private sealed class DisposableDependency : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }
}
