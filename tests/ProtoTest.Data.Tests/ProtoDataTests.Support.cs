namespace ProtoTest.Data.Tests;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

public sealed partial class ProtoDataTests
{
    private static ProtoHost CreateHost()
        => new ProtoHostBuilder()
            .AddData(data => data.AddDefaults<TestDefaults>())
            .Build();


    public sealed class TestDefaults : IProtoDataDefaultsModule
    {
        public void Configure(ProtoDataConfiguration data)
        {
            data.Values.Use<InvoiceId>(context => new InvoiceId(context.NextGuid()));
            data.For<Invoice>().Default(x => x.Currency, "EUR");
        }
    }

    public sealed class Invoice
    {
        public InvoiceId Id { get; init; }
        public string Description { get; init; } = null!;
        public string Currency { get; init; } = null!;
        public decimal Total { get; init; }
        public IReadOnlyList<string> Lines { get; init; } = null!;
        public string? Note { get; init; }
    }

    public readonly record struct InvoiceId(Guid Value);
    public sealed record CreateInvoice(InvoiceId Id, decimal Total, string Reference);
    public sealed record SemanticState(InvoiceStatus Status);
    public sealed record RequiresNumber(decimal Amount);
    public enum InvoiceStatus { Draft, Overdue }

    public sealed class DomainInvoice
    {
        private DomainInvoice(InvoiceId id, decimal total)
        {
            Id = id;
            Total = total;
        }

        public InvoiceId Id { get; }
        public decimal Total { get; }

        public static DomainInvoice Create(InvoiceId id, decimal total)
            => total <= 0
                ? throw new ArgumentOutOfRangeException(nameof(total))
                : new DomainInvoice(id, total);
    }

    public sealed class Credentials
    {
        public string UserName { get; init; } = null!;
        public string Secret { get; init; } = null!;
    }

    public sealed record Login(Password Password, string UserName);
    public sealed record Password(string Value);

    public sealed record Contact(EmailAddress Email);
    public sealed record EmailAddress(string Value);

    public sealed class TestEmailResolver : IProtoDataValueResolver
    {
        public bool TryResolve(ProtoDataValueContext context, out ProtoDataResolvedValue value)
        {
            if (context.ValueType == typeof(EmailAddress))
            {
                value = new ProtoDataResolvedValue(
                    new EmailAddress($"{context.MemberName}-{context.ObjectSequence:D4}@example.test"),
                    nameof(TestEmailResolver));
                return true;
            }

            value = null!;
            return false;
        }
    }

    public sealed class StoredInvoiceProvisioner : IProtoDataProvisioner<StoredInvoice>
    {
        private readonly CleanupProbe _cleanup;

        public StoredInvoiceProvisioner(CleanupProbe cleanup)
        {
            _cleanup = cleanup;
        }

        public ValueTask<ProtoDataProvisioningResult<StoredInvoice>> CreateAsync(
            StoredInvoice value,
            ProtoDataProvisioningContext context,
            CancellationToken cancellationToken)
        {
            var stored = value with { Reference = $"stored:{value.Reference}" };
            return ValueTask.FromResult(new ProtoDataProvisioningResult<StoredInvoice>(
                stored,
                stored.Id.ToString(),
                new CleanupOwnership(_cleanup)));
        }
    }

    public sealed class SharedIdentityProvisioner : IProtoDataProvisioner<StoredInvoice>
    {
        public ValueTask<ProtoDataProvisioningResult<StoredInvoice>> CreateAsync(
            StoredInvoice value,
            ProtoDataProvisioningContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ProtoDataProvisioningResult<StoredInvoice>(value, "shared"));
    }

    public sealed class CleanupProbe
    {
        public bool Disposed { get; set; }

        public List<string> Log { get; } = [];
    }

    private sealed class CleanupOwnership : IAsyncDisposable
    {
        private readonly CleanupProbe _probe;

        public CleanupOwnership(CleanupProbe probe)
        {
            _probe = probe;
        }

        public ValueTask DisposeAsync()
        {
            _probe.Disposed = true;
            _probe.Log.Add("data.cleanup");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LoggingClient(List<string> log) : IDisposable
    {
        public void Dispose() => log.Add("client.dispose");
    }

    private sealed class CapabilityProbe
    {
        public int DataCapabilityCount { get; set; }
    }

    private sealed class CapabilityCountingHook : IProtoTestHook
    {
        public CapabilityCountingHook(
            IEnumerable<ProtoCapabilityDescriptor> capabilities,
            CapabilityProbe probe)
            => probe.DataCapabilityCount = capabilities.Count(capability => capability.Kind == ProtoCapabilityKinds.Data);

        public int Order => 0;

        public Task BeforeTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

        public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
    }

    private sealed class NoopResolver : IProtoDataValueResolver
    {
        public bool TryResolve(ProtoDataValueContext context, out ProtoDataResolvedValue value)
        {
            value = null!;
            return false;
        }
    }

    public sealed record SecretHolder(object Payload);

    public sealed class CredentialHolder
    {
        public Credentials Credentials { get; init; } = null!;
    }

    public sealed class CyclicHolder
    {
        public CyclicNode Node { get; init; } = null!;
    }

    public sealed class CyclicNode
    {
        public string Secret { get; init; } = null!;

        public CyclicNode? Next { get; set; }
    }

    public abstract class CredentialBase
    {
        public string UserName { get; init; } = null!;
        public string Secret { get; init; } = null!;
    }

    public sealed class DerivedCredentials : CredentialBase
    {
    }

    public interface ISecretMarker
    {
    }

    public abstract class SecretBase
    {
    }

    public sealed class DerivedSecret : SecretBase, ISecretMarker
    {
        public string Code { get; init; } = string.Empty;
    }
}
