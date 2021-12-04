using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Wabbajack.App.Wpf.Controls;
using Wabbajack.App.Wpf.Interfaces;
using Wabbajack.App.Wpf.Screens;
using Wabbajack.App.Wpf.Util;
using Wabbajack.DTOs;
using Wabbajack.Services.OSIntegrated;
using Wabbajack.App.Wpf.Interfaces;
using Wabbajack.App.Wpf.Messages;
using Wabbajack.App.Wpf.Support;

namespace Wabbajack.App.Wpf
{
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;
        public App()
        {
            var host = Host.CreateDefaultBuilder(Array.Empty<string>())
                .ConfigureLogging(c => { c.ClearProviders(); })
                .ConfigureServices((host, services) => { ConfigureServices(services); })
                .Build();
            _serviceProvider = host.Services;
        }
        
        private void ConfigureServices(IServiceCollection services)
        {
            services.AddAllSingleton<ILoggerProvider, LoggerProvider>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<MainWindowViewModel>();
            
            services.AddAllSingleton<IScreenView, ModeSelectionView>();
            services.AddAllSingleton<IScreenViewModel, ModeSelectionViewModel>();
            
            services.AddAllSingleton<IScreenView, ModListGalleryView>();
            services.AddAllSingleton<IScreenViewModel, ModListGalleryViewModel>();

            services.AddTransient<ModListTileView>();
            services.AddTransient<ModListTileViewModel>();

            services.AddOSIntegrated();
        }


        private void OnStartup(object sender, StartupEventArgs e)
        {
            var mainWindow = _serviceProvider.GetService<MainWindow>()!;
            var mainWindowViewModel = _serviceProvider.GetService<MainWindowViewModel>()!;
            // Avoids a deadlock in DI
            mainWindowViewModel.IndexViewModels(_serviceProvider.GetService<IEnumerable<IScreenViewModel>>()!);
            mainWindow.Show();
            MessageBus.Current.SendMessage(NavigateTo.Create<ModeSelectionViewModel>());
        }
    }
}