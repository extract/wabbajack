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
using Wabbajack.Paths;
using Wabbajack.Paths.IO;
using Wabbajack.Networking.WabbajackClientApi;
using Wabbajack.Hashing.xxHash64;
using Wabbajack.CLI.Services;
using Wabbajack.RateLimiter;

namespace Wabbajack.CLI.Verbs;

public class DownloadModList : IVerb
{
    private readonly Client _wjClient;
    private readonly DownloadDispatcher _dispatcher;
    private readonly ILogger<ValidateLists> _logger;
    private readonly IResource[] _resources;

    public DownloadModList(Client wjClient, DownloadDispatcher dispatcher, ILogger<ValidateLists> logger,
                           IEnumerable<IResource> resources)
    {
        _logger = logger;
        _wjClient = wjClient;
        _dispatcher = dispatcher;
        _resources = resources.ToArray();
    }

    public Command MakeCommand()
    {
        var command = new Command("download-modlist");
        command.Add(new Option<AbsolutePath>(new[] {"-o", "-output"}, "Output file (defaults to current directory)"));
        command.Add(new Option<String>(new[] {"-t", "--title"}, "ModList title to download (run \"list-modlists\" command)"));
        command.Description = "Downloads a modlist";
        command.Handler = CommandHandler.Create(Run);
        return command;
    }

    private async Task<int> Run(AbsolutePath output, String title/*, bool onlyInstalledGames, String tags*/)
    {
        ArchiveManager archiveManager = null;//new ArchiveManager(_logger, output.Combine(".archivemanager"));
        var token = CancellationToken.None;
        var modListsInput = await _wjClient.LoadLists();
        List<ModlistMetadata> modLists = new List<ModlistMetadata>();
        modLists = modListsInput.ToList();
        List<ModlistMetadata> modList;
        // Filters
        //if (game != null && game != string.Empty) modLists = modLists.FindAll(x => x.Game.ToString().Contains(game, StringComparison.OrdinalIgnoreCase)).ToList();
        try {
            modList = modLists.FindAll(x => x.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
            if (modList == null) throw new Exception();
            if (modList.Count() != 1) throw new Exception();
        } catch {
            Console.WriteLine("Number of mod list(s) found were not equal to exactly 1 with the name: " + title + "... Run list-modlists to find the correct title");
            return 1;
        }
        var Metadata = modList.First();
        if (string.IsNullOrEmpty(output.ToString())) output = (Directory.GetCurrentDirectory() + "/../downloaded/" + Metadata.Links.MachineURL + ".wabbajack").ToAbsolutePath();// no worky KnownFolders.EntryPoint.Parent.Combine("downloaded_mod_lists", Metadata.Links.MachineURL).WithExtension(new Extension(".wabbajack"));
        else output = output.Combine(Metadata.Links.MachineURL + ".wabbajack");
        Console.WriteLine("Path: " + output.ToString());
        // Check hash.
        if (output.FileExists()){
            Console.WriteLine(modList.First().Title + " is already downloaded.");
        }
        //Console.WriteLine("Downloading: " + modList.First().Title + " as " + Metadata.Links.Download + " into ");
        foreach(var resource in _resources) {
            Console.WriteLine("Downloading: " + modList.First().Title + resource.StatusReport);
        }
        await DownloadWabbajackFile(Metadata, archiveManager, token, output);

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

	    var hash = await _dispatcher.Download(archive, outputPath, token);
        if (hash != modList.DownloadMetadata.Hash)
        {
            _logger.LogCritical("Downloaded modlist was {actual} expected {expected}", hash,
                modList.DownloadMetadata.Hash);
            throw new Exception();
        }

        _logger.LogInformation("Successfully downloaded {Title} with {hash}", modList.Title, hash);
        Console.WriteLine("File downloaded to " + outputPath);
        //await archiveManager.Ingest(outputPath, token);
        return hash;
    }
}
