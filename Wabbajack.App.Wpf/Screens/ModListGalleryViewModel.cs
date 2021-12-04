using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Wabbajack.App.Wpf.Controls;
using Wabbajack.App.Wpf.Extensions;
using Wabbajack.App.Wpf.Interfaces;
using Wabbajack.App.Wpf.Messages;
using Wabbajack.App.Wpf.Support;
using Wabbajack.App.Wpf.ViewModels;
using Wabbajack.Common;
using Wabbajack.Downloaders.GameFile;
using Wabbajack.DTOs;
using Wabbajack.Hashing.xxHash64;
using Wabbajack.Networking.WabbajackClientApi;
using ReactiveUIExt = Wabbajack.App.Wpf.Extensions.ReactiveUIExt;

namespace Wabbajack.App.Wpf.Screens
{
    public class ModListGalleryViewModel : ViewModel, IScreenViewModel
    {
        public ModListGalleryViewModel MWVM { get; }

        public ObservableCollectionExtended<ModListTileViewModel> ModLists { get; } = new();

        private const string ALL_GAME_TYPE = "All";

        [Reactive]
        public IErrorResponse Error { get; set; }

        [Reactive]
        public string Search { get; set; }

        [Reactive]
        public bool OnlyInstalled { get; set; }

        [Reactive]
        public bool ShowNSFW { get; set; }

        [Reactive]
        public bool ShowUtilityLists { get; set; }

        [Reactive]
        public string GameType { get; set; }

        public List<string> GameTypeEntries { get { return GetGameTypeEntries(); } }

        private readonly ObservableAsPropertyHelper<bool> _Loaded;

        private ModListGalleryFilterSettingsViewModel _settings;
        private readonly Client _wjClient;
        private readonly ILogger<ModListGalleryViewModel> _logger;
        private readonly GameLocator _gameLocator;

        public bool Loaded => _Loaded.Value;

        public ICommand ClearFiltersCommand { get; }
        
        public ICommand BackCommand { get; }

        public ModListGalleryViewModel(ILogger<ModListGalleryViewModel> logger, MainWindowViewModel mainWindowVM, Client wjClient, GameLocator gameLocator)
            : base()
        {
            _wjClient = wjClient;
            _logger = logger;
            _gameLocator = gameLocator;

            BackCommand = ReactiveCommand.Create(() =>
            {
                MessageBus.Current.SendMessage(new NavigateBack());
            });

            // load persistent filter settings
            /*
            if (settings.IsPersistent)
            {
                GameType = !string.IsNullOrEmpty(settings.Game) ? settings.Game : ALL_GAME_TYPE;
                ShowNSFW = settings.ShowNSFW;
                ShowUtilityLists = settings.ShowUtilityLists;
                OnlyInstalled = settings.OnlyInstalled;
                Search = settings.Search;
            }
            else
                GameType = ALL_GAME_TYPE;
                */

            // subscribe to save signal
            //MWVM.Settings.SaveSignal
            //    .Subscribe(_ => UpdateFiltersSettings())
            //    .DisposeWith(this.CompositeDisposable);

            ClearFiltersCommand = ReactiveCommand.Create(
                () =>
                {
                    OnlyInstalled = false;
                    ShowNSFW = false;
                    ShowUtilityLists = false;
                    Search = string.Empty;
                    GameType = ALL_GAME_TYPE;
                });


            ReactiveUIExt.WhenAny(this, x => x.OnlyInstalled)
                .Subscribe(val =>
                {
                    if(val)
                        GameType = ALL_GAME_TYPE;
                })
                .DisposeWith(CompositeDisposable);
            
            var sourceList = Observable.Return(Unit.Default)
                .ObserveOn(RxApp.TaskpoolScheduler)
                .SelectTask(async _ =>
                {
                    try
                    {
                        Error = null;
                        var list = await _wjClient.LoadLists();
                        Error = ErrorResponse.Success;
                        return list
                            .AsObservableChangeSet(x => x.DownloadMetadata?.Hash ?? default);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "While loading modlists");
                        Error = ErrorResponse.Fail(ex);
                        return Observable.Empty<IChangeSet<ModlistMetadata, Hash>>();
                    }
                })
                // Unsubscribe and release when not active
                .FlowSwitch(this.IsActivated,
                    valueWhenOff: Observable.Return(ChangeSet<ModlistMetadata, Hash>.Empty))
                .Switch()
                .RefCount();
            
            _Loaded = sourceList.CollectionCount()
                .Select(c => c > 0)
                .ToProperty(this, nameof(Loaded));

            // Convert to VM and bind to resulting list
            ReactiveUIExt.ObserveOnGuiThread(sourceList)
                .Transform(m => new ModListTileViewModel(this, m))
                .DisposeMany()
                // Filter only installed
                .Filter(ReactiveUIExt.WhenAny(this, x => x.OnlyInstalled)
                    .Select<bool, Func<ModListTileViewModel, bool>>(onlyInstalled => (vm) =>
                    {
                        if (!onlyInstalled) return true;
                        if (!GameRegistry.Games.TryGetValue(vm.Metadata.Game, out var gameMeta)) return false;
                        return _gameLocator.IsInstalled(gameMeta.Game);
                    }))
                // Filter on search box
                .Filter(ReactiveUIExt.WhenAny(this, x => x.Search)
                    .Debounce(TimeSpan.FromMilliseconds(150), RxApp.MainThreadScheduler)
                    .Select<string, Func<ModListTileViewModel, bool>>(search => (vm) =>
                    {
                        if (string.IsNullOrWhiteSpace(search)) return true;
                        return vm.Metadata.Title.ContainsCaseInsensitive(search) || vm.Metadata.tags.Any(t => t.ContainsCaseInsensitive(search));
                    }))
                .Filter(ReactiveUIExt.WhenAny(this, x => x.ShowNSFW)
                    .Select<bool, Func<ModListTileViewModel, bool>>(showNSFW => vm =>
                    {
                        if (!vm.Metadata.NSFW) return true;
                        return vm.Metadata.NSFW && showNSFW;
                    }))
                .Filter(ReactiveUIExt.WhenAny(this, x => x.ShowUtilityLists)
                    .Select<bool, Func<ModListTileViewModel, bool>>(showUtilityLists => vm => showUtilityLists ? vm.Metadata.UtilityList : !vm.Metadata.UtilityList))
                // Filter by Game
                .Filter(ReactiveUIExt.WhenAny(this, x => x.GameType)
                    .Debounce(TimeSpan.FromMilliseconds(150), RxApp.MainThreadScheduler)
                    .Select<string, Func<ModListTileViewModel, bool>>(GameType => (vm) =>
                    {
                        if (GameType == ALL_GAME_TYPE)
                            return true;
                        if (string.IsNullOrEmpty(GameType))
                            return false;

                        return GameType == vm.Metadata.Game.GetDescription<Game>().ToString();

                    }))
                .Bind(ModLists)
                .Subscribe()
                .DisposeWith(CompositeDisposable);

            // Extra GC when navigating away, just to immediately clean up modlist metadata
            ReactiveUIExt.WhenAny(this, x => x.IsActive)
                .Where(x => !x)
                .Skip(1)
                .Delay(TimeSpan.FromMilliseconds(50), RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    GC.Collect();
                })
                .DisposeWith(CompositeDisposable);
        }

        //public override void Unload()
        //{
            //Error = null;
        //}

        private List<string> GetGameTypeEntries()
        {
            /*
            List<string> gameEntries = new List<string> { ALL_GAME_TYPE };
            gameEntries.AddRange(EnumExtensions.GetAllItems<Game>().Select(gameType => gameType.GetDescription<Game>()));
            gameEntries.Sort();
            return gameEntries;
            */
            return new List<string>();
        }

        /*
        private void UpdateFiltersSettings()
        {
            settings.Game = GameType;
            settings.Search = Search;
            settings.ShowNSFW = ShowNSFW;
            settings.ShowUtilityLists = ShowUtilityLists;
            settings.OnlyInstalled = OnlyInstalled;
        }*/
    }
}
