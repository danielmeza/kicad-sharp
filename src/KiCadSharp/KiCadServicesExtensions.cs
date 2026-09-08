using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KiCadSharp
{
    public static class KiCadServicesExtensions
    {
        public static IServiceCollection AddKiCad(this IServiceCollection services, string clientName, Action<KiCadClientSettings>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(clientName);

            services.AddKeyedSingleton(clientName, (provider, key) =>
            {
                var settings = provider.GetRequiredService<IOptionsFactory<KiCadClientSettings>>().Create(clientName);
                var logger = provider.GetRequiredService<ILogger<KiCadIPCClient>>();
                return new KiCadIPCClient(settings, logger);
            });

            var optioinsBuilder = services.AddOptions<KiCadClientSettings>(clientName)
                 .Configure((settings) =>
                 {
                     settings.ClientName = clientName;

                     // GetDefaultSocketPath, not GetApiSocket: the environment variable is only set
                     // for a plugin KiCad launched itself. Everything else -- a test, a CLI, a
                     // service talking to a KiCad someone started by hand -- has to fall back to the
                     // platform default, which is where KiCad actually puts the socket.
                     settings.PipeName = KiCadEnvironment.GetDefaultSocketPath();
                     settings.Token = KiCadEnvironment.GetApiToken();
                 });

            if (configure != null)
            {
                optioinsBuilder.PostConfigure(configure);
            }

            services.AddKeyedSingleton(clientName, (provider, key) => new KiCad(provider.GetRequiredKeyedService<KiCadIPCClient>(key)));

            // There used to be an IAPIFactory<INngMsg> singleton here, built from an
            // nng.NET NngLoadContext rooted at this assembly's directory: that binding loaded its
            // own managed assembly and the native library out of runtimes/<rid>/ by hand, and every
            // client had to be handed the factory. nng is now called directly (KiCadSharp.Interop),
            // the native library is found by KiCadSharp.Interop.NngLibraryResolver, and there is
            // nothing to register.

            services.AddSingleton<IKiCadFactory, KiCadFactory>();

            return services;
        }

        public static IServiceCollection AddKiCad(this IServiceCollection services, Action<KiCadClientSettings> configure)
        {
            return services.AddKiCad(KiCadClientSettings.DefaultClientName, configure);
        }
    }

    internal class KiCadFactory : IKiCadFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public KiCadFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public KiCad Create(string? clientName = null)
        {
            return _serviceProvider.GetRequiredKeyedService<KiCad>(string.IsNullOrWhiteSpace(clientName) ? KiCadClientSettings.DefaultClientName : clientName);
        }
    }
}
