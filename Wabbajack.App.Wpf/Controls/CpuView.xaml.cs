using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using ReactiveUI;
using Wabbajack.RateLimiter;

namespace Wabbajack.App.Wpf.Controls
{
    /// <summary>
    /// Interaction logic for CpuView.xaml
    /// </summary>
    public partial class CpuView : UserControlRx<ICpuStatusVM>
    {
        public Percent ProgressPercent
        {
            get => (Percent)GetValue(ProgressPercentProperty);
            set => SetValue(ProgressPercentProperty, value);
        }
        public static readonly DependencyProperty ProgressPercentProperty = DependencyProperty.Register(nameof(ProgressPercent), typeof(Percent), typeof(CpuView),
             new FrameworkPropertyMetadata(default(Percent), WireNotifyPropertyChanged));

        public MainSettings SettingsHook
        {
            get => (MainSettings)GetValue(SettingsHookProperty);
            set => SetValue(SettingsHookProperty, value);
        }
        public static readonly DependencyProperty SettingsHookProperty = DependencyProperty.Register(nameof(SettingsHook), typeof(MainSettings), typeof(CpuView),
             new FrameworkPropertyMetadata(default(SettingsVM), WireNotifyPropertyChanged));

        private bool _ShowingSettings;
        public bool ShowingSettings { get => _ShowingSettings; set => this.RaiseAndSetIfChanged(ref _ShowingSettings, value); }

        public CpuView()
        {
            InitializeComponent();
            this.WhenActivated(disposable =>
            {
               
                this.WhenAny(x => x.ViewModel.StatusList)
                    .BindToStrict(this, x => x.CpuListControl.ItemsSource)
                    .DisposeWith(disposable);

                // Progress
                this.WhenAny(x => x.ProgressPercent)
                    .Select(p => p.Value)
                    .BindToStrict(this, x => x.HeatedBorderRect.Opacity)
                    .DisposeWith(disposable);
            });
        }
    }
}
