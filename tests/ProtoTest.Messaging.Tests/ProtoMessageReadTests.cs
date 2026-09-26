namespace ProtoTest.Messaging.Tests;

using System.Text.Json;

[TestFixture]
public sealed class ProtoMessageReadTests
{
    private sealed record Invoice(int Id, string? Reference);

    [Test]
    public void ReadAsJson_ShouldDeserializeThePayloadWithTheSharedReaderDefaults()
    {
        var message = new ProtoMessage("invoices", """{"id":1,"reference":"INV-1"}""");

        var invoice = message.ReadAsJson<Invoice>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(invoice, Is.Not.Null);
            Assert.That(invoice!.Id, Is.EqualTo(1));
            Assert.That(invoice.Reference, Is.EqualTo("INV-1"));
        }
    }

    [Test]
    public void ReadAsJson_ShouldReturnDefaultForAnEmptyPayload()
    {
        var message = new ProtoMessage("invoices");

        Assert.That(message.ReadAsJson<Invoice>(), Is.Null);
    }

    [Test]
    public void ReadAsJson_ShouldThrowForAWrongType()
    {
        var message = new ProtoMessage("invoices", """{"id":"not-a-number"}""");

        Assert.Throws<JsonException>(() => message.ReadAsJson<Invoice>());
    }

    [Test]
    public void ReadRequired_ShouldReturnTheValueWhenPresent()
    {
        var message = new ProtoMessage("invoices", """{"id":1,"reference":"INV-1"}""");

        var invoice = message.ReadRequired<Invoice>();

        Assert.That(invoice.Id, Is.EqualTo(1));
    }

    [Test]
    public void ReadRequired_ShouldNameTheDestinationForAnEmptyPayload()
    {
        var message = new ProtoMessage("invoices");

        var exception = Assert.Throws<MessagingAssertionException>(() => message.ReadRequired<Invoice>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.StartWith("invoices"));
            Assert.That(exception.Message, Does.Contain("empty"));
        }
    }

    [Test]
    public void ReadRequired_ShouldNameTheDestinationForAJsonNullPayload()
    {
        var message = new ProtoMessage("invoices", "null");

        var reference = Assert.Throws<MessagingAssertionException>(() => message.ReadRequired<Invoice>());
        var value = Assert.Throws<MessagingAssertionException>(() => message.ReadRequired<int>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reference!.Message, Does.Contain("JSON null"));
            Assert.That(value!.Message, Does.Contain("JSON null"));
        }
    }

    [Test]
    public void ReadRequired_ShouldReadAPathAndNameItOnAMiss()
    {
        var message = new ProtoMessage("invoices", """{"id":1,"customer":{"id":42}}""");

        Assert.That(message.ReadRequired<int>("$.customer.id"), Is.EqualTo(42));

        var exception = Assert.Throws<MessagingAssertionException>(
            () => message.ReadRequired<int>("$.customer.name"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.StartWith("invoices"));
            Assert.That(exception.Message, Does.Contain("$.customer.name"));
        }
    }

    [Test]
    public void ReadRequired_ShouldFailForAPathHoldingJsonNull()
    {
        var message = new ProtoMessage("invoices", """{"customer":{"id":null}}""");

        var exception = Assert.Throws<MessagingAssertionException>(
            () => message.ReadRequired<int>("$.customer.id"));

        Assert.That(exception!.Message, Does.Contain("JSON null"));
    }

    [Test]
    public void ReadRequired_ShouldRejectAnEmptyPath()
    {
        var message = new ProtoMessage("invoices", """{"id":1}""");

        Assert.Throws<ArgumentException>(() => message.ReadRequired<int>(" "));
    }
}
