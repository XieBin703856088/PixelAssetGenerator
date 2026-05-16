using System;
using Microsoft.Extensions.DependencyInjection;
using PixelAssetGenerator.Services.Localization;

namespace PixelAssetGenerator.Services
{
    /// <summary>
    /// Application-wide service provider for dependency injection.
    /// Transitional: gradually replaces static singletons with injected services.
    /// </summary>
    public static class ServiceLocator
    {
        private static IServiceProvider? _provider;
        private static readonly object Lock = new();

        /// <summary>Ensures the service provider is built. Idempotent.</summary>
        public static void EnsureInitialized()
        {
            if (_provider != null) return;
            lock (Lock)
            {
                if (_provider != null) return;
                _provider = BuildProvider();
            }
        }

        /// <summary>Resolves a service of type T from the DI container.</summary>
        public static T GetService<T>() where T : notnull
        {
            EnsureInitialized();
            return _provider!.GetRequiredService<T>();
        }

        /// <summary>Attempts to resolve a service of type T, returning null if not registered.</summary>
        public static T? TryGetService<T>() where T : class
        {
            EnsureInitialized();
            return _provider!.GetService<T>();
        }

        private static ServiceProvider BuildProvider()
        {
            var services = new ServiceCollection();

            // Register core services (singleton by default for WPF app)
            services.AddSingleton<ILocalizationService>(_ => LocalizationService.Instance);
            services.AddSingleton<LocalizationService>(_ => LocalizationService.Instance);

            // Console service
            services.AddSingleton<IConsoleService, ConsoleService>();

            return services.BuildServiceProvider();
        }
    }
}
