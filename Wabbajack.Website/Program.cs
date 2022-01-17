using System.Text.Json.Serialization;
using Blazored.LocalStorage;
using Blazored.LocalStorage.JsonConverters;
using Blazored.LocalStorage.Serialization;
using Blazored.LocalStorage.StorageOptions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Wabbajack.DTOs.JsonConverters;
using Wabbajack.DTOs.Logins;
using Wabbajack.Networking.Http.Interfaces;
using Wabbajack.Networking.WabbajackClientApi;
using Wabbajack.RateLimiter;
using Wabbajack.VFS;
using Wabbajack.Website;
using Wabbajack.Website.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var services = builder.Services;

services.AddSingleton(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

services.AddScoped<Configuration>();
services.AddScoped<Client>();
services.AddScoped<ITokenProvider<WabbajackApiState>, WabbackApiTokenProvider>();
services.AddBlazoredLocalStorage();
services.AddDTOConverters();
services.AddDTOSerializer();
services.AddSingleton<IResource<HttpClient>>(new Resource<HttpClient>("Web Requests", 4));
services.AddSingleton<IResource<FileHashCache>>(new Resource<FileHashCache>("File Hashing", 4));
services.AddSingleton<FileHashCache>();

await builder.Build().RunAsync();
