namespace PropertyIntelligence.Dispatcher;

/// <summary>
/// Handles a command or query of type <typeparamref name="TRequest"/>.
/// Returns <see cref="ValueTask{TResponse}"/> to avoid heap allocation when
/// the handler completes synchronously (cache hits, trivial commands).
/// </summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> Handle(TRequest request, CancellationToken ct);
}
