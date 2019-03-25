using System;
using System.IO;
using System.Linq;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCoreKit.Domain;
using NetCoreKit.Infrastructure;
using NetCoreKit.Infrastructure.Features;
using NetCoreKit.Infrastructure.Mongo;

namespace NetCoreKit.GrpcTemplate.MongoDb
{
    public static class HostBuilderExtensions
    {
        public static IHost ConfigureDefaultSettings(this HostBuilder hostBuilder,
            string[] args,
            Action<IServiceCollection> preDbWorkHook = null,
            Action<IServiceCollection> moreRegisterAction = null)
        {
            return hostBuilder
                .ConfigureHostConfiguration(configHost =>
                {
                    configHost.SetBasePath(Directory.GetCurrentDirectory());
                    configHost.AddJsonFile("hostsettings.json", optional: true);
                    configHost.AddEnvironmentVariables();
                    configHost.AddCommandLine(args);
                })
                .ConfigureAppConfiguration((hostContext, configApp) =>
                {
                    configApp.AddEnvironmentVariables();
                    configApp.AddJsonFile("appsettings.json", optional: true);
                    configApp.AddJsonFile(
                        $"appsettings.{hostContext.HostingEnvironment.EnvironmentName}.json",
                        optional: true);
                    configApp.AddCommandLine(args);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddLogging();
                    services.AddFeatureToggle();

                    using (var scope = services.BuildServiceProvider().GetService<IServiceScopeFactory>().CreateScope())
                    {
                        var svcProvider = scope.ServiceProvider;
                        var config = svcProvider.GetRequiredService<IConfiguration>();
                        var feature = svcProvider.GetRequiredService<IFeature>();

                        if (feature.IsEnabled("Mongo"))
                        {
                            if (feature.IsEnabled("EfCore"))
                                throw new Exception("Should turn off EfCore settings.");

                            preDbWorkHook?.Invoke(services);
                            services.AddMongoDb();
                        }

                        services.AddSingleton<IDomainEventDispatcher, MemoryDomainEventDispatcher>();

                        Mapper.Initialize(cfg => cfg.AddProfiles(config.LoadFullAssemblies()));
                        services.AddMediatR(config.LoadFullAssemblies().ToArray());

                        moreRegisterAction?.Invoke(services);
                    }
                })
                .ConfigureLogging((hostContext, configLogging) =>
                {
                    configLogging.AddConsole();
                    configLogging.AddDebug();
                })
                .UseConsoleLifetime()
                .Build();
        }
    }
}
