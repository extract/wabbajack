
using System.Reactive.Disposables;
using ReactiveUI;
using Wabbajack.App.Wpf.Interfaces;
using Wabbajack.App.Wpf.Support;

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
                this.WhenAny(x => x.ViewModel.BrowseCommand)
                    .BindToStrict(this, x => x.BrowseButton.Command)
                    .DisposeWith(dispose);
                this.WhenAny(x => x.ViewModel.InstallCommand)
                    .BindToStrict(this, x => x.InstallButton.Command)
                    .DisposeWith(dispose);
                this.WhenAny(x => x.ViewModel.CompileCommand)
                    .BindToStrict(this, x => x.CompileButton.Command)
                    .DisposeWith(dispose);
            });
        }
    }
}
