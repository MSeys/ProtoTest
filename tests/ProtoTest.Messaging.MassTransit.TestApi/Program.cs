namespace ProtoTest.Messaging.MassTransit.TestApi;

using global::MassTransit;

/// <summary>A command the application consumes: the suite publishes it and the application answers with an event.</summary>
public sealed record PaymentReceived(int InvoiceId, decimal Amount);

/// <summary>The event the application publishes for a received payment; the suite awaits it.</summary>
public sealed record InvoicePaid(int InvoiceId, decimal Amount);

/// <summary>The interface contract the application publishes; the suite awaits it by its interface name.</summary>
public interface IInvoiceNotified
{
    int InvoiceId { get; }
}

/// <summary>The concrete message the application publishes as the interface contract.</summary>
public sealed record InvoiceNotified(int InvoiceId) : IInvoiceNotified;

/// <summary>The application's own consumer: it publishes the event the suite awaits.</summary>
public sealed class PaymentReceivedConsumer(IPublishEndpoint publisher) : IConsumer<PaymentReceived>
{
    public Task Consume(ConsumeContext<PaymentReceived> context)
        => publisher.Publish(
            new InvoicePaid(context.Message.InvoiceId, context.Message.Amount),
            context.CancellationToken);
}

public sealed class Program
{
    public static void Main(string[] args) => CreateApp(args).Run();

    /// <summary>
    /// Builds the application without running it. The application composes the MassTransit test harness
    /// the suite's bridge reads, so the application's own bus is the one under test.
    /// </summary>
    public static WebApplication CreateApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<PaymentReceivedConsumer>());
        var app = builder.Build();
        app.MapGet("/ping", () => Results.Ok(new { message = "pong" }));
        app.MapPost("/invoices/{invoiceId:int}/pay", async (
            int invoiceId,
            IPublishEndpoint publisher,
            CancellationToken cancellationToken) =>
        {
            await publisher.Publish(new InvoicePaid(invoiceId, 10.5m), cancellationToken);
            return Results.Ok();
        });
        app.MapPost("/invoices/{invoiceId:int}/notify", async (
            int invoiceId,
            IPublishEndpoint publisher,
            CancellationToken cancellationToken) =>
        {
            await publisher.Publish<IInvoiceNotified>(new InvoiceNotified(invoiceId), cancellationToken);
            return Results.Ok();
        });
        return app;
    }
}
