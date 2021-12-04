using System;
using Wabbajack.App.Wpf.Support;

namespace Wabbajack.App.Wpf.Interfaces;

public interface IScreenView
{
    public Type ViewFor { get; }

    public void SetViewModel(ViewModel vm);
}