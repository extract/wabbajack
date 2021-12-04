using Wabbajack.App.Wpf.Support;

namespace Wabbajack.App.Wpf.ViewModels;

public class ModListGalleryFilterSettingsViewModel : ViewModel
{
    public bool ShowNSFW { get; set; }
    public bool OnlyInstalled { get; set; }
    public string Game { get; set; }
    public string Search { get; set; }
    private bool _isPersistent = true;
    public bool IsPersistent { get => _isPersistent; set => RaiseAndSetIfChanged(ref _isPersistent, value); }
        
    private bool _useCompression = false;
    public bool UseCompression { get => _useCompression; set => RaiseAndSetIfChanged(ref _useCompression, value); }
    public bool ShowUtilityLists { get; set; }
}