using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wabbajack.Downloaders;
using Wabbajack.DTOs;
using Wabbajack.Installer;
using Wabbajack.Paths;
using Wabbajack.Paths.IO;
using Wabbajack.Networking.WabbajackClientApi;
using Wabbajack.Hashing.xxHash64;
using Wabbajack.CLI.Services;
using Wabbajack.Downloaders.GameFile;
using Wabbajack.DTOs.JsonConverters;
using Wabbajack.Services.OSIntegrated;

namespace Wabbajack.CLI.Verbs;
public enum InstallState
{
    Configuration,
    Installing,
    Success,
    Failure
}
public class InstallModList : IVerb
{
    private readonly Client _wjClient;
    private readonly DownloadDispatcher _dispatcher;
    private readonly ILogger<ValidateLists> _logger;
    private readonly DTOSerializer _dtos;
    private readonly SettingsManager _settingsManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly IGameLocator _gameLocator;
    private readonly Wabbajack.Services.OSIntegrated.Configuration _configuration;
    private readonly Wabbajack.Installer.SystemParameters _systemParameters;
    private ModList _modList;
    private ModlistMetadata _modListMetadata;
    private AbsolutePath _modListLocation;
    private AbsolutePath _installationDir;
    private AbsolutePath _downloadDir;
    private AbsolutePath _gameDir;

    public InstallModList(ILogger<ValidateLists> logger, Client wjClient, DownloadDispatcher dispatcher,
                          DTOSerializer dtos, SettingsManager settingsManager, IServiceProvider serviceProvider,
                          IGameLocator gameLocator, Wabbajack.Services.OSIntegrated.Configuration configuration)
    {
        _logger = logger;
        _wjClient = wjClient;
        _dispatcher = dispatcher;
        _dtos = dtos;
        _settingsManager = settingsManager;
        _serviceProvider = serviceProvider;
        _gameLocator = gameLocator;
        _configuration = configuration;
    }

    public Command MakeCommand()
    {
        var command = new Command("install-modlist");
        command.Add(new Option<AbsolutePath>(new[] { "-f", "--input" }, "Input file") {IsRequired = true});
        command.Add(new Option<AbsolutePath>(new[] { "-i", "--install_dir" }, "Installation directory") {IsRequired = true});
        command.Add(new Option<AbsolutePath>(new[] { "-d", "--download_dir" }, "Download directory (defaults to installation dir/Downloads)"));
        command.Add(new Option<AbsolutePath>(new[] { "-g", "--game_dir" }, "Game directory (default tries to autofind the game with the registry)"));
        //command.Add(new Option<String>(new[] { "-t", "--title" }, "ModList title to download (run \"list-modlists\" command)"));
        command.Description = "Install a modlist";
        command.Handler = CommandHandler.Create(Run);
        return command;
    }

    private async Task<int> Run(AbsolutePath output, String input, String install_dir, String download_dir, String game_dir)
    {
        
        _installationDir = Path.GetFullPath(install_dir).ToAbsolutePath();
        _downloadDir = download_dir != null ? Path.GetFullPath(download_dir).ToAbsolutePath() : Path.GetFullPath(install_dir).ToAbsolutePath().Combine("/Downloads");
        if (game_dir != null)
        {
            _gameDir = Path.GetFullPath(game_dir).ToAbsolutePath();
        }
        _modListLocation = Path.GetFullPath(input).ToAbsolutePath();
        Console.WriteLine("Installing modlist from: " + _modListLocation);
        Console.WriteLine("Downloads into: " + _downloadDir);
        Console.WriteLine("Installing into: " + _installationDir);

        try
        {
            _modList = await StandardInstaller.LoadFromFile(_dtos, _modListLocation);
            ModlistMetadata[] modlistMetadata = await _wjClient.LoadLists();
            
            // THIS IS A HORRIBLE IDEA :)
            //_modListMetadata = 
            var metaList = modlistMetadata.ToList().FindAll(x => _modList.Name.Contains(x.Title));
            if (metaList.Count() == 0) {
                throw new Exception("No modlist metadata was found for the mod");
            }
            if (metaList.Count() > 1)
            {
                Console.WriteLine("Multiple modlist with similar names were found. Please specify.");
                for (int i = 0; i < metaList.Count(); i++)
                {
                    Console.WriteLine(i + ". " + metaList[i].Title);
                }
                // holy smugly
                string line;
                while ((line = Console.ReadLine()) != null) {}
                _modListMetadata = metaList[Int32.Parse(line)];
            }
            else
            {
                _modListMetadata = metaList.First();
            }            
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "While loading modlist");
            Console.WriteLine("Failed to load modlist...");
            return 1;
        }
        
        Console.WriteLine("Please read through the mods README: " + _modList!.Readme);
        Console.WriteLine("Now starting the installation");
        
        await BeginInstall();
        return 0;
    }

    private async Task<int> BeginInstall() {
        try
        {
            var installer = StandardInstaller.Create(_serviceProvider, new InstallerConfiguration
            {
                Game = _modList.GameType,
                Downloads = _downloadDir,
                Install = _installationDir,
                ModList = _modList,
                ModlistArchive = _modListLocation,
                GameFolder = _gameDir // TODO: This needs to be changed.
            });
            installer.OnStatusUpdate = update =>
            {
                Console.WriteLine("Status: " + update.StatusText);
                Console.WriteLine("Procent: " + update.StepsProgress);
            };
            await installer.Begin(CancellationToken.None);
            Console.WriteLine("Finished the installation of the modlist");
            Console.WriteLine("IMPORTANT: READ THE README AT " + _modList!.Readme);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "While installing modlist");
        }

        return 0;
    }

    private async Task<Hash> DownloadWabbajackFile(ModlistMetadata modList, ArchiveManager archiveManager,
        CancellationToken token, AbsolutePath outputPath)
    {
        var state = _dispatcher.Parse(new Uri(modList.Links.Download));
        if (state == null)
            _logger.LogCritical("Can't download {url}", modList.Links.Download);

        var archive = new Archive
        {
            State = state!,
            Size = modList.DownloadMetadata!.Size,
            Hash = modList.DownloadMetadata.Hash
        };

        _logger.LogInformation("Downloading {primaryKeyString}", state.PrimaryKeyString);
        var hash = await _dispatcher.Download(archive, outputPath.Combine(".tmp"), token);

        if (hash != modList.DownloadMetadata.Hash)
        {
            _logger.LogCritical("Downloaded modlist was {actual} expected {expected}", hash,
                modList.DownloadMetadata.Hash);
            throw new Exception();
        }

        _logger.LogInformation("Successfully downloaded {Title} with {hash}", modList.Title, hash);
        await outputPath.Combine(".tmp").MoveToAsync(outputPath, true, token);
        Console.WriteLine("File downloaded to " + outputPath);
        //await archiveManager.Ingest(outputPath, token);
        return hash;
    }
}