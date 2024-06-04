using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SwissTopoOfflineCreator
{
    enum TileStatus : byte
    {
        Missing,
        Available,
        Error,
        NotFound,
        OutOfMap,
        TileStatusLength
    }

    struct TileRange
    {
        public int xMin, xMax, yMin, yMax;

        public override string ToString()
        {
            return string.Format("{0}/{1} .. {2}/{3}", yMin, xMin, yMax, xMax);
        }
    }

    class DownloadedDataStatus
    {
        public const string FileExtNotFoundOrEmpty = ".404";

        public static DownloadedDataStatus[] FromLayers(Layer[] layers, string downloadDirectory, CH1903Rectangle area)
        {
            var ret = new DownloadedDataStatus[layers.Length];
            DownloadedDataStatus? parent = null;
            for (int i = 0; i < layers.Length; i++) {
                var layer = layers[i];
                var ds = new DownloadedDataStatus(layer, downloadDirectory, area);
                ds.ImportFiles();
                if (parent != null) {
                    ds.MarkUncoveredInParentLayerAsOutOfMap(parent);
                }
                parent = ds;
                ret[i] = ds;
            }
            return ret;
        }

        public DownloadedDataStatus(Layer layer, string downloadDirectory) : this(layer, downloadDirectory, CH1903Rectangle.All)
        {}

        public DownloadedDataStatus(Layer layer, string downloadDirectory, CH1903Rectangle area)
        {
            this.layer = layer;
            this.downloadDirectory = downloadDirectory;

            var min = layer.CH1903ToTile(new CH1903(area.yMin, area.xMax));
            var max = layer.CH1903ToTile(new CH1903(area.yMax, area.xMin));
            xOffset = min.X;
            yOffset = min.Y;
            tileStatus = new TileStatus[max.Y-min.Y+1][];
            for (int y = 0; y < tileStatus.Length; y++) {
                tileStatus[y] = new TileStatus[max.X-min.X+1];
            }
            TileStatusCount[(int)TileStatus.Missing] = tileStatus.Length*tileStatus[0].Length;
        }

        public Layer Layer => layer;

        public IEnumerable<Tile> AllTiles()
        {
            for (int y = 0; y < tileStatus.Length; y++) {
                for (int x = 0; x < tileStatus[0].Length; x++) {
                    yield return new Tile {X = xOffset+x, Y = yOffset+y, Layer = layer};
                }
            }
        }

        public TileRange AllTilesRange
        {
            get
            {
                return new TileRange {
                    xMin = xOffset, xMax = xOffset+tileStatus[0].Length-1,
                    yMin = yOffset, yMax = yOffset+tileStatus.Length-1
                };
            }
        }

        public TileRange AvailableTilesRange
        {
            get
            {
                var r = new TileRange { xMin = tileStatus[0].Length, xMax = -1, yMin = tileStatus.Length, yMax = -1 };
                for (int y = 0; y < tileStatus.Length; y++) {
                    for (int x = 0; x < tileStatus[0].Length; x++) {
                        if (tileStatus[y][x] == TileStatus.Available) {
                            if (x < r.xMin) { r.xMin = x; }
                            if (x > r.xMax) { r.xMax = x; }
                            if (y < r.yMin) { r.yMin = y; }
                            if (y > r.yMax) { r.yMax = y; }
                        }
                    }
                }
                r.xMin += xOffset;
                r.xMax += xOffset;
                r.yMin += yOffset;
                r.yMax += yOffset;
                return r;
            }
        }

        public void ImportFiles()
        {
            TotalFileSize = 0;
            var baseDir = new DirectoryInfo(Path.Combine(downloadDirectory, layer.TopoZoom.ToString()));
            if (baseDir.Exists) {
                foreach (var yDir in baseDir.GetDirectories("*")) {
                    if (int.TryParse(yDir.Name, out var y) && y >= yOffset && y < yOffset+tileStatus.Length) {
                        foreach (var file in yDir.GetFiles("*")) {
                            if (int.TryParse(Path.GetFileNameWithoutExtension(file.Name), out var x) &&
                                x >= xOffset && x < xOffset+tileStatus[y-yOffset].Length) {
                                TileStatus status;
                                switch (file.Extension) {
                                case FileExtNotFoundOrEmpty: status = TileStatus.NotFound; break;
                                case ".err": status = TileStatus.Error; break;
                                default: status = TileStatus.Available; break;
                                }
                                this[x, y] = status;
                                TotalFileSize += file.Length;
                            }
                        }
                    }
                }
            }
        }

        public void MarkFilteredAsOutOfMap(DownloadedDataStatus filter)
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (filter.layer.TileWidthMeters != layer.TileWidthMeters) {
                throw new ArgumentException("Incompatible zoom level.", nameof(filter));
            }

            var tilesRange = AllTilesRange;
            for (int y = tilesRange.yMin; y <= tilesRange.yMax; y++) {
                for (int x = tilesRange.xMin; x <= tilesRange.xMax; x++) {
                    var ts = this[x, y];
                    if (ts == TileStatus.Missing) {
                        var fs = filter[x, y];
                        if (fs == TileStatus.OutOfMap || fs == TileStatus.NotFound) {
                            this[x, y] = fs;
                        }
                    }
                }
            }
        }

        public void MarkUncoveredInParentLayerAsOutOfMap(DownloadedDataStatus parent)
        {
            var tilesRange = AllTilesRange;
            for (int y = tilesRange.yMin; y <= tilesRange.yMax; y++) {
                for (int x = tilesRange.xMin; x <= tilesRange.xMax; x++) {
                    var ts = this[x, y];
                    if (ts == TileStatus.Missing) {
                        var tile = new Tile { X = x, Y = y, Layer = layer };
                        if (!AvailableInParent(tile, parent)) {
                            this[x, y] = TileStatus.OutOfMap;
                        }
                    }
                }
            }
        }

        private static bool AvailableInParent(Tile tile, DownloadedDataStatus parent)
        {
            var a = tile.Area;
            var parentTiles = new Tile[] {
                parent.Layer.CH1903ToTile(new CH1903(a.yMin, a.xMin)),
                parent.Layer.CH1903ToTile(new CH1903(a.yMin, a.xMax)),
                parent.Layer.CH1903ToTile(new CH1903(a.yMax, a.xMin)),
                parent.Layer.CH1903ToTile(new CH1903(a.yMax, a.xMax)),
            };
            foreach (var pt in parentTiles) {
                switch (parent[pt.X, pt.Y]) {
                case TileStatus.Available:
                case TileStatus.Error:
                    return true;
                }
            }
            return false;
        }

        public TileStatus this[int x, int y]
        {
            get
            {
                if (y >= yOffset && y < yOffset+tileStatus.Length &&
                    x >= xOffset && x < xOffset+tileStatus[y-yOffset].Length) {
                    return tileStatus[y-yOffset][x-xOffset];
                } else {
                    return TileStatus.OutOfMap;
                }
            }
            set
            {
                var oldStatus = tileStatus[y-yOffset][x-xOffset];
                --TileStatusCount[(int)oldStatus];
                tileStatus[y-yOffset][x-xOffset] = value;
                ++TileStatusCount[(int)value];
            }
        }

        public void DumpToFile(string path)
        {
            var statChar = new char[(int)TileStatus.TileStatusLength];
            statChar[(int)TileStatus.Missing] = '?';
            statChar[(int)TileStatus.Available] = '#';
            statChar[(int)TileStatus.Error] = 'E';
            statChar[(int)TileStatus.NotFound] = '-';
            statChar[(int)TileStatus.OutOfMap] = ' ';
            using (var f = new StreamWriter(path)) {
                f.WriteLine("All: {0}", AllTilesRange);
                f.WriteLine("Available: {0}", AvailableTilesRange);
                f.Write("Statistics:");
                foreach (var s in new[] { TileStatus.Missing, TileStatus.Available, TileStatus.Error, TileStatus.NotFound, TileStatus.OutOfMap }) {
                    f.Write(" {0}({1}):{2}", s, statChar[(int)s], TileStatusCount[(int)s]);
                }
                f.WriteLine();
                f.WriteLine("Total file size: {0} MiB", TotalFileSize>> 20);
                for (int y = 0; y < tileStatus.Length; y++) {
                    var sb = new StringBuilder(tileStatus[0].Length + 2);
                    for (int x = 0; x < tileStatus[0].Length; x++) {
                        sb.Append(statChar[(int)tileStatus[y][x]]);
                    }
                    sb.AppendLine();
                    f.Write(sb);
                }
            }
        }

        public long TotalFileSize;
        public readonly int[] TileStatusCount = new int[(int)TileStatus.TileStatusLength];

        private readonly int xOffset;
        private readonly int yOffset;
        private readonly TileStatus[][] tileStatus; // [y][x]

        private readonly Layer layer;
        private readonly string downloadDirectory;
    }
}