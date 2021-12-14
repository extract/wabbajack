using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using ReactiveUI;

namespace Wabbajack.App.Wpf.Controls.Installer
{
    /// <summary>
    /// Interaction logic for InstallationConfigurationView.xaml
    /// </summary>
    public partial class InstallationConfigurationView : ReactiveUserControl<InstallerVM>
    {
        public InstallationConfigurationView()
        {
            InitializeComponent();
            this.WhenActivated(dispose =>
            {
                this.WhenAny(x => x.ViewModel.Installer.ConfigVisualVerticalOffset)
                    .Select(i => (double)i)
                    .BindToStrict(this, x => x.InstallConfigSpacer.Height)
                    .DisposeWith(dispose);
                this.WhenAny(x => x.ViewModel.ModListLocation)
                    .BindToStrict(this, x => x.ModListLocationPicker.PickerVM)
                    .DisposeWith(dispose);
                this.WhenAny(x => x.ViewModel.Installer)
                    .BindToStrict(this, x => x.InstallerCustomizationContent.Content)
                    .DisposeWith(dispose);
                this.WhenAny(x => x.ViewModel.BeginCommand)
                    .BindToStrict(this, x => x.BeginButton.Command)
                    .DisposeWith(dispose);

                // Error icon display
                var vis = this.WhenAny(x => x.ViewModel.Installer.CanInstall)
                    .Select(err => err.Failed ? Visibility.Visible : Visibility.Hidden)
                    .Replay(1)
                    .RefCount();
                vis.BindToStrict(this, x => x.ErrorSummaryIconGlow.Visibility)
                    .DisposeWith(dispose);
                vis.BindToStrict(this, x => x.ErrorSummaryIcon.Visibility)
                    .DisposeWith(dispose);
                this.WhenAny(x => x.ViewModel.Installer.CanInstall)
                    .Select(x => x.Reason)
                    .BindToStrict(this, x => x.ErrorSummaryIcon.ToolTip)
                    .DisposeWith(dispose);
            });
        }
    }
}
