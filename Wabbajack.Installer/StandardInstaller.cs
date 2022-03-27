using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IniParser;
using IniParser.Model.Configuration;
using IniParser.Parser;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wabbajack.Common;
using Wabbajack.Compression.BSA;
using Wabbajack.Compression.Zip;
using Wabbajack.Downloaders;
using Wabbajack.Downloaders.GameFile;
using Wabbajack.DTOs;
using Wabbajack.DTOs.Directives;
using Wabbajack.DTOs.DownloadStates;
using Wabbajack.DTOs.JsonConverters;
using Wabbajack.Installer.Utilities;
using Wabbajack.Networking.WabbajackClientApi;
using Wabbajack.Paths;
using Wabbajack.Paths.IO;
using Wabbajack.VFS;

namespace Wabbajack.Installer;

public class StandardInstaller : AInstaller<StandardInstaller>
{
    public static RelativePath BSACreationDir = "TEMP_BSA_FILES".ToRelativePath();

    public StandardInstaller(ILogger<StandardInstaller> logger,
        InstallerConfiguration config,
        IGameLocator gameLocator, FileExtractor.FileExtractor extractor,
        DTOSerializer jsonSerializer, Context vfs, FileHashCache fileHashCache,
        DownloadDispatcher downloadDispatcher, ParallelOptions parallelOptions, Client wjClient) :
        base(logger, config, gameLocator, extractor, jsonSerializer, vfs, fileHashCache, downloadDispatcher,
            parallelOptions, wjClient)
    {
        MaxSteps = 14;
    }

    public static StandardInstaller Create(IServiceProvider provider, InstallerConfiguration configuration)
    {
        return new StandardInstaller(provider.GetRequiredService<ILogger<StandardInstaller>>(),
            configuration,
            provider.GetRequiredService<IGameLocator>(),
            provider.GetRequiredService<FileExtractor.FileExtractor>(),
            provider.GetRequiredService<DTOSerializer>(),
            provider.GetRequiredService<Context>(),
            provider.GetRequiredService<FileHashCache>(),
            provider.GetRequiredService<DownloadDispatcher>(),
            provider.GetRequiredService<ParallelOptions>(),
            provider.GetRequiredService<Client>());
    }

    public override async Task<bool> Begin(CancellationToken token)
    {
        if (token.IsCancellationRequested) return false;
        await _wjClient.SendMetric(MetricNames.BeginInstall, ModList.Name);
        NextStep(Consts.StepPreparing, "Configuring Installer", 0);
        _logger.LogInformation("Configuring Processor");

        if (_configuration.GameFolder == default)
            _configuration.GameFolder = _gameLocator.GameLocation(_configuration.Game);

        if (_configuration.GameFolder == default)
        {
            var otherGame = _configuration.Game.MetaData().CommonlyConfusedWith
                .Where(g => _gameLocator.IsInstalled(g)).Select(g => g.MetaData()).FirstOrDefault();
            if (otherGame != null)
                _logger.LogError(
                    "In order to do a proper install Wabbajack needs to know where your {lookingFor} folder resides. However this game doesn't seem to be installed, we did however find an installed " +
                    "copy of {otherGame}, did you install the wrong game?",
                    _configuration.Game.MetaData().HumanFriendlyGameName, otherGame.HumanFriendlyGameName);
            else
                _logger.LogError(
                    "In order to do a proper install Wabbajack needs to know where your {lookingFor} folder resides. However this game doesn't seem to be installed.",
                    _configuration.Game.MetaData().HumanFriendlyGameName);

            return false;
        }

        if (!_configuration.GameFolder.DirectoryExists())
        {
            _logger.LogError("Located game {game} at \"{gameFolder}\" but the folder does not exist!",
                _configuration.Game, _configuration.GameFolder);
            return false;
        }


        _logger.LogInformation("Install Folder: {InstallFolder}", _configuration.Install);
        _logger.LogInformation("Downloads Folder: {DownloadFolder}", _configuration.Downloads);
        _logger.LogInformation("Game Folder: {GameFolder}", _configuration.GameFolder);
        _logger.LogInformation("Wabbajack Folder: {WabbajackFolder}", KnownFolders.EntryPoint);

        _configuration.Install.CreateDirectory();
        _configuration.Downloads.CreateDirectory();

        await OptimizeModlist(token);
        
        await HashArchives(token);

        await DownloadArchives(token);

        await HashArchives(token);

        var missing = ModList.Archives.Where(a => !HashedArchives.ContainsKey(a.Hash)).ToList();
        if (missing.Count > 0)
        {
            foreach (var a in missing)
                _logger.LogCritical("Unable to download {name} ({primaryKeyString})", a.Name,
                    a.State.PrimaryKeyString);
            _logger.LogCritical("Cannot continue, was unable to download one or more archives");
            return false;
        }

        await ExtractModlist(token);

        await PrimeVFS();

        await BuildFolderStructure();

        await InstallArchives(token);

        await InstallIncludedFiles(token);

        await InstallIncludedDownloadMetas(token);

        await BuildBSAs(token);

        // TODO: Port this
        await GenerateZEditMerges(token);

        await ForcePortable();
        await RemapMO2File();

        CreateOutputMods();

        SetScreenSizeInPrefs();

        await ExtractedModlistFolder!.DisposeAsync();
        await _wjClient.SendMetric(MetricNames.FinishInstall, ModList.Name);

        NextStep(Consts.StepFinished, "Finished", 1);
        _logger.LogInformation("Finished Installation");
        return true;
    }

    private async Task RemapMO2File()
    {
        var iniFile = _configuration.Install.Combine("ModOrganizer.ini");
        if (!iniFile.FileExists()) return;

        _logger.LogInformation("Remapping ModOrganizer.ini");

        var iniData = iniFile.LoadIniFile();
        var settings = iniData["Settings"];
        settings["download_directory"] = _configuration.Downloads.ToString();
        iniData.SaveIniFile(iniFile);
    }

    private void CreateOutputMods()
    {
        _configuration.Install.Combine("profiles")
            .EnumerateFiles()
            .Where(f => f.FileName == Consts.SettingsIni)
            .Do(f =>
            {
                if (!f.FileExists())
                {
                    _logger.LogInformation("settings.ini is null for {profile}, skipping", f);
                    return;
                }

                var ini = f.LoadIniFile();

                var overwrites = ini["custom_overrides"];
                if (overwrites == null)
                {
                    _logger.LogInformation("No custom overwrites found, skipping");
                    return;
                }

                overwrites!.Do(keyData =>
                {
                    var v = keyData.Value;
                    var mod = _configuration.Install.Combine(Consts.MO2ModFolderName, (RelativePath) v);

                    mod.CreateDirectory();
                });
            });
    }

    private async Task ForcePortable()
    {
        var path = _configuration.Install.Combine("portable.txt");
        if (path.FileExists()) return;

        try
        {
            await path.WriteAllTextAsync("Created by Wabbajack");
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, "Could not create portable.txt in {_configuration.Install}",
                _configuration.Install);
        }
    }

    private async Task InstallIncludedDownloadMetas(CancellationToken token)
    {
        await ModList.Archives
            .PDoAll(async archive =>
            {
                if (HashedArchives.TryGetValue(archive.Hash, out var paths))
                {
                    var metaPath = paths.WithExtension(Ext.Meta);
                    if (!metaPath.FileExists() && archive.State is not GameFileSource)
                    {
                        var meta = AddInstalled(_downloadDispatcher.MetaIni(archive));
                        await metaPath.WriteAllLinesAsync(meta, token);
                    }
                }
            });
    }

    private IEnumerable<string> AddInstalled(IEnumerable<string> getMetaIni)
    {
        foreach (var f in getMetaIni)
        {
            yield return f;
            if (f == "[General]") yield return "installed=true";
        }
    }

    private async Task BuildBSAs(CancellationToken token)
    {
        var bsas = ModList.Directives.OfType<CreateBSA>().ToList();
        _logger.LogInformation("Building {bsasCount} bsa files", bsas.Count);

        foreach (var bsa in bsas)
        {
            _logger.LogInformation("Building {bsaTo}", bsa.To.FileName);
            var sourceDir = _configuration.Install.Combine(BSACreationDir, bsa.TempID);

            var a = BSADispatch.CreateBuilder(bsa.State, _manager);
            var streams = await bsa.FileStates.PMapAll(async state =>
            {
                var fs = sourceDir.Combine(state.Path).Open(FileMode.Open, FileAccess.Read, FileShare.Read);
                await a.AddFile(state, fs, token);
                return fs;
            }).ToList();

            _logger.LogInformation("Writing {bsaTo}", bsa.To);
            await using var outStream = _configuration.Install.Combine(bsa.To)
                .Open(FileMode.Create, FileAccess.Write, FileShare.None);
            await a.Build(outStream, token);
            streams.Do(s => s.Dispose());

            sourceDir.DeleteDirectory();
        }

        var bsaDir = _configuration.Install.Combine(BSACreationDir);
        if (bsaDir.DirectoryExists())
        {
            _logger.LogInformation("Removing temp folder {bsaCreationDir}", BSACreationDir);
            bsaDir.DeleteDirectory();
        }
    }

    private async Task InstallIncludedFiles(CancellationToken token)
    {
        _logger.LogInformation("Writing inline files");
        NextStep(Consts.StepInstalling, "Installing Included Files", ModList.Directives.OfType<InlineFile>().Count());
        await ModList.Directives
            .OfType<InlineFile>()
            .PDoAll(async directive =>
            {
                UpdateProgress(1);
                var outPath = _configuration.Install.Combine(directive.To);
                outPath.Delete();

                switch (directive)
                {
                    case RemappedInlineFile file:
                        await WriteRemappedFile(file);
                        break;
                    default:
                        await outPath.WriteAllBytesAsync(await LoadBytesFromPath(directive.SourceDataID), token);
                        break;
                }
            });
    }

    private void SetScreenSizeInPrefs()
    {
        if (_configuration.SystemParameters == null)
            _logger.LogWarning("No SystemParameters set, ignoring ini settings for system parameters");

        var config = new IniParserConfiguration {AllowDuplicateKeys = true, AllowDuplicateSections = true};
        config.CommentRegex = new Regex(@"^(#|;)(.*)");
        var oblivionPath = (RelativePath) "Oblivion.ini";
        foreach (var file in _configuration.Install.Combine("profiles").EnumerateFiles()
            .Where(f => ((string) f.FileName).EndsWith("refs.ini") || f.FileName == oblivionPath))
            try
            {
                var parser = new FileIniDataParser(new IniDataParser(config));
                var data = parser.ReadFile(file.ToString());
                var modified = false;
                if (data.Sections["Display"] != null)
                    if (data.Sections["Display"]["iSize W"] != null && data.Sections["Display"]["iSize H"] != null)
                    {
                        data.Sections["Display"]["iSize W"] =
                            _configuration.SystemParameters.ScreenWidth.ToString(CultureInfo.CurrentCulture);
                        data.Sections["Display"]["iSize H"] =
                            _configuration.SystemParameters.ScreenHeight.ToString(CultureInfo.CurrentCulture);
                        modified = true;
                    }

                if (data.Sections["MEMORY"] != null)
                    if (data.Sections["MEMORY"]["VideoMemorySizeMb"] != null)
                    {
                        data.Sections["MEMORY"]["VideoMemorySizeMb"] =
                            _configuration.SystemParameters.EnbLEVRAMSize.ToString(CultureInfo.CurrentCulture);
                        modified = true;
                    }

                if (!modified) continue;
                parser.WriteFile(file.ToString(), data);
                _logger.LogTrace("Remapped screen size in {file}", file);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Skipping screen size remap for {file} due to parse error.", file);
            }

        var tweaksPath = (RelativePath) "SSEDisplayTweaks.ini";
        foreach (var file in _configuration.Install.EnumerateFiles()
            .Where(f => f.FileName == tweaksPath))
            try
            {
                var parser = new FileIniDataParser(new IniDataParser(config));
                var data = parser.ReadFile(file.ToString());
                var modified = false;
                if (data.Sections["Render"] != null)
                    if (data.Sections["Render"]["Resolution"] != null)
                    {
                        data.Sections["Render"]["Resolution"] =
                            $"{_configuration.SystemParameters.ScreenWidth.ToString(CultureInfo.CurrentCulture)}x{_configuration.SystemParameters.ScreenHeight.ToString(CultureInfo.CurrentCulture)}";
                        modified = true;
                    }

                if (modified)
                    parser.WriteFile(file.ToString(), data);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Skipping screen size remap for {file} due to parse error.", file);
            }
    }

    private async Task WriteRemappedFile(RemappedInlineFile directive)
    {
        var data = Encoding.UTF8.GetString(await LoadBytesFromPath(directive.SourceDataID));

        var gameFolder = _configuration.GameFolder.ToString();

        data = data.Replace(Consts.GAME_PATH_MAGIC_BACK, gameFolder);
        data = data.Replace(Consts.GAME_PATH_MAGIC_DOUBLE_BACK, gameFolder.Replace("\\", "\\\\"));
        data = data.Replace(Consts.GAME_PATH_MAGIC_FORWARD, gameFolder.Replace("\\", "/"));

        data = data.Replace(Consts.MO2_PATH_MAGIC_BACK, _configuration.Install.ToString());
        data = data.Replace(Consts.MO2_PATH_MAGIC_DOUBLE_BACK,
            _configuration.Install.ToString().Replace("\\", "\\\\"));
        data = data.Replace(Consts.MO2_PATH_MAGIC_FORWARD, _configuration.Install.ToString().Replace("\\", "/"));

        data = data.Replace(Consts.DOWNLOAD_PATH_MAGIC_BACK, _configuration.Downloads.ToString());
        data = data.Replace(Consts.DOWNLOAD_PATH_MAGIC_DOUBLE_BACK,
            _configuration.Downloads.ToString().Replace("\\", "\\\\"));
        data = data.Replace(Consts.DOWNLOAD_PATH_MAGIC_FORWARD,
            _configuration.Downloads.ToString().Replace("\\", "/"));

        await _configuration.Install.Combine(directive.To).WriteAllTextAsync(data);
    }

    public async Task GenerateZEditMerges(CancellationToken token)
    {
        await _configuration.ModList
            .Directives
            .OfType<MergedPatch>()
            .PDoAll(async m =>
            {
                _logger.LogInformation("Generating zEdit merge: {to}", m.To);

                var srcData = (await m.Sources.SelectAsync(async s =>
                            await _configuration.Install.Combine(s.RelativePath).ReadAllBytesAsync(token))
                        .ToReadOnlyCollection())
                    .ConcatArrays();

                var patchData = await LoadBytesFromPath(m.PatchID);

                await using var fs = _configuration.Install.Combine(m.To)
                    .Open(FileMode.Create, FileAccess.Write, FileShare.None);
                await BinaryPatching.ApplyPatch(new MemoryStream(srcData), new MemoryStream(patchData), fs);
            });
    }

    public static async Task<ModList> Load(DTOSerializer dtos, DownloadDispatcher dispatcher, ModlistMetadata metadata, CancellationToken token)
    {
        var archive = new Archive
        {
            State = dispatcher.Parse(new Uri(metadata.Links.Download))!,
            Size = metadata.DownloadMetadata!.Size,
            Hash = metadata.DownloadMetadata.Hash
        };

        var stream = await dispatcher.ChunkedSeekableStream(archive, token);
        await using var reader = new ZipReader(stream);
        var entry = (await reader.GetFiles()).First(e => e.FileName == "modlist");
        var ms = new MemoryStream();
        await reader.Extract(entry, ms, token);
        ms.Position = 0;
        return JsonSerializer.Deserialize<ModList>(ms, dtos.Options)!;
    }
}