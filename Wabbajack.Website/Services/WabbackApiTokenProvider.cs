using Blazored.LocalStorage;
using Wabbajack.DTOs.Logins;
using Wabbajack.Hashing.xxHash64;
using Wabbajack.Networking.Http.Interfaces;
using Wabbajack.Paths;
using Wabbajack.Paths.IO;

namespace Wabbajack.Website.Services;

public class WabbackApiTokenProvider  : ITokenProvider<WabbajackApiState>
{
    private readonly ILocalStorageService _localStorage;
    private static string MetricsKeyName = "metrics-key";

    public WabbackApiTokenProvider(ILocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    public async ValueTask<WabbajackApiState?> Get()
    {
        if (await _localStorage.ContainKeyAsync(MetricsKeyName))
        {
            return new WabbajackApiState()
            {
                MetricsKey = await _localStorage.GetItemAsync<string>(MetricsKeyName)
            };
        }


        var key = MakeRandomKey();
        await _localStorage.SetItemAsync(MetricsKeyName, key);
        return new WabbajackApiState
        {
            MetricsKey = key
        };

    }

    public ValueTask SetToken(WabbajackApiState val)
    {
        throw new NotImplementedException();
    }

    public ValueTask<bool> Delete()
    {
        throw new NotImplementedException();
    }

    public bool HaveToken()
    {
        return true;
    }
    
    private static string MakeRandomKey()
    {
        var random = new Random();
        var bytes = new byte[32];
        random.NextBytes(bytes);
        return bytes.ToHex();
    }
}