using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DynamicData;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Wabbajack.App.Wpf.Interfaces;
using Wabbajack.App.Wpf.Messages;
using Wabbajack.App.Wpf.Support;
using Wabbajack.Common;
using Wabbajack.Networking.WabbajackClientApi;
using Wabbajack.Paths;
using Wabbajack.Paths.IO;

namespace Wabbajack.App.Wpf;

public class MainWindowViewModel : ViewModel
{
    private readonly ILogger _logger;
    private readonly Client _wjClient;
    private Dictionary<Type,ViewModel> _screenViewModels = new();

    private List<ViewModel> _history = new();

    public string VersionDisplay { get; set; }
    
    [Reactive]
    public ViewModel ContentViewModel { get; set; }

    public void IndexViewModels(IEnumerable<IScreenViewModel> screens)
    {
        _screenViewModels = screens.ToDictionary(s => s.GetType(), s => (ViewModel) s);
    }

    public MainWindowViewModel(ILogger<MainWindowViewModel> logger, Client wjClient)
    {
        _wjClient = wjClient;
        _logger = logger;
        _logger.LogInformation("Wabbajack Build - {Sha}", ThisAssembly.Git.Sha);
        _logger.LogInformation("Running in {Entry}", KnownFolders.EntryPoint);

        MessageBus.Current.Listen<NavigateTo>()
            .Subscribe(n =>
            {
                if (_screenViewModels.TryGetValue(n.ViewModelType, out var vm))
                {
                    if (n.SaveHistory && ContentViewModel != null) _history.Add(ContentViewModel);
                    ContentViewModel = vm;
                    _logger.LogInformation("Navigating to {ViewModelType}", n.ViewModelType);
                }
                else
                {
                    _logger.LogError("Cannot find VM for {ViewModelType}", n.ViewModelType);
                }
            });

        MessageBus.Current.Listen<NavigateBack>()
            .Subscribe(n =>
            {
                if (_history.Count <= 0) return;
                ContentViewModel = _history.Last();
                _history.RemoveAt(_history.Count - 1);
            });
        
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            VersionDisplay = $"v{fvi.FileVersion}";
            _logger.LogInformation("Wabbajack Version: {Version}", fvi.FileVersion);
                
            Task.Run(() => _wjClient.SendMetric("started_wabbajack", fvi.FileVersion)).FireAndForget();
            Task.Run(() => _wjClient.SendMetric("started_sha", ThisAssembly.Git.Sha));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error Loading Version");
            VersionDisplay = "ERROR";
        }

    }
    
    
}