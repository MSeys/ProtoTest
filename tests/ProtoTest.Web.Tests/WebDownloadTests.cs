namespace ProtoTest.Web.Tests;

using NUnit.Framework;
using ProtoTest.Core;

/// <summary>
/// A browser download is named binary content: anything consuming `IProtoBinaryContent` (for example
/// the Sheets reader) accepts it directly instead of through a stream.
/// </summary>
public sealed class WebDownloadTests
{
    [Test]
    public void WebDownload_ShouldExposeItselfAsNamedBinaryContent()
    {
        var bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        var download = new WebDownload(
            "invoices-2030-06.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            new ReadOnlyMemory<byte>(bytes));

        Assert.That(download, Is.InstanceOf<IProtoBinaryContent>());
        var content = (IProtoBinaryContent)download;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(content!.FileName, Is.EqualTo("invoices-2030-06.xlsx"));
            Assert.That(
                content.MediaType,
                Is.EqualTo("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
            Assert.That(content.Content.ToArray(), Is.EqualTo(bytes));
        }
    }
}
