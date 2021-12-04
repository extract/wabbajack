using System;
using Wabbajack.App.Wpf.Support;

namespace Wabbajack.App.Wpf.Messages;

public class NavigateTo
{
    public Type ViewModelType { get; init; }
    public bool SaveHistory { get; init; }
    
    public static NavigateTo Create<T>(bool saveHistory = true) where T : ViewModel
    {
        return new NavigateTo
        {
            SaveHistory = saveHistory,
            ViewModelType = typeof(T)
        };
    }

}