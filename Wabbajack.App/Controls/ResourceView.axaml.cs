using System.Reactive.Disposables;
using Avalonia.ReactiveUI;
using FluentFTP.Helpers;
using ReactiveUI;
using PropertyBindingMixins = ReactiveUI.PropertyBindingMixins;

namespace Wabbajack.App.Controls;

public partial class ResourceView : ReactiveUserControl<ResourceViewModel>, IActivatableView
{
    public ResourceView()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            PropertyBindingMixins.OneWayBind(this, ViewModel, vm => vm.Name, view => view.ResourceName.Text)
                .DisposeWith(disposables);

            Bind<>(ViewModel, vm => vm.MaxTasks, view => view.MaxTasks.Text)
                .DisposeWith(disposables);

            PropertyBindingMixins.Bind(this, ViewModel, vm => vm.MaxThroughput, view => view.MaxThroughput.Text,
                    l => l is 0 or long.MaxValue ? "∞" : (l / 1024 / 1024).ToString(),
                    v =>
                    {
                        v = v.Trim();
                        if (v is "0" or "∞" || v == long.MaxValue.ToString()) return long.MaxValue;
                        return long.TryParse(v, out var l) ? l * 1024 * 1024 : long.MaxValue;
                    })
                .DisposeWith(disposables);

            PropertyBindingMixins.OneWayBind(this, ViewModel, vm => vm.CurrentThroughput,
                    view => view.CurrentThroughput.Text,
                    val => val.FileSizeToString())
                .DisposeWith(disposables);
        });
    }
}