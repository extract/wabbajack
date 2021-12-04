
using System.Reactive.Disposables;
using ReactiveUI;
using Wabbajack.App.Wpf.Extensions;
using Wabbajack.App.Wpf.Interfaces;
using Wabbajack.App.Wpf.Support;
using ReactiveUIExt = Wabbajack.App.Wpf.Extensions.ReactiveUIExt;

namespace Wabbajack.App.Wpf.Screens
{
    /// <summary>
    /// Interaction logic for ModeSelectionView.xaml
    /// </summary>
    public partial class ModeSelectionView
    {
        public ModeSelectionView(ModeSelectionViewModel vm) : base(vm)
        {
            InitializeComponent();
            this.WhenActivated(dispose =>
            {
                ReactiveUIExt.WhenAny(this, x => x.ViewModel.BrowseCommand)
                    .BindToStrict(this, x => x.BrowseButton.Command)
                    .DisposeWith(dispose);
                ReactiveUIExt.WhenAny(this, x => x.ViewModel.InstallCommand)
                    .BindToStrict(this, x => x.InstallButton.Command)
                    .DisposeWith(dispose);
                ReactiveUIExt.WhenAny(this, x => x.ViewModel.CompileCommand)
                    .BindToStrict(this, x => x.CompileButton.Command)
                    .DisposeWith(dispose);
            });
        }
    }
}
