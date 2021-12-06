using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wabbajack.Hashing.xxHash64;
using Wabbajack.Paths;
using Wabbajack.Paths.IO;
using Wabbajack.RateLimiter;
using Wabbajack.Services.OSIntegrated;
using Wabbajack.VFS;

namespace Wabbajack.App.Wpf.Support;

public class ImageCache
{
    private readonly Configuration _configuration;
    private readonly HttpClient _client;
    private readonly IResource<HttpClient> _limiter;
    private readonly FileHashCache _hashCache;

    public ImageCache(Configuration configuration, HttpClient client, IResource<HttpClient> limiter, FileHashCache hashCache)
    {
        _configuration = configuration;
        _configuration.ImageCacheLocation.CreateDirectory();
        _client = client;
        _limiter = limiter;
        _hashCache = hashCache;
    }

    public async Task<BitmapSource> From(Uri uri, int width, int height)
    {
        var hash = (await Encoding.UTF8.GetBytes(uri.ToString()).Hash()).ToHex();
        var file = _configuration.ImageCacheLocation.Combine(hash);
        
        if (!file.FileExists())
        {
            using var job = await _limiter.Begin("Loading Image", 0, CancellationToken.None);
            
            var wdata = await _client.GetByteArrayAsync(uri);
            await file.WriteAllBytesAsync(wdata);
            return new BitmapImage(new Uri(file.ToString()));
        }
        return new BitmapImage(new Uri(file.ToString()));
    }
    /*
    public async Task<IBitmap> From(AbsolutePath image, int width, int height)
    {
        var hash = await _hashCache.FileHashCachedAsync(image, CancellationToken.None);
        var file = _configuration.ImageCacheLocation.Combine(hash + $"_{width}_{height}");
        
        if (!file.FileExists())
        {
            var resized = SKBitmap.Decode(image.ToString()).Resize(new SKSizeI(width, height), SKFilterQuality.High);
            await file.WriteAllBytesAsync(resized.Encode(SKEncodedImageFormat.Webp, 90).ToArray());
        }
        
        var data = await file.ReadAllBytesAsync();
        return new Bitmap(new MemoryStream(data));
    }*/
}