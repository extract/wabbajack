using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ReactiveUI.Fody.Helpers;
using Wabbajack.DTOs.Logins;

namespace Wabbajack.App.Blazor.Browser;

public abstract class BrowserTabViewModel : ViewModel
{
    [Reactive]
    public string HeaderText { get; set; }
    
    [Reactive]
    public string Instructions { get; set; }

    public BrowserView? Browser { get; set; }

    private WebView2 _browser => Browser!.Browser;

    public async Task RunWrapper(CancellationToken token)
    {
        await Run(token);
    }
    
    protected abstract Task Run(CancellationToken token);

    protected async Task WaitForReady()
    {
        while (Browser?.Browser.CoreWebView2 == null)
        {
            await Task.Delay(250);
        }
    }

    public async Task NavigateTo(Uri uri)
    {
        var tcs = new TaskCompletionSource();

        void Completed(object? o, CoreWebView2NavigationCompletedEventArgs a)
        {
            if (a.IsSuccess)
            {
                tcs.TrySetResult();
            }
            else
            {
                tcs.TrySetException(new Exception($"Navigation error to {uri}"));
            }
        }

        _browser.NavigationCompleted += Completed;
        _browser.Source = uri;
        await tcs.Task;
        _browser.NavigationCompleted -= Completed;
    }
    
    public async Task<Cookie[]> GetCookies(string domainEnding, CancellationToken token)
    {
        var cookies = (await _browser.CoreWebView2.CookieManager.GetCookiesAsync(""))
            .Where(c => c.Domain.EndsWith(domainEnding));
        return cookies.Select(c => new Cookie
        {
            Domain = c.Domain,
            Name = c.Name,
            Path = c.Path,
            Value = c.Value
        }).ToArray();
    }

    public async Task<string> EvaluateJavaScript(string js)
    {
        return await _browser.ExecuteScriptAsync(js);
    }

    public async Task<HtmlDocument> GetDom(CancellationToken token)
    {
        var v = HttpUtility.UrlDecode("\u003D");
        var source = await EvaluateJavaScript("document.body.outerHTML");
        var decoded = JsonSerializer.Deserialize<string>(source);
        var doc = new HtmlDocument();
        doc.LoadHtml(decoded);
        return doc;
    }
    
}
