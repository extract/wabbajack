using System;
using System.Windows.Input;
using ReactiveUI;

namespace Wabbajack.App.Wpf.Controls.Interventions
{
    public abstract class AUserIntervention : ReactiveObject, IUserIntervention
    {
        public DateTime Timestamp { get; } = DateTime.Now;
        public abstract string ShortDescription { get; }
        public abstract string ExtendedDescription { get; }

        private bool _handled;
        public bool Handled { get => _handled; set => this.RaiseAndSetIfChanged(ref _handled, value); }

        public abstract void Cancel();
        public ICommand CancelCommand { get; }

        public AUserIntervention()
        {
            CancelCommand = ReactiveCommand.Create(() => Cancel());
        }
    }
}
