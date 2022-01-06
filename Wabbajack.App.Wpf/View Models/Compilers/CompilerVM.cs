﻿using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Wabbajack.Extensions;
using Wabbajack.Interventions;
using Wabbajack.Messages;
using Wabbajack.RateLimiter;
using ReactiveUI;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WindowsAPICodePack.Dialogs;
using ReactiveUI.Fody.Helpers;
using Wabbajack.Common;
using Wabbajack.Compiler;
using Wabbajack.Downloaders;
using Wabbajack.Downloaders.GameFile;
using Wabbajack.DTOs;
using Wabbajack.DTOs.Interventions;
using Wabbajack.DTOs.JsonConverters;
using Wabbajack.Installer;
using Wabbajack.Models;
using Wabbajack.Networking.WabbajackClientApi;
using Wabbajack.Paths;
using Wabbajack.Paths.IO;
using Wabbajack.Services.OSIntegrated;
using Wabbajack.VFS;

namespace Wabbajack
{
    
    
    public enum CompilerState
    {
        Configuration,
        Compiling,
        Completed,
        Errored
    }
    public class CompilerVM : BackNavigatingVM, ICpuStatusVM
    {
        private const string LastSavedCompilerSettings = "last-saved-compiler-settings";
        private readonly DTOSerializer _dtos;
        private readonly SettingsManager _settingsManager;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CompilerVM> _logger;
        private readonly ResourceMonitor _resourceMonitor;

        [Reactive]
        public CompilerState State { get; set; }
        
        [Reactive]
        public ISubCompilerVM SubCompilerVM { get; set; }
        
        // Paths 
        public FilePickerVM ModlistLocation { get; }
        public FilePickerVM DownloadLocation { get; }
        public FilePickerVM OutputLocation { get; }
        
        // Modlist Settings
        
        [Reactive] public string ModListName { get; set; }
        [Reactive] public string Version { get; set; }
        [Reactive] public string Author { get; set; }
        [Reactive] public string Description { get; set; }
        public FilePickerVM ModListImagePath { get; } = new();
        [Reactive] public ImageSource ModListImage { get; set; }
        [Reactive] public string Website { get; set; }
        [Reactive] public string Readme { get; set; }
        [Reactive] public bool IsNSFW { get; set; }
        [Reactive] public bool PublishUpdate { get; set; }
        [Reactive] public string MachineUrl { get; set; }
        [Reactive] public Game BaseGame { get; set; }
        [Reactive] public string SelectedProfile { get; set; }
        [Reactive] public AbsolutePath GamePath { get; set; }
        [Reactive] public bool IsMO2Compilation { get; set; }

        [Reactive] public RelativePath[] AlwaysEnabled { get; set; } = Array.Empty<RelativePath>();
        [Reactive] public string[] OtherProfiles { get; set; } = Array.Empty<string>();
        
        [Reactive] public AbsolutePath Source { get; set; }
        
        public AbsolutePath SettingsOutputLocation => Source.Combine(ModListName).WithExtension(Ext.CompilerSettings);
        
        
        public ReactiveCommand<Unit, Unit> ExecuteCommand { get; }

        public LoggerProvider LoggerProvider { get; }
        public ReadOnlyObservableCollection<CPUDisplayVM> StatusList => _resourceMonitor.Tasks;
        
        public CompilerVM(ILogger<CompilerVM> logger, DTOSerializer dtos, SettingsManager settingsManager,
            IServiceProvider serviceProvider, LoggerProvider loggerProvider, ResourceMonitor resourceMonitor) : base(logger)
        {
            _logger = logger;
            _dtos = dtos;
            _settingsManager = settingsManager;
            _serviceProvider = serviceProvider;
            LoggerProvider = loggerProvider;
            _resourceMonitor = resourceMonitor;

            BackCommand =
                ReactiveCommand.CreateFromTask(async () =>
                {
                    await SaveSettingsFile();
                    NavigateToGlobal.Send(NavigateToGlobal.ScreenType.ModeSelectionView);
                });
            
            SubCompilerVM = new MO2CompilerVM(this);

            ExecuteCommand = ReactiveCommand.CreateFromTask(async () => await StartCompilation());

            ModlistLocation = new FilePickerVM()
            {
                ExistCheckOption = FilePickerVM.CheckOptions.On,
                PathType = FilePickerVM.PathTypeOptions.File,
                PromptTitle = "Select a config file or a modlist.txt file"
            };

            DownloadLocation = new FilePickerVM()
            {
                ExistCheckOption = FilePickerVM.CheckOptions.On,
                PathType = FilePickerVM.PathTypeOptions.Folder,
                PromptTitle = "Location where the downloads for this list are stored"
            };
            
            OutputLocation = new FilePickerVM()
            {
                ExistCheckOption = FilePickerVM.CheckOptions.On,
                PathType = FilePickerVM.PathTypeOptions.Folder,
                PromptTitle = "Location where the compiled modlist will be stored"
            };
            
            ModlistLocation.Filters.AddRange(new []
            {
                new CommonFileDialogFilter("MO2 Modlist", "*" + Ext.Txt),
                new CommonFileDialogFilter("Compiler Settings File", "*" + Ext.CompilerSettings)
            });

            
            this.WhenActivated(disposables =>
            {
                State = CompilerState.Configuration;
                Disposable.Empty.DisposeWith(disposables);

                ModlistLocation.WhenAnyValue(vm => vm.TargetPath)
                    .Subscribe(p => InferModListFromLocation(p).FireAndForget())
                    .DisposeWith(disposables);
                
                LoadLastSavedSettings().FireAndForget();
            });
        }

        private async Task InferModListFromLocation(AbsolutePath settingsFile)
        {
            if (settingsFile == default) return;
            
            using var ll = LoadingLock.WithLoading();
            if (settingsFile.FileName == "modlist.txt".ToRelativePath() && settingsFile.Depth > 3)
            {
                var mo2Folder = settingsFile.Parent.Parent.Parent;
                var mo2Ini = mo2Folder.Combine(Consts.MO2IniName);
                if (mo2Ini.FileExists())
                {
                    var iniData = mo2Ini.LoadIniFile();

                    var general = iniData["General"];

                    BaseGame = GameRegistry.GetByFuzzyName(general["gameName"].FromMO2Ini()).Game;
                    Source = mo2Folder;

                    SelectedProfile = general["selected_profile"].FromMO2Ini();
                    GamePath = general["gamePath"].FromMO2Ini().ToAbsolutePath();
                    ModListName = SelectedProfile;

                    var settings = iniData["Settings"];
                    var downloadLocation = settings["download_directory"].FromMO2Ini().ToAbsolutePath();
                    
                    if (downloadLocation == default)
                        downloadLocation = Source.Combine("downloads");
                    
                    DownloadLocation.TargetPath = downloadLocation;
                    IsMO2Compilation = true;


                    
                    AlwaysEnabled = Array.Empty<RelativePath>();
                    // Find Always Enabled mods
                    foreach (var modFolder in mo2Folder.Combine("mods").EnumerateDirectories())
                    {
                        var iniFile = modFolder.Combine("meta.ini");
                        if (!iniFile.FileExists()) continue;

                        var data = iniFile.LoadIniFile();
                        var generalModData = data["General"];
                        if ((generalModData["notes"]?.Contains("WABBAJACK_ALWAYS_ENABLE") ?? false) ||
                            (generalModData["comments"]?.Contains("WABBAJACK_ALWAYS_ENABLE") ?? false))
                            AlwaysEnabled = AlwaysEnabled.Append(modFolder.RelativeTo(mo2Folder)).ToArray();
                    }

                    var otherProfilesFile = settingsFile.Parent.Combine("otherprofiles.txt");
                    if (otherProfilesFile.FileExists())
                    {
                        OtherProfiles = await otherProfilesFile.ReadAllLinesAsync().ToArray();
                    }

                    if (mo2Folder.Depth > 1)
                        OutputLocation.TargetPath = mo2Folder.Parent;

                    await SaveSettingsFile();
                    ModlistLocation.TargetPath = SettingsOutputLocation;
                }
            }

        }

        private async Task StartCompilation()
        {
            var tsk = Task.Run(async () =>
            {
                try
                {
                    State = CompilerState.Compiling;

                    var mo2Settings = new MO2CompilerSettings
                    {
                        Game = BaseGame,
                        ModListName = ModListName,
                        ModListAuthor = Author,
                        ModlistReadme = Readme,
                        Source = Source,
                        Downloads = DownloadLocation.TargetPath,
                        OutputFile = OutputLocation.TargetPath,
                        Profile = SelectedProfile,
                        OtherProfiles = OtherProfiles,
                        AlwaysEnabled = AlwaysEnabled
                    };

                    var compiler = new MO2Compiler(_serviceProvider.GetRequiredService<ILogger<MO2Compiler>>(),
                        _serviceProvider.GetRequiredService<FileExtractor.FileExtractor>(),
                        _serviceProvider.GetRequiredService<FileHashCache>(),
                        _serviceProvider.GetRequiredService<Context>(),
                        _serviceProvider.GetRequiredService<TemporaryFileManager>(),
                        mo2Settings,
                        _serviceProvider.GetRequiredService<ParallelOptions>(),
                        _serviceProvider.GetRequiredService<DownloadDispatcher>(),
                        _serviceProvider.GetRequiredService<Client>(),
                        _serviceProvider.GetRequiredService<IGameLocator>(),
                        _serviceProvider.GetRequiredService<DTOSerializer>(),
                        _serviceProvider.GetRequiredService<IResource<ACompiler>>(),
                        _serviceProvider.GetRequiredService<IBinaryPatchCache>());

                    await compiler.Begin(CancellationToken.None);

                    State = CompilerState.Completed;
                }
                catch (Exception ex)
                {
                    State = CompilerState.Errored;
                    _logger.LogInformation(ex, "Failed Compilation : {Message}", ex.Message);
                }
            });

            await tsk;
        }
        
        private async Task SaveSettingsFile()
        {
            if (Source == default) return;
            await using var st = SettingsOutputLocation.Open(FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(st, GetSettings(), _dtos.Options);

            await _settingsManager.Save(LastSavedCompilerSettings, Source);
        }

        private async Task LoadLastSavedSettings()
        {
            var lastPath = await _settingsManager.Load<AbsolutePath>(LastSavedCompilerSettings);
            if (Source == default) return;
            Source = lastPath;
        }

                    
        private CompilerSettings GetSettings()
        {
            return new CompilerSettings
            {
                ModListName = ModListName,
                ModListAuthor = Author,
                Downloads = DownloadLocation.TargetPath,
                Source = ModlistLocation.TargetPath,
                Game = BaseGame,
                Profile = SelectedProfile,
                UseGamePaths = true,
                OutputFile = OutputLocation.TargetPath.Combine(SelectedProfile).WithExtension(Ext.Wabbajack),
                AlwaysEnabled = AlwaysEnabled.ToArray(),
                OtherProfiles = OtherProfiles.ToArray()
            };
        }
    }
}
