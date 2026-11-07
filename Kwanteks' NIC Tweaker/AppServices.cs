using Kwanteks.NicTweaker.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kwanteks.NicTweaker;

public static class AppServices
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStorageService, StorageService>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<IRegistryService, RegistryService>();
        services.AddSingleton<IPowerShellBridge, PowerShellBridge>();
        services.AddSingleton<IAdapterService, AdapterService>();
        services.AddSingleton<IRssService, RssService>();
        services.AddSingleton<INetIpInterfaceService, NetIpInterfaceService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IRestorePointService, RestorePointService>();
        services.AddSingleton<IConfigTransferService, ConfigTransferService>();
        services.AddSingleton<ITweakEngine, TweakEngine>();
        services.AddSingleton<ViewModels.MainViewModel>();
        services.AddTransient<ViewModels.BackupManagerViewModel>();
        services.AddTransient<ViewModels.SettingsViewModel>();
        return services.BuildServiceProvider();
    }
}
