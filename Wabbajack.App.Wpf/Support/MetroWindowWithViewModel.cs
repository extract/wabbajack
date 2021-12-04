using MahApps.Metro.Controls;

namespace Wabbajack.App.Wpf.Support;

public class MetroWindowWithViewModel<TViewModel> : MetroWindow
    where TViewModel : ViewModel
{
    protected TViewModel ViewModel
    {
        get => (TViewModel) DataContext;
        set => DataContext = value;
    }

    public MetroWindowWithViewModel(TViewModel vm)
    {
        ViewModel = vm;
    }

}