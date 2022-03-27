using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Logging;
using Wabbajack.DTOs.Logins;
using Wabbajack.Services.OSIntegrated;

namespace Wabbajack.App.Blazor.Browser.ViewModels;

public abstract class IPSOAuth2Login<TLoginType> : BrowserTabViewModel
    where TLoginType : OAuth2LoginState, new()
{
    private readonly HttpClient _httpClient;
    private readonly EncryptedJsonTokenProvider<TLoginType> _tokenProvider;
    private readonly ILogger _logger;

    public IPSOAuth2Login(ILogger logger, HttpClient httpClient,
        EncryptedJsonTokenProvider<TLoginType> tokenProvider)
    {
        var tlogin = new TLoginType();
        HeaderText = $"{tlogin.SiteName} Login";
        _logger = logger;
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
    }

    protected override async Task Run(CancellationToken token)
    {
        var tlogin = new TLoginType();

        var tcs = new TaskCompletionSource<Uri>();
        await WaitForReady();
        Browser!.Browser.CoreWebView2.Settings.UserAgent = "Wabbajack";
        Browser!.Browser.NavigationStarting += (sender, args) =>
        {
            var uri = new Uri(args.Uri);
            if (uri.Scheme == "wabbajack")
            {
                tcs.TrySetResult(uri);
            }
        };
        
        Instructions = $"Please log in and allow Wabbajack to access your {tlogin.SiteName} account";

        var scopes = string.Join(" ", tlogin.Scopes);
        var state = Guid.NewGuid().ToString();

        await NavigateTo(new Uri(tlogin.AuthorizationEndpoint +
                                         $"?response_type=code&client_id={tlogin.ClientID}&state={state}&scope={scopes}"));

        var uri = await tcs.Task.WaitAsync(token);

        var cookies = await GetCookies(tlogin.AuthorizationEndpoint.Host, token);

        var parsed = HttpUtility.ParseQueryString(uri.Query);
        if (parsed.Get("state") != state)
        {
            _logger.LogCritical("Bad OAuth state, this shouldn't happen");
            throw new Exception("Bad OAuth State");
        }

        if (parsed.Get("code") == null)
        {
            _logger.LogCritical("Bad code result from OAuth");
            throw new Exception("Bad code result from OAuth");
        }

        var authCode = parsed.Get("code");

        var formData = new KeyValuePair<string?, string?>[]
        {
            new("grant_type", "authorization_code"),
            new("code", authCode),
            new("client_id", tlogin.ClientID)
        };

        var msg = new HttpRequestMessage();
        msg.Method = HttpMethod.Post;
        msg.RequestUri = tlogin.TokenEndpoint;
        msg.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.159 Safari/537.36");
        msg.Headers.Add("Cookie", string.Join(";", cookies.Select(c => $"{c.Name}={c.Value}")));
        msg.Content = new FormUrlEncodedContent(formData.ToList());

        using var response = await _httpClient.SendAsync(msg, token);
        var data = await response.Content.ReadFromJsonAsync<OAuthResultState>(cancellationToken: token);

        await _tokenProvider.SetToken(new TLoginType
        {
            Cookies = cookies,
            ResultState = data!
        });
        
    }
}
