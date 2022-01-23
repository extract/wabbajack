using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wabbajack.Downloaders;
using Wabbajack.Paths;
using Wabbajack.DTOs.Logins;
using Wabbajack.Networking.Http.Interfaces;
using Wabbajack.Services.OSIntegrated;
using WebDriverManager;
using OpenQA.Selenium;
using WebDriverManager.DriverConfigs.Impl;
using WebDriverManager.DriverConfigs;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Chromium;
using OpenQA.Selenium.Support.UI;
using System.Collections.Generic;


using Command = System.CommandLine.Command;

namespace Wabbajack.CLI.Verbs;

public class LoginManager : IVerb
{
    private readonly DownloadDispatcher _dispatcher;
    private readonly ILogger<LoginManager> _logger;
    private readonly ITokenProvider<NexusApiState> _nexusToken;
    private readonly ITokenProvider<LoversLabLoginState> _loversLabToken;
    private readonly ITokenProvider<VectorPlexusLoginState> _vectorPlexusLoginToken;
    // Add the rest of the ITokenProviders like Wabbajack API, Steam, OAuth2LoginState

    private WebDriver webDriver;
    private const String LoggedInText = " : Logged in ✅";
    private const String LoggedOutText = " : Not logged in ❌";

    private const String NexusText = "Nexus";
    private const String LoversLabText = "LoversLab";
    private const String VectorPlexusText = "VectorPlexus";
    private int MaxNameLength = VectorPlexusText.Length + 2;
    
    
    public LoginManager(ILogger<LoginManager> logger,
                        ITokenProvider<NexusApiState> nexusToken, 
                        ITokenProvider<VectorPlexusLoginState> vectorPlexusToken,
                        ITokenProvider<LoversLabLoginState> loversLabToken)
    {
        _logger = logger;
        _nexusToken = nexusToken;
        _vectorPlexusLoginToken = vectorPlexusToken;
        _loversLabToken = loversLabToken;
    }

    public Command MakeCommand()
    {
        var command = new Command("login-manager");
        command.Add(new Option<String>(new[] {"-s", "--site"}, "Site to log in to. (Nexus, VectorPlexus, LoversLab)"));
        command.Add(new Option<String>(new[] {"-u", "--username"}, "Username to the site"));
        command.Add(new Option<bool>(new[] {"-l", "--logout"}, "Logout from sites (defaults to all sites)"));
        command.Description = "Manages the logins to sites. No args to print all sites and login state.";
        command.Handler = CommandHandler.Create(Run);
        return command;
    }
    [STAThread]
    private async Task<int> Run(String site, String username, bool logout)
    {
        if (logout)
        {
            if (String.IsNullOrEmpty(site))
            {
                DeleteAllToken();
                Console.WriteLine("All sites have been logged out");
                return 0;
            }
            if (DeleteToken(site))
            {
                Console.WriteLine("Logged out of " + site);
                return 0;
            }
            else
            {
                Console.WriteLine("Failed to log out. Site (" + site + ") was not found.");
                return 1;
            }
        }
        if (String.IsNullOrEmpty(site) && String.IsNullOrEmpty(username))
        {
            Console.WriteLine("Your logins:");
            
            Console.WriteLine(NexusText.PadRight(MaxNameLength)        + (_nexusToken.HaveToken()             ? LoggedInText : LoggedOutText));
            Console.WriteLine(LoversLabText.PadRight(MaxNameLength)    + (_loversLabToken.HaveToken()         ? LoggedInText : LoggedOutText));
            Console.WriteLine(VectorPlexusText.PadRight(MaxNameLength) + (_vectorPlexusLoginToken.HaveToken() ? LoggedInText : LoggedOutText));

            return 0;
        }
        if (!String.IsNullOrEmpty(site))
        {
            try
            {
                await LoginHandler(site, username);
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
            }
        }
        
        return 0;
    }

    private void DeleteAllToken(){
        _nexusToken.Delete();
        _loversLabToken.Delete();
        _vectorPlexusLoginToken.Delete();
    }

    private bool DeleteToken(String site) {
        switch(site){
            case NexusText:
                _nexusToken.Delete();
                break;
            case LoversLabText:
                _loversLabToken.Delete();
                break;
            case VectorPlexusText:
                _vectorPlexusLoginToken.Delete();
                break;
            default:
                return false;
        }
        return true;
    }

    private async Task<int> LoginHandler(String site, String username)
    {
        bool isFirefox = true;
        string? browser = Environment.GetEnvironmentVariable("BROWSER");
        if (!String.IsNullOrEmpty(browser)){
            if (browser.Contains("chrome", StringComparison.OrdinalIgnoreCase)) {
                isFirefox = false;
            }
        }
        switch(site){
            case NexusText:
                if (isFirefox) await LogInNexus<FirefoxConfig, FirefoxDriver>();
                else await LogInNexus<ChromeConfig, ChromeDriver>();
                break;
            /*TODO(extract): Work in progress 
            case LoversLabText:
                Console.WriteLine("AABBBAA LOV");
                await LogInOAuth2(new LoversLabLoginState());
                break;
            case VectorPlexusText:
                Console.WriteLine("VECCOOO LOV");
                await LogInOAuth2(new VectorPlexusLoginState());
                break;*/
            default:
                throw(new Exception("Site invalid"));
        }

        /*if (webDriver == null) {
            throw(new Exception());
        }*/
        
        return 0;
    }

    private async Task LogInNexus<WebConfigType, WebDriverType>()
        where WebConfigType : IDriverConfig, new()
        where WebDriverType : WebDriver, new()
    {
        new DriverManager().SetUpDriver(new WebConfigType());
        webDriver = new WebDriverType();
        
        webDriver.Navigate().GoToUrl("https://users.nexusmods.com/auth/continue?client_id=nexus&redirect_uri=https://www.nexusmods.com/oauth/callback&response_type=code&referrer=//www.nexusmods.com");
        
        WebDriverWait w = new WebDriverWait(webDriver, TimeSpan.FromSeconds(120));
        w.Until(x => x.Manage().Cookies.GetCookieNamed("member_id"));
        var cookies = webDriver.Manage().Cookies.AllCookies.ToList().FindAll(x => x.Domain.Contains("nexusmods.com"));
        
        webDriver.Navigate().GoToUrl("https://www.nexusmods.com/users/myaccount?tab=api");

        string key = "";
        try
        {
            key = webDriver.FindElement(By.XPath("//input[@value='wabbajack']/../.."))
                           .FindElement(By.ClassName("application-key"))
                           .Text;
        }
        catch (Exception)
        {
            // TODO(extract): test this by revoking key.
            webDriver.FindElement(By.XPath("//input[@value='wabbajack']/../.."))
                     .FindElement(By.Name("submit")).Click();

            w.Until(x => x.FindElement(By.XPath("//input[@value='wabbajack']/../.."))
                          .FindElement(By.ClassName("application-key")));

            key = webDriver.FindElement(By.XPath("//input[@value='wabbajack']/../.."))
                           .FindElement(By.ClassName("application-key"))
                           .Text;
        }
        
        if (String.IsNullOrEmpty(key))
        {
            Console.WriteLine("Tried to find the key but could not find it... Paste it here:");
            key = Console.ReadLine() ?? "";
            if (String.IsNullOrEmpty(key)) throw new Exception("Invalid key");
        }

        Wabbajack.DTOs.Logins.Cookie[] cookiesArray = {};

        foreach (var cok in cookies) {
            cookiesArray.Add(new Wabbajack.DTOs.Logins.Cookie () { Name = cok.Name, Domain = cok.Domain, Path = cok.Path, Value = cok.Value });
        }

        await _nexusToken.SetToken(new NexusApiState()
        {
            ApiKey = key,
            Cookies = cookiesArray
        });

        webDriver.Quit();
    }

    private async Task<OAuth2LoginState> LogInOAuth2(OAuth2LoginState loginState)
    {
        //if(loginState == null) return null;
        
        var scopes = string.Join(" ", loginState.Scopes);
        var state = Guid.NewGuid().ToString();

        new DriverManager().SetUpDriver(new ChromeConfig());
        webDriver = new ChromeDriver();
        webDriver.Url = new Uri(loginState.AuthorizationEndpoint + $"?response_type=code&client_id={loginState.ClientID}&state={state}&scope={scopes}").ToString();
        
        Console.WriteLine("Waiting... for " + new Uri(loginState.AuthorizationEndpoint + $"?response_type=code&client_id={loginState.ClientID}&state={state}&scope={scopes}").ToString());
        WebDriverWait w = new WebDriverWait(webDriver, TimeSpan.FromSeconds(60));
        w.Until(x => x.FindElement(By.Id("elSignIn_submit")));


        var cookies = webDriver.Manage().Cookies.GetCookieNamed(loginState.AuthorizationEndpoint.Host);
        Console.WriteLine("Cookies: " + "\nUrl: " + webDriver.Url);

        w = new WebDriverWait(webDriver, TimeSpan.FromSeconds(60));
        w.Until(x => x.FindElement(By.Id("elSignIn_submit")));




        // var formData = new KeyValuePair<string?, string?>[]
        // {
        //     new("grant_type", "authorization_code"),
        //     new("code", authCode),
        //     new("client_id", tlogin.ClientID)
        // };

        // var msg = new HttpRequestMessage();
        // msg.Method = HttpMethod.Post;
        // msg.RequestUri = tlogin.TokenEndpoint;
        // msg.Headers.Add("User-Agent",
        //     "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.159 Safari/537.36");
        // msg.Headers.Add("Cookie", string.Join(";", cookies.Select(c => $"{c.Name}={c.Value}")));
        // msg.Content = new FormUrlEncodedContent(formData.ToList());

        // using var response = await _httpClient.SendAsync(msg, Message.Token);
        // var data = await response.Content.ReadFromJsonAsync<OAuthResultState>(cancellationToken: Message.Token);

        // await _tokenProvider.SetToken(new TLoginType
        // {
        //     Cookies = cookies,
        //     ResultState = data!
        // });

        //webDriver.Quit();
        //webDriver.
        //webDriver.Url = new Uri(loginState.AuthorizationEndpoint + $"?response_type=code&client_id={loginState.ClientID}&state={state}&scope={scopes}").ToString();
/*
        await NavigateTo(new Uri(tlogin.AuthorizationEndpoint + $"?response_type=code&client_id={tlogin.ClientID}&state={state}&scope={scopes}"));

        var uri = await handler.Task.WaitAsync(Message.Token);

        var cookies = await Driver.GetCookies(tlogin.AuthorizationEndpoint.Host);

        var parsed = HttpUtility.ParseQueryString(uri.Query);
        if (parsed.Get("state") != state)
        {
            Logger.LogCritical("Bad OAuth state, this shouldn't happen");
            throw new Exception("Bad OAuth State");
        }

        if (parsed.Get("code") == null)
        {
            Logger.LogCritical("Bad code result from OAuth");
            throw new Exception("Bad code result from OAuth");
        }

        var authCode = parsed.Get("code");

        */
        return loginState;
    }
}