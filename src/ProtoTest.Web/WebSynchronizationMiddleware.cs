namespace ProtoTest.Web;

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

internal sealed record WebWaitRegistration(
    Type ConditionType,
    WebWaitTiming Timing,
    IReadOnlySet<WebOperationKind> Operations,
    TimeSpan Timeout,
    TimeSpan PollInterval);

internal sealed class WebSynchronizationMiddleware(
    IEnumerable<WebWaitRegistration> registrations) : IWebOperationMiddleware
{
    private readonly IReadOnlyList<WebWaitRegistration> _registrations = [.. registrations];

    public async ValueTask InvokeAsync(
        WebOperationContext context,
        WebOperationDelegate next,
        CancellationToken cancellationToken = default)
    {
        await WaitAsync(context, WebWaitTiming.Before, cancellationToken);
        await next(context, cancellationToken);
        await WaitAsync(context, WebWaitTiming.After, cancellationToken);
    }

    private async ValueTask WaitAsync(
        WebOperationContext operationContext,
        WebWaitTiming timing,
        CancellationToken cancellationToken)
    {
        foreach (var registration in _registrations.Where(item =>
                     item.Timing == timing && item.Operations.Contains(operationContext.Kind)))
        {
            var condition = (IWebWaitCondition)operationContext.Execution.Services.GetRequiredService(registration.ConditionType);
            using var trace = operationContext.Execution.Trace
                .Operation("web.wait", $"Wait · {condition.Name}", "ProtoTest.Web")
                .With("web.wait.timing", timing.ToString())
                .With("web.wait.condition", condition.GetType().FullName)
                .With("web.wait.timeout", registration.Timeout.ToString())
                .With("web.operation", operationContext.Kind.ToString())
                .Begin();
            var stopwatch = Stopwatch.StartNew();
            string? lastObserved = null;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var observation = await condition.ObserveAsync(
                        new WebWaitContext(operationContext, operationContext.Backend),
                        cancellationToken);
                    lastObserved = observation.LastObserved;
                    if (observation.Satisfied)
                    {
                        trace.SetAttribute("web.wait.last_observed", lastObserved);
                        trace.Succeed();
                        break;
                    }

                    if (stopwatch.Elapsed >= registration.Timeout)
                        throw new WebWaitTimeoutException(
                            $"Wait '{condition.Name}' timed out after {registration.Timeout}. " +
                            $"Last observed: {lastObserved ?? "no observation"}.");

                    var remaining = registration.Timeout - stopwatch.Elapsed;
                    await Task.Delay(
                        remaining < registration.PollInterval ? remaining : registration.PollInterval,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException exception)
            {
                trace.SetAttribute("web.wait.last_observed", lastObserved);
                trace.Cancel(exception);
                throw;
            }
            catch (Exception exception)
            {
                trace.SetAttribute("web.wait.last_observed", lastObserved);
                trace.Fail(exception);
                throw;
            }
        }
    }
}
