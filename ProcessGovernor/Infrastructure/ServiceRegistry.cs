using System.Reflection;

namespace ProcessGovernor.Infrastructure;

public enum ServiceLifetime
{
    Singleton,
    Transient
}

public sealed class ServiceRegistry
{
    private readonly Dictionary<Type, ServiceDescriptor> _descriptors = new();

    public ServiceRegistry AddSingleton<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        _descriptors[typeof(TService)] = ServiceDescriptor.Create(typeof(TService), typeof(TImplementation), ServiceLifetime.Singleton);
        return this;
    }

    public ServiceRegistry AddSingleton<TService>(Func<AppServiceProvider, TService> factory)
        where TService : class
    {
        _descriptors[typeof(TService)] = ServiceDescriptor.Create(typeof(TService), provider => factory(provider), ServiceLifetime.Singleton);
        return this;
    }

    public ServiceRegistry AddSingleton<TService>()
        where TService : class
    {
        _descriptors[typeof(TService)] = ServiceDescriptor.Create(typeof(TService), typeof(TService), ServiceLifetime.Singleton);
        return this;
    }

    public ServiceRegistry AddTransient<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        _descriptors[typeof(TService)] = ServiceDescriptor.Create(typeof(TService), typeof(TImplementation), ServiceLifetime.Transient);
        return this;
    }

    public ServiceRegistry AddTransient<TService>()
        where TService : class
    {
        _descriptors[typeof(TService)] = ServiceDescriptor.Create(typeof(TService), typeof(TService), ServiceLifetime.Transient);
        return this;
    }

    public AppServiceProvider Build() => new(_descriptors);
}

public sealed class AppServiceProvider : IServiceProvider, IDisposable
{
    private readonly Dictionary<Type, ServiceDescriptor> _descriptors;
    private readonly Dictionary<Type, object> _singletons = new();
    private bool _disposed;

    internal AppServiceProvider(Dictionary<Type, ServiceDescriptor> descriptors)
    {
        _descriptors = new Dictionary<Type, ServiceDescriptor>(descriptors);
        _descriptors[typeof(AppServiceProvider)] = ServiceDescriptor.Create(typeof(AppServiceProvider), _ => this, ServiceLifetime.Singleton);
        _descriptors[typeof(IServiceProvider)] = ServiceDescriptor.Create(typeof(IServiceProvider), _ => this, ServiceLifetime.Singleton);
    }

    public object? GetService(Type serviceType)
    {
        ThrowIfDisposed();

        if (!_descriptors.TryGetValue(serviceType, out var descriptor))
        {
            if (serviceType.IsClass && !serviceType.IsAbstract)
            {
                return CreateInstance(serviceType);
            }

            return null;
        }

        if (descriptor.Lifetime == ServiceLifetime.Transient)
        {
            return CreateDescriptorInstance(descriptor);
        }

        if (!_singletons.TryGetValue(serviceType, out var instance))
        {
            instance = CreateDescriptorInstance(descriptor);
            _singletons[serviceType] = instance;
        }

        return instance;
    }

    public T GetRequiredService<T>()
        where T : class
    {
        return (T)(GetService(typeof(T)) ?? throw new InvalidOperationException($"Service {typeof(T).Name} is not registered."));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var instance in _singletons.Values.OfType<IDisposable>())
        {
            instance.Dispose();
        }

        _singletons.Clear();
        _disposed = true;
    }

    private object CreateDescriptorInstance(ServiceDescriptor descriptor)
    {
        if (descriptor.Factory is not null)
        {
            return descriptor.Factory(this);
        }

        if (descriptor.ImplementationType is null)
        {
            throw new InvalidOperationException($"Service {descriptor.ServiceType.Name} has no implementation.");
        }

        return CreateInstance(descriptor.ImplementationType);
    }

    private object CreateInstance(Type implementationType)
    {
        var constructor = implementationType
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(static c => c.GetParameters().Length)
            .FirstOrDefault();

        if (constructor is null)
        {
            throw new InvalidOperationException($"Type {implementationType.Name} has no public constructor.");
        }

        var arguments = constructor
            .GetParameters()
            .Select(parameter => GetService(parameter.ParameterType)
                ?? throw new InvalidOperationException($"Unable to resolve {parameter.ParameterType.Name} for {implementationType.Name}."))
            .ToArray();

        return Activator.CreateInstance(implementationType, arguments)
            ?? throw new InvalidOperationException($"Unable to create {implementationType.Name}.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AppServiceProvider));
        }
    }
}

internal sealed record ServiceDescriptor(
    Type ServiceType,
    Type? ImplementationType,
    Func<AppServiceProvider, object>? Factory,
    ServiceLifetime Lifetime)
{
    public static ServiceDescriptor Create(Type serviceType, Type implementationType, ServiceLifetime lifetime)
        => new(serviceType, implementationType, null, lifetime);

    public static ServiceDescriptor Create(Type serviceType, Func<AppServiceProvider, object> factory, ServiceLifetime lifetime)
        => new(serviceType, null, factory, lifetime);
}
