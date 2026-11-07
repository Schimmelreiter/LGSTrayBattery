using LGSTrayCore;
using LGSTrayCore.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using System;
using LGSTrayPrimitives.IPC;
using System.Globalization;
using System.IO;
using System.Threading;
using LGSTrayPrimitives;
using Tommy.Extensions.Configuration;

using static LGSTrayUI.AppExtensions;
using System.Threading.Tasks;

namespace LGSTrayUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static Mutex? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CrashHandler);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Write("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        // A second instance cannot open the IPC pipe and would fail during startup.
        _singleInstance = new Mutex(true, @"Local\LGSTrayUI", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("LGSTray is already running.", "LGSTray", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            await RunHost();
        }
        catch (Exception ex)
        {
            // Report the real startup error instead of a follow-up error while cleaning up.
            var file = WriteCrashLog(ex);
            MessageBox.Show(
                $"LGSTray could not start:\n\n{ex.GetBaseException().Message}\n\nDetails: {file}",
                "LGSTray - Startup Error", MessageBoxButton.OK, MessageBoxImage.Error
            );
        }

        Dispatcher.InvokeShutdown();
    }

    private async Task RunHost()
    {
        EnableEfficiencyMode();

        var builder = Host.CreateEmptyApplicationBuilder(null);
        await LoadAppSettings(builder.Configuration);

        builder.Services.Configure<AppSettings>(builder.Configuration);
        builder.Services.AddLGSMessagePipe(true);
        builder.Services.AddSingleton<UserSettingsWrapper>();

        builder.Services.AddSingleton<LogiDeviceIconFactory>();
        builder.Services.AddSingleton<LogiDeviceViewModelFactory>();

        builder.Services.AddWebserver(builder.Configuration);
        builder.Services.AddMqtt(builder.Configuration);

        builder.Services.AddIDeviceManager<LGSTrayHIDManager>(builder.Configuration);
        builder.Services.AddIDeviceManager<GHubManager>(builder.Configuration);
        builder.Services.AddSingleton<ILogiDeviceCollection, LogiDeviceCollection>();

        builder.Services.AddSingleton<MainTaskbarIconWrapper>();
        builder.Services.AddHostedService<NotifyIconViewModel>();

        var host = builder.Build();
        await host.RunAsync();
    }

    static async Task LoadAppSettings(Microsoft.Extensions.Configuration.ConfigurationManager config)
    {
        string settingsFile = AppPaths.EnsureSettingsFile();
        try
        {
            config.AddTomlFile(settingsFile);
        }
        catch (Exception ex)
        {
            if (ex is FileNotFoundException || ex is InvalidDataException)
            {
                var msgBoxRet = MessageBox.Show(
                    "Failed to read settings, do you want reset to default?", 
                    "LGSTray - Settings Load Error", 
                    MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.No
                );

                if (msgBoxRet == MessageBoxResult.Yes)
                {
                    await File.WriteAllBytesAsync(
                        settingsFile,
                        LGSTrayUI.Properties.Resources.defaultAppsettings
                    );
                }

                config.AddTomlFile(settingsFile);
            }
            else
            {
                throw;
            }
        }
    }

    private void CrashHandler(object sender, UnhandledExceptionEventArgs args)
    {
        WriteCrashLog((Exception)args.ExceptionObject);
    }

    private static string WriteCrashLog(Exception e)
    {
        long unixTime = DateTimeOffset.Now.ToUnixTimeSeconds();
        string file = Path.Combine(AppPaths.DataDirectory, $"crashlog_{unixTime}.log");

        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            using StreamWriter writer = new(file, false);
            writer.WriteLine(e.ToString());
        }
        catch
        {
            // ignore, the log below is the fallback
        }
        AppLog.Write("Crash", e);

        return file;
    }
}