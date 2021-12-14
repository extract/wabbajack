using System.Reactive.Disposables;
using ReactiveUI;

namespace Wabbajack.App.Wpf.Controls.Interventions
{
    /// <summary>
    /// Interaction logic for ConfirmUpdateOfExistingInstallView.xaml
    /// </summary>
    public partial class ConfirmUpdateOfExistingInstallView : ReactiveUserControl<ConfirmUpdateOfExistingInstallVM>
    {
        public ConfirmUpdateOfExistingInstallView()
        {
            InitializeComponent();
            this.WhenActivated(dispose =>
            {
                this.WhenAny(x => x.ViewModel!.ShortDescription)
                    .BindToStrict(this, x => x.ShortDescription.Text)
                    .DisposeWith(dispose);
                this.WhenAny(x => x.ViewModel!.ExtendedDescription)
                    .BindToStrict(this, x => x.ExtendedDescription.Text)
                    .DisposeWith(dispose);
                this.WhenAny(x => x.ViewModel!.Source.ConfirmCommand)
                    .BindToStrict(this, x => x.ConfirmButton.Command)
                    .DisposeWith(dispose);
                this.WhenAny(x => x.ViewModel!.Source.CancelCommand)
                    .BindToStrict(this, x => x.CancelButton.Command)
                    .DisposeWith(dispose);

                this.BindStrict(this.ViewModel, x => x.Installer.AutomaticallyOverwrite, x => x.AutoOverwriteCheckbox.IsChecked,
                        vmToViewConverter: x => x,
                        viewToVmConverter: x => x ?? false)
                    .DisposeWith(dispose);
            });
        }
    }
}
