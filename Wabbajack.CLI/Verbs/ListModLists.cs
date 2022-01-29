using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Wabbajack.DTOs;
using Wabbajack.Paths;
using Wabbajack.Networking.WabbajackClientApi;

namespace Wabbajack.CLI.Verbs;

public class ListModLists : IVerb
{
    private readonly Client _wjClient;

    public ListModLists(Client wjClient)
    {
        _wjClient = wjClient;
    }

    public Command MakeCommand()
    {
        var command = new Command("list-modlists");
        command.Add(new Option<AbsolutePath>(new[] {"-o", "-output"}, "Output file"));
        command.Add(new Option<String>(new[] {"-g", "--game"}, "Filter by game (see command \"list-games\")"));
        command.Add(new Option<bool>(new[] {"-v", "--verbose"}, "Show download sizes and other statistics"));
        command.Description = "Lists all modlists";
        command.Handler = CommandHandler.Create(Run);
        return command;
    }

    private async Task<int> Run(AbsolutePath output, String game, bool verbose/*, bool onlyInstalledGames, String tags*/)
    {
        var modListsInput = await _wjClient.LoadLists();
        List<ModlistMetadata> modLists = new List<ModlistMetadata>();
        modLists = modListsInput.ToList();
        
        // Filters
        if (game != null && game != string.Empty) modLists = modLists.FindAll(x => x.Game.ToString().Contains(game, StringComparison.OrdinalIgnoreCase)).ToList();

        if (modLists.Count() == 0)
        {
            Console.WriteLine("No game was found with the name: " + game);
            return 1;
        }
        var maxLengthGame = modLists.ToList().OrderByDescending(x => x.Game.ToString().Count()).First().Game.ToString().Count();
        var maxLengthTitle = modLists.ToList().OrderByDescending(x => x.Title.ToString().Count()).First().Title.ToString().Count();

        foreach (var modList in modLists.ToList().OrderBy(x => x.Game.ToString()).ThenBy(x => x.Title)){
            if (modList.ForceDown) Console.ForegroundColor = ConsoleColor.Red;
            string nsfw = "";
            if (modList.NSFW) nsfw = " (NSFW) ";
            Console.WriteLine(modList.Game.ToString().PadRight(maxLengthGame + 2) +
                              (modList.Title + nsfw).PadRight(maxLengthTitle + 8) +
                              "Tags: " + string.Join(", ", modList.Tags));
            if (verbose) Console.WriteLine(("ModList: " + FormatBytes(modList.DownloadMetadata!.Size)).PadRight(maxLengthGame + 2) + "Unpacked Size: " + FormatBytes(modList.DownloadMetadata.SizeOfInstalledFiles) + "\n");
            if (modList.ForceDown) Console.ResetColor();
        }
        
        return 0;
    }

    /// <summary>
    /// Format bytes to a greater unit
    /// </summary>
    /// <param name="bytes">number of bytes</param>
    /// <returns></returns>
    public static string FormatBytes(long bytes)
    {
        string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
        int i;
        double dblSByte = bytes;
        for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }

        return String.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
    }
}