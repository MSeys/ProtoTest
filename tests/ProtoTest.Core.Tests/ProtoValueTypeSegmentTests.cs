namespace ProtoTest.Core.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class ProtoValueTypeSegmentTests
{
    [TestCase(typeof(Invoice), "invoice")]
    [TestCase(typeof(InvoiceLine), "invoice_line")]
    [TestCase(typeof(HTTPClient), "http_client")]
    [TestCase(typeof(OAuthToken), "o_auth_token")]
    [TestCase(typeof(Project2), "project2")]
    [TestCase(typeof(Invoice2Item), "invoice2_item")]
    public void TypeSegment_ShouldConvertTheClrTypeNameToSnakeCase(Type type, string expected)
        => Assert.That(ProtoTraceItemKeys.TypeSegment(type), Is.EqualTo(expected));

    [TestCase("invoice", "invoice")]
    [TestCase("invoiceLine", "invoice_line")]
    [TestCase("HTTPServerURL", "http_server_url")]
    [TestCase("already_snake", "already_snake")]
    [TestCase("Invoice__Line", "invoice_line")]
    [TestCase("_Invoice_", "invoice")]
    public void TypeSegment_ShouldNormalizeTypeNames(string typeName, string expected)
        => Assert.That(ProtoTraceItemKeys.TypeSegment(typeName), Is.EqualTo(expected));

    [Test]
    public void TypeSegment_ShouldRejectANullType()
        => Assert.Throws<ArgumentNullException>(() => ProtoTraceItemKeys.TypeSegment((Type)null!));

    [Test]
    public void TypeSegment_ShouldRejectABlankName()
        => Assert.Throws<ArgumentException>(() => ProtoTraceItemKeys.TypeSegment(" "));

    private sealed class Invoice
    {
    }

    private sealed class InvoiceLine
    {
    }

    private sealed class HTTPClient
    {
    }

    private sealed class OAuthToken
    {
    }

    private sealed class Project2
    {
    }

    private sealed class Invoice2Item
    {
    }
}
