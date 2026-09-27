using System.Runtime.CompilerServices;

// The shared await machinery is internal, and the broker adapters live in their own assemblies: the
// internals are what the RabbitMQ and MassTransit consumers await over, and the focused tests pin the
// semantics.
[assembly: InternalsVisibleTo("ProtoTest.Messaging.RabbitMq")]
[assembly: InternalsVisibleTo("ProtoTest.Messaging.MassTransit")]
[assembly: InternalsVisibleTo("ProtoTest.Messaging.Tests")]
