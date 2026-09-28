namespace ProtoTest.Messaging.RabbitMq.Tests;

using System.Reflection;
using System.Text;
using global::RabbitMQ.Client;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.RabbitMq;
using ProtoTest.Messaging.RabbitMq.Testcontainers;

[TestFixture]
public sealed class RabbitMqTests
{
    // One lazy start shared by every parallel test: a check-then-act here would start a container
    // per racing caller and leak all but the last.
    private static readonly Lazy<(RabbitMqBroker? Broker, string? Error)> Container = new(
        static () =>
        {
            var result = RabbitMqBroker.TryStart();
            return (result.Resource, result.Error);
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    [OneTimeTearDown]
    public static async Task StopContainer()
    {
        if (Container.IsValueCreated && Container.Value.Broker is { } container)
        {
            await container.DisposeAsync();
        }
    }

    [Test]
    public async Task UseRabbitMq_ShouldRegisterTheCapabilityAndBindConfiguration()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Messaging:RabbitMq:ConnectionString"] = "amqp://127.0.0.1:1/"
            }));
        builder.AddMessaging(messaging => messaging.UseRabbitMq());
        await using var host = builder.Build();
        Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.True);
        await host.StartAsync();
        var context = await host.StartTestAsync("rabbit options", TestMethods.Placeholder);

        var options = context.Service<RabbitMqOptions>();
        var unreachable = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Messaging().PublishAsync("invoices", "{}"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(unreachable!));
        Assert.Multiple(() =>
        {
            Assert.That(options.ConnectionString, Is.EqualTo("amqp://127.0.0.1:1/"));
            Assert.That(unreachable!.Message, Does.Contain("ProtoTest:Messaging:RabbitMq:ConnectionString"));
        });
    }

    [Test]
    public async Task UseRabbitMq_ShouldRunTheConfigureCallbackOncePerRegistration()
    {
        var calls = 0;
        var builder = new ProtoHostBuilder();
        builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
        {
            calls++;
            options.ConnectionString = "amqp://127.0.0.1:1/";
        }));

        await using var host = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(1), "the capability decision must not invoke the callback a second time");
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.True,
                "a code-provided address keeps the capability unconditional");
        });

        await host.StartAsync();
        var context = await host.StartTestAsync("rabbit callback count", TestMethods.Placeholder);
        var options = context.Service<RabbitMqOptions>();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(1), "resolving the options must not run the callback again");
            Assert.That(options.ConnectionString, Is.EqualTo("amqp://127.0.0.1:1/"));
        });
    }

    [Test]
    public async Task PublishAndAwait_ShouldRoundTripAgainstARealBroker()
    {
        // A configured broker wins; otherwise the test owns a container. A machine with neither skips
        // instead of passing against the in-memory double.
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Fanout);

        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    // The test's tap queue is declared at setup; the exchange exists before the publish.
                    ["ProtoTest:Messaging:Destinations:0"] = exchange
                }));
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
                options.ConnectionString = connectionString));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit round trip", TestMethods.Placeholder);
            var messages = context.Messaging();

            await messages.PublishAsync(exchange, "{\"id\":1}", contentType: "application/json");
            var received = await messages.AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(15));

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            Assert.That(received.ContentType, Is.EqualTo("application/json"));
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task Tap_ShouldPreBindTheDestinationSoAnEarlierPublishIsReceived()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Fanout);

        try
        {
            // The tap is declared in code, so the consumer prepares it during test setup: the message
            // published before the test's first await is still delivered to this test's queue.
            var builder = new ProtoHostBuilder();
            builder.AddMessaging(messaging => messaging
                .UseRabbitMq(options => options.ConnectionString = connectionString)
                .Tap(exchange));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit tap prebind", TestMethods.Placeholder);
            var messages = context.Messaging();

            await messages.PublishAsync(exchange, "{\"id\":1}", contentType: "application/json");
            var received = await messages.AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(15));

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            Assert.That(received.ContentType, Is.EqualTo("application/json"));
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task Declare_ShouldCreateTheDestinationBeforeTheSuitePublishes()
    {
        var connectionString = RequireBroker();

        // The suite itself declares the exchange: no fixture pre-creates it with a raw client, so this
        // is the documented path for a suite that owns the broker and publishes its own events.
        var exchange = $"prototest.tests.{Guid.NewGuid():N}";

        try
        {
            var builder = new ProtoHostBuilder();
            builder.AddMessaging(messaging => messaging
                .UseRabbitMq(options => options.ConnectionString = connectionString)
                .Declare(exchange)
                .Tap(exchange));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit declared destination", TestMethods.Placeholder);
            var messages = context.Messaging();

            // The declaration ran during setup: the exchange exists before the act publishes, and the
            // tap bound its queue to it.
            await AssertExchangeExistsAsync(connectionString, exchange);

            await messages.PublishAsync(exchange, "{\"id\":1}", contentType: "application/json");
            var received = await messages.AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(15));

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            Assert.That(received.ContentType, Is.EqualTo("application/json"));
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task Declare_ShouldBeIdempotentWhenRepeatedAndAlreadyOnTheBroker()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        // The application's shape: a fanout, durable exchange a suite may declare again.
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Fanout, durable: true, autoDelete: false);

        try
        {
            var builder = new ProtoHostBuilder();
            builder.AddMessaging(messaging => messaging
                .UseRabbitMq(options => options.ConnectionString = connectionString)
                .Declare(exchange, exchange)
                .Declare(exchange)
                .Tap(exchange));
            await using var host = builder.Build();
            await host.StartAsync();

            // The first test's prepare declares the destination (an existing exchange is left as is);
            // the second test's prepare repeats the declaration. A repeat is a no-op, not a 406.
            var first = await host.StartTestAsync("rabbit declare twice", TestMethods.Placeholder);
            await first.Messaging().PublishAsync(exchange, "{\"id\":1}");
            var firstReceived = await first.Messaging().AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(15));
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            Assert.That(firstReceived.Payload, Is.EqualTo("{\"id\":1}"));

            var second = await host.StartTestAsync("rabbit declare repeated", TestMethods.Placeholder);
            await second.Messaging().PublishAsync(exchange, "{\"id\":2}");
            var secondReceived = await second.Messaging().AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":2}",
                TimeSpan.FromSeconds(15));
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            Assert.That(secondReceived.Payload, Is.EqualTo("{\"id\":2}"));
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task Declare_WhenTheDestinationExistsWithOtherProperties_ShouldFailSetupNamingTheDestination()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        // A direct, non-durable exchange with the declared name: the suite's declaration cannot match it.
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Direct, durable: false, autoDelete: true);

        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options => options.Enabled = false);
            builder.AddMessaging(messaging => messaging
                .UseRabbitMq(options => options.ConnectionString = connectionString)
                .Declare(exchange));
            await using var host = builder.Build();
            await host.StartAsync();

            // The suite stated the destination exists; the broker refusing the declaration must fail
            // setup with the destination named instead of letting tests publish into a mismatch.
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await host.StartTestAsync("rabbit declare conflict", TestMethods.Placeholder));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain(exchange));
                Assert.That(exception.Message, Does.Contain("refused the declaration"));
            });
            await host.StopAsync();
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task PublishAndAwait_ShouldRoundTripOnADirectExchange()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Direct);

        try
        {
            // The tap binds the destination routing key as well as "#", which is what makes a direct
            // exchange round trip while a topic exchange stays catch-all.
            var builder = new ProtoHostBuilder();
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Messaging:Destinations:0"] = exchange
                }));
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
                options.ConnectionString = connectionString));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit direct round trip", TestMethods.Placeholder);
            var messages = context.Messaging();

            await messages.PublishAsync(exchange, "{\"id\":1}", contentType: "application/json");
            var received = await messages.AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(15));

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            Assert.That(received.Payload, Is.EqualTo("{\"id\":1}"));
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task PublishAndAwait_ShouldRoundTripByRoutingKeyOnATopicExchange()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Topic);

        try
        {
            // The tap's "#" binding catches every routing key on a topic exchange, so the act-then-await
            // flow works and the routing key selects the delivery the await matches.
            var builder = new ProtoHostBuilder();
            builder.AddMessaging(messaging => messaging
                .UseRabbitMq(options => options.ConnectionString = connectionString)
                .Tap(exchange));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit routing key topic", TestMethods.Placeholder);
            var messages = context.Messaging();

            await messages.PublishAsync(exchange, "invoice.paid", "{\"id\":1}", contentType: "application/json");
            await messages.PublishAsync(exchange, "invoice.shipped", "{\"id\":2}", contentType: "application/json");
            var received = await messages.AwaitAsync(
                exchange,
                "invoice.paid",
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(15));

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(received.Destination, Is.EqualTo(exchange));
                Assert.That(
                    received.RoutingKey,
                    Is.EqualTo("invoice.paid"),
                    "the delivery carries the routing key it was published under");
                Assert.That(received.Payload, Is.EqualTo("{\"id\":1}"));
            }
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task AwaitByRoutingKey_OnADirectExchange_ShouldBindTheKeyAndNameATimeout()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Direct);

        try
        {
            var builder = new ProtoHostBuilder();
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
                options.ConnectionString = connectionString));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit routing key direct", TestMethods.Placeholder);
            var messages = context.Messaging();

            // A direct exchange matches the routing key exactly, so the timeout below proves the
            // negative path and binds the key on the tap; the same await then receives the message.
            var missing = Assert.ThrowsAsync<TimeoutException>(async () =>
                await messages.AwaitAsync(exchange, "session.ended", _ => true, TimeSpan.FromMilliseconds(200)));
            await messages.PublishAsync(exchange, "session.ended", "{\"id\":1}");
            var received = await messages.AwaitAsync(
                exchange,
                "session.ended",
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(15));

            await host.CompleteTestAsync(ProtoTestResult.Failed(missing!));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(missing!.Message, Does.Contain(exchange));
                Assert.That(missing.Message, Does.Contain("session.ended"), "the timeout names the routing key it asked for");
                Assert.That(received.RoutingKey, Is.EqualTo("session.ended"));
                Assert.That(received.Payload, Is.EqualTo("{\"id\":1}"));
            }
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task PublishAndAwait_ShouldRoundTripOnAHeadersExchange()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Headers);

        try
        {
            // A headers exchange ignores the routing key; the tap's argument-less bindings match every
            // message, so the same act-then-await flow works without knowing the application's headers.
            var builder = new ProtoHostBuilder();
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Messaging:Destinations:0"] = exchange
                }));
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
                options.ConnectionString = connectionString));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit headers round trip", TestMethods.Placeholder);
            var messages = context.Messaging();

            await messages.PublishAsync(
                exchange,
                "{\"id\":1}",
                new Dictionary<string, string?> { ["event"] = "invoice.paid" },
                "application/json");
            var received = await messages.AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(15));

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            Assert.That(received.Headers!["event"], Is.EqualTo("invoice.paid"));
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task TwoConsumersAwaitingTheSameDestination_ShouldEachGetTheirOwnMessage()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Fanout);

        try
        {
            var builder = new ProtoHostBuilder();
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
                options.ConnectionString = connectionString));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit consumer isolation", TestMethods.Placeholder);
            var broker = context.Service<IProtoMessageBroker>();
            var messages = context.Messaging();

            // Two tests, simulated by two consumers created in order: each has its own queue, so the
            // second never sees the first's message even though both await the same exchange.
            var first = await broker.CreateConsumerAsync();
            context.RegisterResource("messaging:consumer:first", "consumer", "First consumer", _ => first.DisposeAsync());
            await first.PrepareAsync([exchange]);
            await messages.PublishAsync(exchange, "{\"id\":1}");

            var second = await broker.CreateConsumerAsync();
            context.RegisterResource("messaging:consumer:second", "consumer", "Second consumer", _ => second.DisposeAsync());
            await second.PrepareAsync([exchange]);
            await messages.PublishAsync(exchange, "{\"id\":2}");

            var firstOwn = await first.AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(15));
            var secondOwn = await second.AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":2}",
                TimeSpan.FromSeconds(15));
            var stolen = Assert.ThrowsAsync<TimeoutException>(async () =>
                await second.AwaitAsync(
                    exchange,
                    message => message.Payload == "{\"id\":1}",
                    TimeSpan.FromSeconds(1)));

            await host.CompleteTestAsync(ProtoTestResult.Failed(stolen!));
            Assert.Multiple(() =>
            {
                Assert.That(firstOwn.Payload, Is.EqualTo("{\"id\":1}"));
                Assert.That(secondOwn.Payload, Is.EqualTo("{\"id\":2}"));
                Assert.That(stolen!.Message, Does.Contain(exchange));
            });
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task Prepare_ShouldReportAMissingExchange()
    {
        var connectionString = RequireBroker();

        var builder = new ProtoHostBuilder();
        builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
            options.ConnectionString = connectionString));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rabbit missing exchange", TestMethods.Placeholder);
        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        var consumer = await context.Service<IProtoMessageBroker>().CreateConsumerAsync();
        context.RegisterResource("messaging:consumer:missing", "consumer", "Missing exchange consumer",
            _ => consumer.DisposeAsync());

        var missing = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await consumer.PrepareAsync([exchange]));

        await host.CompleteTestAsync(ProtoTestResult.Failed(missing!));
        Assert.Multiple(() =>
        {
            Assert.That(missing!.Message, Does.Contain(exchange));
            Assert.That(missing.Message, Does.Contain("does not exist"));
        });
    }

    [Test]
    public async Task Tap_WhenADestinationCannotBeDeclared_ShouldFailOnlyTheTestsThatAwaitIt()
    {
        var connectionString = RequireBroker();

        var declared = $"prototest.tests.{Guid.NewGuid():N}";
        var missing = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, declared, ExchangeType.Fanout);

        try
        {
            var builder = new ProtoHostBuilder();
            builder.AddMessaging(messaging => messaging
                .UseRabbitMq(options => options.ConnectionString = connectionString)
                .Tap(declared, missing));
            await using var host = builder.Build();
            await host.StartAsync();

            // The declared destination's tap binds; the missing one fails during setup of every test,
            // but only the test that awaits it may fail. A class-wide setup failure would break here.
            var first = await host.StartTestAsync("rabbit declared tap", TestMethods.Placeholder);
            await first.Messaging().PublishAsync(declared, "{\"id\":1}");
            var received = await first.Messaging().AwaitAsync(
                declared,
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(15));
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            Assert.That(received.Payload, Is.EqualTo("{\"id\":1}"));

            // The test that awaits the destination whose exchange is missing fails naming it, with the
            // adapter's declaration error rather than a bare timeout.
            var second = await host.StartTestAsync("rabbit missing tap", TestMethods.Placeholder);
            var failure = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await second.Messaging().AwaitAsync(missing, _ => true, TimeSpan.FromSeconds(1)));

            await host.CompleteTestAsync(ProtoTestResult.Failed(failure!));
            Assert.Multiple(() =>
            {
                Assert.That(failure!.Message, Does.Contain(missing));
                Assert.That(failure.Message, Does.Contain("does not exist"));
            });
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, declared);
        }
    }

    [Test]
    public async Task ConcurrentAwaitsOnOneDestination_ShouldNotLoseOrStealMessages()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Fanout);

        try
        {
            var builder = new ProtoHostBuilder();
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
                options.ConnectionString = connectionString));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit concurrent awaits", TestMethods.Placeholder);
            var broker = context.Service<IProtoMessageBroker>();

            // The same body the in-memory suite runs: the strict waiter starts first and the delivery
            // only the lenient waiter accepts is published first. A discard-on-mismatch adapter loses
            // it here; a serialized, buffering one hands each waiter its own message.
            await MessagingConcurrencyContract.ConcurrentAwaitsOnOneDestination_ShouldNotLoseOrStealMessages(
                broker,
                exchange);

            await host.CompleteTestAsync(ProtoTestResult.Passed);
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task Await_ShouldTimeOutWhileNonMatchingMessagesKeepArriving()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Fanout);

        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Messaging:Destinations:0"] = exchange
                }));
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
            {
                options.ConnectionString = connectionString;

            }));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit non matching stream", TestMethods.Placeholder);
            var messages = context.Messaging();

            // A backlog that would take seconds to drain at one slow predicate per message: the deadline
            // must win instead of the loop spinning until the queue finally empties.
            for (var index = 0; index < 200; index++)
            {
                await messages.PublishAsync(exchange, $"{{\"id\":{index}}}");
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var timeout = Assert.ThrowsAsync<TimeoutException>(async () =>
                await messages.AwaitAsync(
                    exchange,
                    message =>
                    {
                        // A deliberate timing probe: the predicate must outlast the poll
                        // interval so the wait is still pending when its timeout elapses.
                        Thread.Sleep(20);
                        return message.Payload == "never";
                    },
                    TimeSpan.FromMilliseconds(300)));
            stopwatch.Stop();

            await host.CompleteTestAsync(ProtoTestResult.Failed(timeout!));
            Assert.Multiple(() =>
            {
                Assert.That(timeout!.Message, Does.Contain(exchange));
                Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)),
                    "the deadline is checked on every iteration, not only when the queue is empty");
            });
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task Publish_AfterBrokerDispose_ShouldThrowObjectDisposedException()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Messaging:RabbitMq:ConnectionString"] = "amqp://127.0.0.1:1/"
            }));
        builder.AddMessaging(messaging => messaging.UseRabbitMq());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rabbit disposed broker", TestMethods.Placeholder);
        var broker = context.Service<IProtoMessageBroker>();
        await ((IAsyncDisposable)broker).DisposeAsync();

        // The release must stick: without a disposed guard the publish silently reopens the run's
        // connection and fails on the unreachable address instead of naming the released broker.
        var exception = Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await context.Messaging().PublishAsync("invoices", "{}"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.ObjectName, Does.Contain("RabbitMqMessageBroker"));
    }

    [Test]
    public async Task Await_AfterDispose_ShouldThrowObjectDisposedException()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Messaging:RabbitMq:ConnectionString"] = "amqp://127.0.0.1:1/"
            }));
        builder.AddMessaging(messaging => messaging.UseRabbitMq());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rabbit disposed consumer", TestMethods.Placeholder);
        var consumer = await context.Service<IProtoMessageBroker>().CreateConsumerAsync();
        await consumer.DisposeAsync();

        var exception = Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await consumer.AwaitAsync("invoices", _ => true, TimeSpan.FromSeconds(1)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.ObjectName, Does.Contain("RabbitMqProtoMessageConsumer"));
    }

    [Test]
    public async Task Await_OnAQueueADeadLetterExchangeFeeds_ShouldReadTheQueueBacklog()
    {
        var connectionString = RequireBroker();

        var suffix = Guid.NewGuid().ToString("N");
        var deadLetterExchange = $"prototest.tests.{suffix}.dlx";
        var deadLetterQueue = $"prototest.tests.{suffix}.dlq";
        var sourceQueue = $"prototest.tests.{suffix}.source";
        var token = Guid.NewGuid().ToString("N");

        try
        {
            // Arrange the product-shaped dead-letter path on a raw client: a source queue whose
            // x-dead-letter-exchange dead-letters a rejected delivery into the dead-letter exchange,
            // whose binding routes it to the named queue.
            await using (var connection = await ConnectAsync(connectionString))
            {
                await using var channel = await connection.CreateChannelAsync();
                await channel.ExchangeDeclareAsync(deadLetterExchange, ExchangeType.Fanout);
                await channel.QueueDeclareAsync(deadLetterQueue, durable: false, exclusive: false, autoDelete: false);
                await channel.QueueBindAsync(deadLetterQueue, deadLetterExchange, routingKey: string.Empty);
                await channel.QueueDeclareAsync(
                    sourceQueue,
                    durable: false,
                    exclusive: false,
                    autoDelete: false,
                    arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = deadLetterExchange });
                await channel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: sourceQueue,
                    body: Encoding.UTF8.GetBytes($$"""{"token":"{{token}}"}"""));
                var delivery = await channel.BasicGetAsync(sourceQueue, autoAck: false);
                Assert.That(delivery, Is.Not.Null, "the poison was published to the source queue");
                await channel.BasicNackAsync(delivery!.DeliveryTag, multiple: false, requeue: false);
            }

            // The dead letter reaches the queue before the test starts: a queue await reads what the
            // queue already holds, where an exchange tap only sees messages published after it binds.
            var builder = new ProtoHostBuilder();
            builder.AddMessaging(messaging => messaging
                .Tap(ProtoDestination.Queue(deadLetterQueue))
                .UseRabbitMq(options => options.ConnectionString = connectionString));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit queue dead letter", TestMethods.Placeholder);
            var messages = context.Messaging();

            var received = await messages.AwaitAsync(
                ProtoDestination.Queue(deadLetterQueue),
                message => message.Payload!.Contains(token),
                TimeSpan.FromSeconds(15));

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    received.Destination,
                    Is.EqualTo(ProtoDestination.Queue(deadLetterQueue)),
                    "the delivery carries the queue destination the await named");
                Assert.That(
                    received.RoutingKey,
                    Is.EqualTo(sourceQueue),
                    "the dead-lettered copy keeps the routing key it was published under");
                Assert.That(received.Payload, Does.Contain(token));
            }
        }
        finally
        {
            await DeleteQueueAsync(connectionString, deadLetterQueue, sourceQueue);
            await DeleteExchangeAsync(connectionString, deadLetterExchange);
        }
    }

    [Test]
    public async Task Await_OnAQueueThatDoesNotExist_ShouldFailNamingTheQueue()
    {
        var connectionString = RequireBroker();

        var queue = $"prototest.tests.{Guid.NewGuid():N}.missing";
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
            options.ConnectionString = connectionString));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rabbit missing queue", TestMethods.Placeholder);

        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Messaging().AwaitAsync(
                ProtoDestination.Queue(queue),
                _ => true,
                TimeSpan.FromSeconds(1)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(error!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain(queue));
            Assert.That(error.Message, Does.Contain("does not exist"));
        }
    }

    private static string RequireBroker()
    {
        var connectionString = ResolveBroker();
        if (connectionString is null)
        {
            Assert.Ignore(
                "No RabbitMQ broker is available: set ProtoTest__Messaging__RabbitMq__ConnectionString " +
                "or start a container runtime. " + Container.Value.Error);
        }

        return connectionString;
    }

    private static string? ResolveBroker()
    {
        var configured = Environment.GetEnvironmentVariable("ProtoTest__Messaging__RabbitMq__ConnectionString");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Container.Value.Broker?.ConnectionString;
    }

    /// <summary>Declares a throwaway exchange for one test, on a connection of its own.</summary>
    private static async Task DeclareExchangeAsync(
        string connectionString,
        string exchange,
        string type,
        bool durable = false,
        bool autoDelete = true)
    {
        await using var connection = await ConnectAsync(connectionString);
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(exchange, type, durable: durable, autoDelete: autoDelete);
    }

    /// <summary>Asserts the exchange exists, with a passive declaration that fails the test on a 404.</summary>
    private static async Task AssertExchangeExistsAsync(string connectionString, string exchange)
    {
        await using var connection = await ConnectAsync(connectionString);
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(
            exchange, ExchangeType.Fanout, durable: true, autoDelete: false, passive: true);
    }

    /// <summary>Deletes the throwaway exchange, so a rerun starts from the same broker state.</summary>
    private static async Task DeleteExchangeAsync(string connectionString, string exchange)
    {
        await using var connection = await ConnectAsync(connectionString);
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeleteAsync(exchange);
    }

    /// <summary>Deletes throwaway queues, including their messages, so a rerun starts clean.</summary>
    private static async Task DeleteQueueAsync(string connectionString, params string[] queues)
    {
        await using var connection = await ConnectAsync(connectionString);
        await using var channel = await connection.CreateChannelAsync();
        foreach (var queue in queues)
        {
            await channel.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: false);
        }
    }

    private static async Task<IConnection> ConnectAsync(string connectionString)
        => await new ConnectionFactory { Uri = new Uri(connectionString) }
            .CreateConnectionAsync("ProtoTest.Messaging.RabbitMq.Tests");
}
