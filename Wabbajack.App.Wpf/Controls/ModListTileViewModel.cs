

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Wabbajack.App.Wpf.Screens;
using Wabbajack.App.Wpf.Support;
using Wabbajack.Common;
using Wabbajack.DTOs;
using Wabbajack.Paths;
using Wabbajack.Paths.IO;
using Wabbajack.RateLimiter;
using Wabbajack.Services.OSIntegrated;
using Wabbajack.VFS;

namespace Wabbajack.App.Wpf.Controls
{
    
    public enum ModListState
    {
        Downloaded,
        NotDownloaded,
        Downloading,
        Disabled
    }

    public struct ModListTag
    {
        public ModListTag(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    public class ModListTileViewModel : ViewModel
    {
        public ModlistMetadata Metadata { get; }
        private ModListGalleryViewModel _parent;

        public ICommand OpenWebsiteCommand { get; }
        public ICommand ExecuteCommand { get; }
        
        public ICommand ModListContentsCommend { get; }
        
        [Reactive]
        public ModListState State { get; set; }
        
        [Reactive]
        public bool Exists { get; set; }
        

        public AbsolutePath Location { get; }

        [Reactive]
        public List<ModListTag> ModListTagList { get; private set; }

        [Reactive]
        public Percent ProgressPercent { get; private set; }

        [Reactive]
        public bool IsBroken { get; private set; }
        
        [Reactive]
        public bool IsDownloading { get; private set; }

        [Reactive]
        public string DownloadSizeText { get; private set; }

        [Reactive]
        public string InstallSizeText { get; private set; }
        
        [Reactive]
        public string VersionText { get; private set; }

        //[Reactive]
        //public IErrorResponse Error { get; private set; }

        
        [Reactive]
        public BitmapSource Image { get; set; }
        
        public Uri ImageUri => new(Metadata.Links.ImageUri);
        
        [Reactive]
        public bool LoadingImage { get; set; }

        private Subject<bool> IsLoadingIdle;
        private readonly Configuration _configuration;
        private readonly FileHashCache _hashCache;
        private readonly ImageCache _imageCache;

        public AbsolutePath ModListLocation => _configuration.ModListsDownloadLocation.Combine(Metadata.Links.MachineURL)
            .WithExtension(Ext.Wabbajack);

        public ModListTileViewModel(ModListGalleryViewModel parent, ModlistMetadata metadata, Configuration configuration,
            FileHashCache hashCache, ImageCache imageCache)
        {
            _parent = parent;
            _configuration = configuration;
            _hashCache = hashCache;
            _imageCache = imageCache;
            Metadata = metadata;
            //Location = LauncherUpdater.CommonFolder.Value.Combine("downloaded_mod_lists", Metadata.Links.MachineURL + (string)Consts.ModListExtension);
            ModListTagList = new List<ModListTag>();

            Metadata.tags.ForEach(tag =>
            {
                ModListTagList.Add(new ModListTag(tag));
            });
            ModListTagList.Add(new ModListTag(metadata.Game.MetaData().HumanFriendlyGameName));

            DownloadSizeText = "Download size : " + UIUtils.FormatBytes(Metadata.DownloadMetadata.SizeOfArchives);
            InstallSizeText = "Installation size : " + UIUtils.FormatBytes(Metadata.DownloadMetadata.SizeOfInstalledFiles);
            VersionText = "Modlist version : " + Metadata.Version;
            IsBroken = metadata.ValidationSummary.HasFailures || metadata.ForceDown;
            //https://www.wabbajack.org/#/modlists/info?machineURL=eldersouls
            OpenWebsiteCommand = ReactiveCommand.Create(() => UIUtils.OpenWebsite(new Uri($"https://www.wabbajack.org/#/modlists/info?machineURL={Metadata.Links.MachineURL}")));

            IsLoadingIdle = new Subject<bool>();

            this.WhenAny(vm => vm.State)
                .Select(s => s == ModListState.Downloaded)
                .StartWith(false)
                .BindTo(this, vm => vm.Exists)
                .DisposeWith(CompositeDisposable);
            
            UpdateState().FireAndForget();
            LoadImage().FireAndForget();

            /*
            ModListContentsCommend = ReactiveCommand.Create(async () =>
            {
                _parent.MWVM.ModListContentsVM.Value.Name = metadata.Title;
                IsLoadingIdle.OnNext(false);
                try
                {
                    var status = await ClientAPIEx.GetDetailedStatus(metadata.Links.MachineURL);
                    var coll = _parent.MWVM.ModListContentsVM.Value.Status;
                    coll.Clear();
                    coll.AddRange(status.Archives);
                    _parent.MWVM.NavigateTo(_parent.MWVM.ModListContentsVM.Value);
                }
                finally
                {
                    IsLoadingIdle.OnNext(true);
                }
            }, IsLoadingIdle.StartWith(true));
            ExecuteCommand = ReactiveCommand.CreateFromObservable<Unit, Unit>(
                canExecute: this.WhenAny(x => x.IsBroken).Select(x => !x),
                execute: (unit) =>
                Observable.Return(unit)
                .WithLatestFrom(
                    this.WhenAny(x => x.Exists),
                    (_, e) => e)
                // Do any download work on background thread
                .ObserveOn(RxApp.TaskpoolScheduler)
                .SelectTask(async (exists) =>
                {
                    if (!exists)
                    {
                        try
                        {
                            var success = await Download();
                            if (!success)
                            {
                                Error = ErrorResponse.Fail("Download was marked unsuccessful");
                                return false;
                            }
                        }
                        catch (Exception ex)
                        {
                            Error = ErrorResponse.Fail(ex);
                            return false;
                        }
                        // Return an updated check on exists
                        return Location.Exists;
                    }
                    return exists;
                })
                .Where(exists => exists)
                // Do any install page swap over on GUI thread
                .ObserveOnGuiThread()
                .Select(_ =>
                {
                    _parent.MWVM.OpenInstaller(Location);

                    // Wait for modlist member to be filled, then open its readme
                    return _parent.MWVM.Installer.Value.WhenAny(x => x.ModList)
                        .NotNull()
                        .Take(1)
                        .Do(modList =>
                        {
                            try
                            {
                                modList.OpenReadme();
                            }
                            catch (Exception ex)
                            {
                                Utils.Error(ex);
                            }
                        });
                })
                .Switch()
                .Unit());



            var imageObs = Observable.Return(Metadata.Links.ImageUri)
                .DownloadBitmapImage((ex) => Utils.Log($"Error downloading modlist image {Metadata.Title}"));

            _Image = imageObs
                .ToGuiProperty(this, nameof(Image));

            _LoadingImage = imageObs
                .Select(x => false)
                .StartWith(true)
                .ToGuiProperty(this, nameof(LoadingImage));
                */
        }



        private async Task<bool> Download()
        {
            /*
            ProgressPercent = Percent.Zero;
            using (var queue = new WorkQueue(1))
            using (queue.Status.Select(i => i.ProgressPercent)
                .ObserveOnGuiThread()
                .Subscribe(percent => ProgressPercent = percent))
            {
                var tcs = new TaskCompletionSource<bool>();
                queue.QueueTask(async () =>
                {
                    try
                    {
                        IsDownloading = true;
                        Utils.Log($"Starting Download of {Metadata.Links.MachineURL}");
                        var downloader = DownloadDispatcher.ResolveArchive(Metadata.Links.Download);
                        var result = await downloader.Download(
                            new Archive(state: null!)
                            {
                                Name = Metadata.Title, Size = Metadata.DownloadMetadata?.Size ?? 0
                            }, Location);
                        Utils.Log($"Done downloading {Metadata.Links.MachineURL}");

                        // Want to rehash to current file, even if failed?
                        await Location.FileHashCachedAsync();
                        Utils.Log($"Done hashing {Metadata.Links.MachineURL}");

                        await Metadata.ToJsonAsync(Location.WithExtension(Consts.ModlistMetadataExtension));
                        
                        tcs.SetResult(result);
                    }
                    catch (Exception ex)
                    {
                        Utils.Error(ex, $"Error Downloading of {Metadata.Links.MachineURL}");
                        tcs.SetException(ex);
                    }
                    finally
                    {
                        IsDownloading = false;
                    }
                });


                Task.Run(async () => await Metrics.Send(Metrics.Downloading, Metadata.Title))
                    .FireAndForget(ex => Utils.Error(ex, "Error sending download metric"));

                return await tcs.Task;
                */
            return true;
        }
        
                
        public async Task LoadImage()
        {
            LoadingImage = true;
            Image = await _imageCache.From(ImageUri, 540, 300);
            LoadingImage = false;
        }
        
        public async Task<ModListState> GetState()
        {
            if (Metadata.ForceDown || Metadata.ValidationSummary.HasFailures)
                return ModListState.Disabled;
        
            var file = ModListLocation;
            if (!file.FileExists())
                return ModListState.NotDownloaded;

            return await _hashCache.FileHashCachedAsync(file, CancellationToken.None) !=
                   Metadata.DownloadMetadata?.Hash
                ? ModListState.NotDownloaded
                : ModListState.Downloaded;
        }
        
        public async Task UpdateState()
        {
            State = await GetState();
        }

    }
    
}
