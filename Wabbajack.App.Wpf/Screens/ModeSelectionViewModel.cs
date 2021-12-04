using System.Reactive;
using System.Windows.Input;
using ReactiveUI;
using Wabbajack.App.Wpf.Interfaces;
using Wabbajack.App.Wpf.Messages;
using Wabbajack.App.Wpf.Support;

namespace Wabbajack.App.Wpf.Screens
{
    public class ModeSelectionViewModel : ViewModel, IScreenViewModel
    {
        private MainWindowViewModel _mainVM;
        public ICommand BrowseCommand { get; }
        public ICommand InstallCommand { get; }
        public ICommand CompileCommand { get; }
        public ReactiveCommand<Unit, Unit> UpdateCommand { get; }

        public ModeSelectionViewModel(MainWindowViewModel mainVM)
        {
            _mainVM = mainVM;

            BrowseCommand = ReactiveCommand.Create(() =>
            {
                MessageBus.Current.SendMessage(NavigateTo.Create<ModListGalleryViewModel>());
            });

        }
    }
}
