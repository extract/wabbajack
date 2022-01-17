using Wabbajack.Downloaders.Interfaces;
using Wabbajack.DTOs;
using Wabbajack.DTOs.DownloadStates;
using Wabbajack.DTOs.Validation;
using Wabbajack.Hashing.xxHash64;
using Wabbajack.Paths;
using Wabbajack.Paths.IO;
using Wabbajack.RateLimiter;
using Wabbajack.VFS;

namespace Wabbajack.Downloaders.GameFile;

public class GameFileDownloader : ADownloader<GameFileSource>
{
    private readonly FileHashCache _hashCache;
    private readonly IGameLocator _locator;

    public GameFileDownloader(IGameLocator locator, FileHashCache hashCache)
    {
        _locator = locator;
        _hashCache = hashCache;
    }

    public override Priority Priority => Priority.Normal;

    public override async Task<Hash> Download(Archive archive, GameFileSource state, AbsolutePath destination, IJob job,
        CancellationToken token)
    {
        var gamePath = _locator.GameLocation(state.Game);
        var filePath = gamePath.Combine(state.GameFile);
        if (!filePath.FileExists() || await _hashCache.FileHashCachedAsync(filePath, token) != state.Hash)
            throw new FileNotFoundException("Can't download file, doesn't exist or hash is incorrect.");
        
        await filePath.CopyToAsync(destination, true, token);
        return state.Hash;
    }

    public override Task<bool> Prepare()
    {
        return Task.FromResult(true);
    }

    public override bool IsAllowed(ServerAllowList allowList, IDownloadState state)
    {
        return true;
    }

    public override IDownloadState? Resolve(IReadOnlyDictionary<string, string> iniData)
    {
        if (!iniData.TryGetValue("gameName", out var gameName) || !iniData.TryGetValue("gameFile", out var gameFile) ||
            !GameRegistry.TryGetByFuzzyName(gameName, out var game)) return null;

        return new GameFileSource
        {
            Game = game.Game,
            GameFile = gameFile.ToRelativePath()
        };
    }

    public override async Task<bool> Verify(Archive archive, GameFileSource archiveState, IJob job,
        CancellationToken token)
    {
        var fp = archiveState.GameFile.RelativeTo(_locator.GameLocation(archiveState.Game));
        if (!fp.FileExists()) return false;
        return await _hashCache.FileHashCachedAsync(fp, token) == archive.Hash;
    }

    public override IEnumerable<string> MetaIni(Archive a, GameFileSource state)
    {
        return new[]
        {
            $"gameName={state.Game}",
            $"gameFile={state.GameFile}"
        };
    }
}