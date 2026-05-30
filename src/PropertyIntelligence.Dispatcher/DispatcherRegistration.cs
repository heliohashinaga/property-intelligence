using System.Collections.Frozen;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace PropertyIntelligence.Dispatcher;

/// <summary>
/// DI registration extensions for the custom CQRS dispatcher.
/// Reflection is used once at startup to build the FrozenDictionary;
/// the hot dispatch path never touches reflection.
/// </summary>
public static class DispatcherRegistration
{
    /// <summary>
    /// Scans <paramref name="assembly"/> for all <see cref="IRequestHandler{TRequest,TResponse}"/>
    /// and <see cref="INotificationHandler{TNotification}"/> implementations,
    /// builds a <see cref="DispatcherRegistry"/>, and registers
    /// <see cref="ISender"/> and <see cref="IPublisher"/>.
    /// </summary>
    public static IServiceCollection AddDispatcher(
        this IServiceCollection services,
        Assembly                assembly)
    {
        var requestWrappers      = new Dictionary<Type, RequestHandlerBase>();
        var notificationHandlers = new Dictionary<Type, List<NotificationHandlerBase>>();

        foreach (var type in assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var iface in type.GetInterfaces())
            {
                if (!iface.IsGenericType) continue;
                var def = iface.GetGenericTypeDefinition();

                // ── IRequestHandler<TRequest, TResponse> ──────────────────────
                if (def == typeof(IRequestHandler<,>))
                {
                    var args        = iface.GetGenericArguments(); // [TRequest, TResponse]
                    var wrapperType = typeof(RequestHandlerWrapper<,>).MakeGenericType(args);
                    requestWrappers[args[0]] =
                        (RequestHandlerBase)Activator.CreateInstance(wrapperType)!;
                    services.AddScoped(iface, type);
                }

                // ── INotificationHandler<TNotification> ───────────────────────
                if (def == typeof(INotificationHandler<>))
                {
                    var notifType   = iface.GetGenericArguments()[0];
                    var wrapperType = typeof(NotificationHandlerWrapper<>).MakeGenericType(notifType);
                    var wrapper     = (NotificationHandlerBase)Activator.CreateInstance(wrapperType)!;
                    if (!notificationHandlers.TryGetValue(notifType, out var list))
                        notificationHandlers[notifType] = list = [];
                    list.Add(wrapper);
                    services.AddScoped(iface, type);
                }
            }
        }

        var registry = new DispatcherRegistry(
            requestWrappers.ToFrozenDictionary(),
            notificationHandlers.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<NotificationHandlerBase>)kvp.Value.AsReadOnly()));

        services.AddSingleton(registry);
        services.AddScoped<Dispatcher>();
        services.AddScoped<ISender>(sp => sp.GetRequiredService<Dispatcher>());
        services.AddScoped<IPublisher>(sp => sp.GetRequiredService<Dispatcher>());

        return services;
    }

    /// <summary>
    /// Registers an open-generic pipeline behavior.
    /// Behaviors run in registration order, outermost first.
    /// </summary>
    public static IServiceCollection AddPipelineBehavior(
        this IServiceCollection services,
        Type                    openGenericBehaviorType)
    {
        services.AddScoped(typeof(IPipelineBehavior<,>), openGenericBehaviorType);
        return services;
    }
}
