using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Controls;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Wabbajack.App.Wpf.Interfaces;
using Wabbajack.App.Wpf.Messages;

namespace Wabbajack.App.Wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : IActivatableView
    {
        private readonly Dictionary<Type, Control> _screens;
        

        public MainWindow(MainWindowViewModel vm, IEnumerable<Interfaces.IScreenView> screens) : base(vm)
        {
            _screens = screens.ToDictionary(s => s.ViewFor, s => (Control)s);
            InitializeComponent();
            
            this.WhenActivated(disposables =>
            {
                this.ViewModel.WhenAnyValue(v => v.ContentViewModel)
                    .Where(v => v != null)
                    .Select(vm => (_screens[vm.GetType()], vm))
                    .Subscribe(t =>
                    {
                        var (s, vm) = t;
                        ((IScreenView)s).SetViewModel(vm);
                        Content.Content = s;
                    })
                    .DisposeWith(disposables);
            });


            // Bring window to the front if it isn't already
            Initialized += (s, e) =>
            {
                Activate();
                Topmost = true;
                Focus();
            };
            ContentRendered += (s, e) =>
            {
                Topmost = false;
            };
        }
    }
}