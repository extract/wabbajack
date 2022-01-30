using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wabbajack.Common;
using Wabbajack.Downloaders;
using Wabbajack.DTOs;
using Wabbajack.Installer;
using Wabbajack.Paths;
using Wabbajack.Paths.IO;
using Wabbajack.Networking.WabbajackClientApi;
using Wabbajack.Downloaders.GameFile;
using Wabbajack.DTOs.JsonConverters;
using Wabbajack.Services.OSIntegrated;
using Wabbajack.RateLimiter;
using Wabbajack.DTOs.DownloadStates;

using Timer = System.Timers.Timer;

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
    private readonly Timer _timer;
    private StatusReport[] _prevReport;
    private int _prevWindowWidth;
    private readonly Client _wjClient;
    private readonly DownloadDispatcher _dispatcher;
    private readonly ILogger<ValidateLists> _logger;
    private readonly DTOSerializer _dtos;
    private readonly SettingsManager _settingsManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly IGameLocator _gameLocator;
    private readonly Wabbajack.Services.OSIntegrated.Configuration _configuration;
    private readonly Wabbajack.Installer.SystemParameters _systemParameters;
    private readonly IResource[] _resources;
    private readonly InstallerConfiguration _installerConfiguration;
    private StatusUpdate _statusUpdate;
    private ModList _modList;
    private AbsolutePath _modListLocation;
    private AbsolutePath _installationDir;
    private AbsolutePath _downloadDir;
    private AbsolutePath _gameDir;

    public InstallModList(ILogger<ValidateLists> logger, Client wjClient, DownloadDispatcher dispatcher,
                          DTOSerializer dtos, SettingsManager settingsManager, IServiceProvider serviceProvider,
                          IGameLocator gameLocator, Wabbajack.Services.OSIntegrated.Configuration configuration,
                          IEnumerable<IResource> resources, InstallerConfiguration config)
    {
        _resources = resources.ToArray();
        _logger = logger;
        _wjClient = wjClient;
        _dispatcher = dispatcher;
        _installerConfiguration = config;
        _dtos = dtos;
        _settingsManager = settingsManager;
        _serviceProvider = serviceProvider;
        _gameLocator = gameLocator;
        _configuration = configuration;
        _prevReport = _resources.Select(x => (x.StatusReport)).ToArray();
        _prevWindowWidth = 0;
        _timer = new Timer();
        _timer.Interval = 1000;
        _timer.Elapsed += Elapsed;
    }

    public Command MakeCommand()
    {
        var command = new Command("install-modlist");
        command.Add(new Option<AbsolutePath>(new[] { "-f", "--input" }, "Input file") { IsRequired = true });
        command.Add(new Option<AbsolutePath>(new[] { "-i", "--install_dir" }, "Installation directory") { IsRequired = true });
        command.Add(new Option<AbsolutePath>(new[] { "-d", "--download_dir" }, "Download directory (defaults to installation dir/Downloads)"));
        command.Add(new Option<AbsolutePath>(new[] { "-g", "--game_dir" }, "Game directory (default tries to autofind the game with the registry)"));
        //command.Add(new Option<String>(new[] { "-t", "--title" }, "ModList title to download (run \"list-modlists\" command)"));
        command.Description = "Install a modlist";
        command.Handler = CommandHandler.Create(Run);
        return command;
    }

    private async Task<int> Run(AbsolutePath output, String input, String install_dir, String download_dir, String game_dir)
    {
        LoadSystemParams();
        if (_installerConfiguration.SystemParameters == null)
        {
            Console.WriteLine("You need to set up your system parameters before running this script.\nSee [system-config --help]");
        }
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

    private async Task<int> BeginInstall()
    {
        try
        {

            var installer = StandardInstaller.Create(_serviceProvider, new InstallerConfiguration
            {
                SystemParameters = _installerConfiguration.SystemParameters,
                Game = _modList.GameType,
                Downloads = _downloadDir,
                Install = _installationDir,
                ModList = _modList,
                ModlistArchive = _modListLocation,
                GameFolder = _gameDir // TODO(extract): incorperate GameFinder somehow?
            });

            installer.OnStatusUpdate = update =>
            {
                _statusUpdate = update;
            };

            Console.Write($"{Esc}[{Console.WindowHeight}B{Esc}[2J");
            _timer.Enabled = true;

            var success = await installer.Begin(CancellationToken.None);
            _timer.Enabled = false;
            _timer.Close();
            if (success)
            {
                Console.WriteLine("\nFinished the installation of the modlist");
                Console.WriteLine("IMPORTANT: READ THE README AT " + _modList!.Readme);
            }
            else
            {
                Console.WriteLine("Failed to install the modlist, see output.");
                Console.WriteLine("For more information you can re-run the command and redirect stderr to a file. ( %command% 2>errors.txt )");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "While installing modlist");
        }

        return 0;
    }

    private void Elapsed(object? sender, ElapsedEventArgs e)
    {
        // TODO(extract): This needs to allow errors to show up somehow.
        //                but C# logger has really ugly errors.
        //                Current solution is to log warns and above to stderr
        //                User could redirect this into a file to debug issues?
        Console.Write($"{Esc}[2J{Esc}[H");
        Console.WriteLine(":: Status: " + _statusUpdate.StatusText);

        var report = NextReport();
        foreach (var (prev, next, resource) in _prevReport.Zip(report, _resources))
        {

            var throughput = next.Transferred - prev.Transferred;
            if (throughput > 0)
            {
                Console.WriteLine($"{Esc}[2K:: {resource.Name}: [Running: {next.Running}, Pending: {next.Pending}] {throughput.ToFileSizeString()}/sec.");
                Console.Write($"{Esc}[2K");

                var jobs = resource.Jobs.ToList();
                foreach (var job in jobs.Where(x => x.Current != 0).OrderByDescending(x => Percent.FactoryPutInRange(x.Current, (long)x.Size).Value))
                {
                    string fullName = job.Description;

                    switch (job.Description.Split("+")[0].Split("|")[0])
                    {
                        case "NexusDownloader":
                            fullName = NexusJobFormatter(job.Description);
                            break;
                        default:
                            fullName = DefaultJobFormatter(job.Description);
                            break;
                    }

                    var percent = Percent.FactoryPutInRange(job.Current, (long)job.Size);
                    const int length = 75;
                    var prc = (double)length * percent.Value;

                    var hashtag = new String('#', (int)Math.Round(prc));
                    var dots = new String('.', length - (int)Math.Round(prc));
                    var bar = $"[{hashtag}{dots}] {percent.ToString().PadRight(4)}";
                    var nameToPrint = " " + fullName.Substring(0, Math.Min((int)(Console.WindowWidth - bar.Length - 3), fullName.Length));
                    Console.WriteLine($"{nameToPrint} {bar.PadLeft(Console.WindowWidth - nameToPrint.Length - 2)}");
                    Console.Write($"{Esc}[2K");
                }
            }
        }
        _prevReport = report;
    }

    private string NexusJobFormatter(string description)
    {
        var modId = description.Split('|')[2];
        var fullName = description;

        if ((_modList.Archives.First(x => x.Meta.Contains("modID=" + modId)).State).TypeName.Contains("Nexus"))
        {
            var state = (Nexus)(_modList.Archives.First(x => x.Meta.Contains("modID=" + modId)).State);
            fullName = String.IsNullOrEmpty(state.Name) ? description : state.Name;
        }

        return fullName;
    }

    private string DefaultJobFormatter(string description)
    {
        var fullName = description.Split('|')[1].Split('/').Last();
        return fullName;
    }

    private StatusReport[] NextReport()
    {
        return _resources.Select(r => r.StatusReport).ToArray();
    }

    private void LoadSystemParams()
    {
        try
        {
            _installerConfiguration.SystemParameters = KnownFolders.CurrentDirectory
                                                                   .Combine("system_parameters.json")
                                                                   .Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                                                                   .FromJson<Wabbajack.Installer.SystemParameters>()
                                                                   .Result;
        }
        catch (Exception e)
        {
            Console.WriteLine("File \"system_parameters.json\" could not be found. Run \"system-config\" first.");
        }
    }

    public static string Esc { get; } = "\u001b";
}