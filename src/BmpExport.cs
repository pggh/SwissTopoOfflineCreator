using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace SwissTopoOfflineCreator
{
    static class BmpExport
    {
        public static void Export(string tileDir,
                                  Layer[] layers,
                                  CH1903Rectangle area,
                                  string bitmapFilename)
        {
            var layer = layers[layers.Length - 1];

            var tileMin = layer.CH1903ToTile(new CH1903(area.yMin, area.xMax));
            var tileMax = layer.CH1903ToTile(new CH1903(area.yMax, area.xMin));

            var status = new DownloadedDataStatus(layer, tileDir, area);
            status.ImportFiles();

            var tilesRange = status.AvailableTilesRange;
            tilesRange.xMin = Math.Max(tilesRange.xMin, tileMin.X);
            tilesRange.xMax = Math.Min(tilesRange.xMax, tileMax.X);
            tilesRange.yMin = Math.Max(tilesRange.yMin, tileMin.Y);
            tilesRange.yMax = Math.Min(tilesRange.yMax, tileMax.Y);

            const int tilePixels = 256;

            var xPixels = tilePixels * (tilesRange.xMax-tilesRange.xMin +1);
            var yPixels = tilePixels * (tilesRange.yMax-tilesRange.yMin +1);
            if (xPixels > 32767 || yPixels > 32767) {
                throw new NotSupportedException($"The size of the image to be exported is {xPixels}x{yPixels} but bitmaps support max 32767x32767 pixels.");
            }

            using (var bitmap = new Bitmap(xPixels, yPixels, PixelFormat.Format24bppRgb)) {
                using (var g = Graphics.FromImage(bitmap)) {
                    for (int y = tilesRange.yMin; y <= tilesRange.yMax; y++) {
                        for (int x = tilesRange.xMin; x <= tilesRange.xMax; x++) {
                            if (status[x, y] == TileStatus.Available) {
                                var tile = new Tile { X = x, Y = y, Layer = layer };
                                string tileFile = Path.Combine(tileDir, tile.FilePath);
                                using (var tileBitmap = new Bitmap(tileFile)) {
                                    g.DrawImageUnscaled(tileBitmap, new Point(tilePixels * (x - tilesRange.xMin), 
                                                                              tilePixels * (y - tilesRange.yMin)));
                                }
                            }
                        }
                    }
                }
                bitmap.Save(bitmapFilename, ImageFormat.Bmp);
            }
        }
    }
}
