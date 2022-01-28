using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wabbajack.Downloaders;
using Wabbajack.DTOs;
using Wabbajack.DTOs.JsonConverters;
using Wabbajack.Paths;
using Wabbajack.Paths.IO;
using Wabbajack.Networking.WabbajackClientApi;
using Wabbajack.Hashing.xxHash64;
using Wabbajack.CLI.Services;
using Wabbajack.RateLimiter;
using Wabbajack.Installer;
using Wabbajack.Common;
using System.Text.Json;

namespace Wabbajack.CLI.Verbs;

public class SystemConfig : IVerb
{
    private readonly InstallerConfiguration _installerConfiguration;
    private readonly ILogger<ValidateLists> _logger;

    public SystemConfig(Client wjClient, InstallerConfiguration config, ILogger<ValidateLists> logger)
    {
        _logger = logger;
        _installerConfiguration = config;
    }

    public Command MakeCommand()
    {
        var command = new Command("system-config");
        /*command.Add(new Option<AbsolutePath>(new[] {"-o", "-output"}, "Output file (defaults to current directory)"));
        command.Add(new Option<String>(new[] {"-t", "--title"}, "ModList title to download (run \"list-modlists\" command)"));*/
        command.Description = "Setup your configuration";
        command.Handler = CommandHandler.Create(Run);
        return command;
    }

    private async Task<int> Run(AbsolutePath output, String title/*, bool onlyInstalledGames, String tags*/)
    {
        // This can _not_ be done automatically. It has to be configured by hand.
        Console.WriteLine("Enter your primary/gameplay screen width:");
        var screenWidth = Int32.Parse(Console.ReadLine()!.Trim());
        
        Console.WriteLine("Enter your primary/gameplay screen height:");
        var screenHeight = Int32.Parse(Console.ReadLine()!.Trim());
        
        Console.WriteLine("GPU memory size in MB: (E.g., 4096 for 4GB) (4 * 1024 MB)");
        var videoMemorySize = Int32.Parse(Console.ReadLine()!.Trim());

        Console.WriteLine("RAM memory size in MB: (E.g., 16384 for 16GB) (16 * 1024 MB)");
        var systemMemorySize = Int32.Parse(Console.ReadLine()!.Trim());

        Console.WriteLine("Page/SWAP memory size in MB: (E.g., 16384 for 16GB) (16 * 1024 MB)");
        var swapMemorySize = Int32.Parse(Console.ReadLine()!.Trim());

        var systemParams = new SystemParameters
        {
            ScreenWidth = screenWidth,
            ScreenHeight = screenHeight,
            VideoMemorySize = (long)(videoMemorySize * 1024 * 1024),
            SystemMemorySize = (long)(systemMemorySize * 1024 * 1024),
            SystemPageSize = (long)(swapMemorySize * 1024 * 1024)
        };

        var serializedOptions = JsonSerializer.SerializeToDocument<SystemParameters>(systemParams);

        File.Delete(KnownFolders.CurrentDirectory.Combine("system_parameters.json").ToString());

        var writeStream = File.OpenWrite(KnownFolders.CurrentDirectory.Combine("system_parameters.json").ToString());
        
        Utf8JsonWriter writer = new Utf8JsonWriter(writeStream);
        serializedOptions.WriteTo(writer);
        await writer.FlushAsync(CancellationToken.None);

        writeStream.Close();
        return 0;
    }
}
