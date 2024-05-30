using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace SwissTopoOfflineCreator
{
    static class ImageCheck
    {
        public static bool IsEmptyImage(byte[] data, string fileExt)
        {
            if (data.Length > 1500) { return false; }

            foreach (var e in emptyCache) {
                if (EqualByteArray(data,e)) { return true; }
            }

            Console.WriteLine("Checking image data with length {0} for being empty.", data.Length);
            try {
                var stream = new MemoryStream(data);
                using (var bitmap = new Bitmap(stream)) {
                    if (bitmap.PixelFormat == PixelFormat.Format32bppArgb) {
                        for (int y = 0; y < bitmap.Height; y++) {
                            for (int x = 0; x < bitmap.Width; x++) {
                                var p = bitmap.GetPixel(x, y);
                                if (p.A > 0) { return false; }
                            }
                        }
                    } else {
                        for (int y = 0; y < bitmap.Height; y++) {
                            for (int x = 0; x < bitmap.Width; x++) {
                                var p = bitmap.GetPixel(x, y);
                                if (p.R < 0xFF || p.G < 0xFF || p.B < 0xFF) { return false; }
                            }
                        }
                    }
                }
                emptyCache.Add(data);
                return true;
            } catch (Exception) {
                Console.WriteLine("Image decode error; assuming not an empty image.");
                return false;
            }
        }

        private static bool EqualByteArray(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) { return false; }
            for (int i = 0; i < a.Length; i++) {
                if (a[i] != b[i]) {  return false; }
            }
            return true;
        }

        private static readonly List<byte[]> emptyCache = new List<byte[]>();
    }
}