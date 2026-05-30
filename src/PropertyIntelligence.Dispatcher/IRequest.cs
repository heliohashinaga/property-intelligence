namespace PropertyIntelligence.Dispatcher;

/// <summary>Marker for commands/queries that return a typed response.</summary>
public interface IRequest<out TResponse>;

/// <summary>Marker for commands that return no value.</summary>
public interface IRequest : IRequest<Unit>;

/// <summary>Void-equivalent — avoids boxing for commands with no return value.</summary>
public readonly struct Unit
{
    public static readonly Unit Value = default;
}
