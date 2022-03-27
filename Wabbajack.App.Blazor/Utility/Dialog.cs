﻿using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.WindowsAPICodePack.Dialogs;
using Wabbajack.Paths;

namespace Wabbajack.App.Blazor.Utility;

public static class Dialog
{
    /*
     * TODO: [Critical] CommonOpenFileDialog.ShowDialog() causes UI freeze and crash.
     * This method seems to alleviate it, but it still occasionally happens.
     */
    public static async Task<AbsolutePath?> ShowDialogNonBlocking(bool isFolderPicker = false)
    {
        return await Task.Factory.StartNew(() =>
            {
                Window newWindow = new();
                var dialog = new CommonOpenFileDialog();
                dialog.IsFolderPicker = isFolderPicker;
                dialog.Multiselect    = false;
                var result = dialog.ShowDialog(newWindow);
                return result == CommonFileDialogResult.Ok ? dialog.FileName : null;
            }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.FromCurrentSynchronizationContext())
            .ContinueWith(result => result.Result?.ToAbsolutePath())
            .ConfigureAwait(false);
    }
}
