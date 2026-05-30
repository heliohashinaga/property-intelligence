using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PropertyIntelligence.Dispatcher.DomainEvents;

/// <summary>
/// Background service that drains <see cref="DomainEventChannel"/> and
/// dispatches each event to all registered <see cref="INotificationHandler{T}"/>
/// implementations in a fresh DI scope.
///
/// Design decisions:
/// - Uses a scoped DI scope per event so handlers can resolve scoped services (e.g. DbContext).
/// - Never rethrows — a handler failure is logged and the processor continues.
///   This prevents one bad handler from poisoning the entire queue.
/// - Channel is unbounded; backpressure must be handled by the producer if needed.
/// </summary>
public sealed class DomainEventProcessor(
    DomainEventChannel                    channel,
    IServiceProvider                      sp,
    ILogger<DomainEventProcessor>         logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var evt in channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                using var scope     = sp.CreateScope();
                var handlerType     = typeof(INotificationHandler<>).MakeGenericType(evt.GetType());
                var handlers        = scope.ServiceProvider.GetServices(handlerType);

                foreach (var handler in handlers)
                {
                    if (handler is null) continue;
                    try
                    {
                        // Dynamic dispatch — reflection only at runtime, acceptable for
                        // background processing (not on the hot HTTP path).
                        var task = (ValueTask)handlerType
                            .GetMethod(nameof(INotificationHandler<IDomainEvent>.Handle))!
                            .Invoke(handler, [evt, ct])!;
                        await task;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex,
                            "DomainEventProcessor: handler {Handler} failed for event {EventType} {EventId}",
                            handler.GetType().Name, evt.GetType().Name, evt.Id);
                        // Never rethrow — one handler failure must not stop the processor.
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "DomainEventProcessor: unhandled error processing event {EventType}",
                    evt.GetType().Name);
            }
        }
    }
}
