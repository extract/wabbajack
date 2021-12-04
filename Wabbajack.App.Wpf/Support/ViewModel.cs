using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Wabbajack.App.Wpf.Support;

public class ViewModel : ReactiveObject, IDisposable, IActivatableViewModel
{
    public ViewModel()
    {
        Activator = new ViewModelActivator();

        IsActivated.BindTo(this, vm => vm.IsActive).DisposeWith(CompositeDisposable);

    }
    public ViewModelActivator Activator { get; }

    public IObservable<bool> IsActivated => Activator.Activated.Select(_ => true).Merge(
        Activator.Deactivated.Select(_ => false));

    private readonly Lazy<CompositeDisposable> _compositeDisposable = new();
    
    [Reactive]
    public bool IsActive { get; set; }
        
    [JsonIgnore]
    public CompositeDisposable CompositeDisposable => _compositeDisposable.Value;

    public virtual void Dispose()
    {
        if (_compositeDisposable.IsValueCreated)
        {
            _compositeDisposable.Value.Dispose();
        }
    }

    protected void RaiseAndSetIfChanged<T>(
        ref T item,
        T newItem,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(item, newItem)) return;
        item = newItem;
        this.RaisePropertyChanged(propertyName);
    }

}