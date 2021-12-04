using System;
using ReactiveUI;
using Wabbajack.App.Wpf.Interfaces;

namespace Wabbajack.App.Wpf.Support;

public class ScreenView<TViewModel> : ReactiveUserControl<TViewModel>, IScreenView, IActivatableView
where TViewModel : ViewModel
{
    public IScreenViewModel ScreenViewModel => (IScreenViewModel) DataContext;

    public ScreenView(TViewModel vm)
    {
        ViewModel = vm;
        DataContext = vm;
    }

    public Type ViewFor => typeof(TViewModel);

    public void SetViewModel(ViewModel vm)
    {
        this.ViewModel = (TViewModel)vm;
    }
}