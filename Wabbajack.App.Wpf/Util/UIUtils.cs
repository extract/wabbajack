using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Wabbajack.Paths;
using Wabbajack.Paths.IO;

namespace Wabbajack
{
    public static class UIUtils
    {
        public static BitmapImage BitmapImageFromResource(string name) => BitmapImageFromStream(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Wabbajack;component/" + name)).Stream);

        public static BitmapImage BitmapImageFromStream(Stream stream)
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = stream;
            img.EndInit();
            img.Freeze();
            return img;
        }

        public static bool TryGetBitmapImageFromFile(AbsolutePath path, out BitmapImage bitmapImage)
        {
            try
            {
                if (!path.FileExists())
                {
                    bitmapImage = default;
                    return false;
                }
                bitmapImage = new BitmapImage(new Uri(path.ToString(), UriKind.RelativeOrAbsolute));
                return true;
            }
            catch (Exception)
            {
                bitmapImage = default;
                return false;
            }
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
        
        public static void OpenWebsite(Uri url)
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c start {url}")
            {
                CreateNoWindow = true,
            });
        }
    }
}
