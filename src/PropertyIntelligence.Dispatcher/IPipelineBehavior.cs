namespace PropertyIntelligence.Dispatcher;

/// <summary>Delegate passed to each pipeline behavior as the "next" step.</summary>
public delegate ValueTask<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Cross-cutting behavior that wraps every handler in the dispatch pipeline.
/// Behaviors run outermost-first in registration order.
/// </summary>
public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> Handle(
        TRequest                         request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken                ct);
}
