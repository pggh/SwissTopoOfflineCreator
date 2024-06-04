using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace SwissTopoOfflineCreator
{
    class DownloadJob
    {
        public Layer[]? Layers;
        public CH1903Rectangle Area = CH1903Rectangle.All;
        public int MaxParallelRequests = 1;
        public double MaxRequestsPerSec = 10;
        public string DownloadDirectory = ".";
        public DownloadedDataStatus?[]? TileFilters = null;

        public DownloadJob()
        {
            var speed = Environment.GetEnvironmentVariable("SWISSTOPOOFFLINECREATOR_RPS");
            if (speed == null ||
                !double.TryParse(speed, out MaxRequestsPerSec) ||
                MaxRequestsPerSec <= 0) {
                MaxRequestsPerSec = 10;
            }
        }

        public void Download()
        {
            dirCreatedCache.Clear();
            layerStatus = new DownloadedDataStatus[Layers!.Length];
            for (int layerIndex = 0; layerIndex < Layers.Length; layerIndex++) {
                layerStatus[layerIndex] = new DownloadedDataStatus(Layers[layerIndex], DownloadDirectory, Area);
                layerStatus[layerIndex].ImportFiles();
                DownloadLayer(layerIndex);
            }
        }

        private void DownloadLayer(int layerIndex)
        {
            var layer = Layers![layerIndex];
            var status = layerStatus![layerIndex];
            var parentStatus = layerIndex > 0 ? layerStatus[layerIndex-1] : null;

            var tileMin = layer.CH1903ToTile(new CH1903(Area.yMin, Area.xMax));
            var tileMax = layer.CH1903ToTile(new CH1903(Area.yMax, Area.xMin));

            var tilesRange = status.AllTilesRange;
            tilesRange.xMin = Math.Max(tilesRange.xMin, tileMin.X);
            tilesRange.xMax = Math.Min(tilesRange.xMax, tileMax.X);
            tilesRange.yMin = Math.Max(tilesRange.yMin, tileMin.Y);
            tilesRange.yMax = Math.Min(tilesRange.yMax, tileMax.Y);

            if (parentStatus != null) {
                status.MarkUncoveredInParentLayerAsOutOfMap(parentStatus);
            }
            if (TileFilters != null && TileFilters[layerIndex] != null) {
                status.MarkFilteredAsOutOfMap(TileFilters[layerIndex]!);
            }

            Console.Error.WriteLine("Downloading Layer {0} ({1})", layer.TopoZoom, layer.MapName);

            int hostIndex = 0;
            for (int retry = 0; retry <= 10; retry++) {
                var requests = new List<TileRequest>();
                downloadErrors = 0;
                for (int y = tilesRange.yMin; y <= tilesRange.yMax; y++) {
                    for (int x = tilesRange.xMin; x <= tilesRange.xMax; x++) {
                        var tileStatus = status[x, y];
                        if (tileStatus == TileStatus.Missing || tileStatus == TileStatus.Error) {
                            var tile = new Tile {X = x, Y = y, Layer = layer};
                            var r = new TileRequest {
                                Tile = tile,
                                Host = layer.Servers[hostIndex],
                                Status = status
                            };
                            hostIndex = (hostIndex + 1) % layer.Servers.Length;
                            requests.Add(r);
                        }
                    }
                }
                totalRequests = requests.Count;
                completedRequests = 0;
                using (var downloader = new BatchDownloader<TileRequest>(MaxRequestsPerSec, MaxParallelRequests)) {
                    downloader.Download(requests, StoreContent).Wait();
                }
                if (downloadErrors == 0) { break; }
            }

            var statusPath = Path.Combine(DownloadDirectory, layer.TopoZoom + ".status");
            CreateDirectoryForFile(statusPath);
            status.DumpToFile(statusPath);
        }

        private void StoreContent(TileRequest request, byte[]? data, HttpStatusCode httpStatusCode, Exception? exception)
        {
            ++completedRequests;
            var relPath = request.Tile.FilePath;
            var pathData = Path.Combine(DownloadDirectory, relPath);
            var pathNotFound = Path.ChangeExtension(pathData, DownloadedDataStatus.FileExtNotFoundOrEmpty);
            var pathError = Path.ChangeExtension(pathData, ".err");
            CreateDirectoryForFile(pathData);

            void WriteWithRename(string path, byte[] d)
            {
                var temp = Path.ChangeExtension(path, ".part");
                File.WriteAllBytes(temp, d);
                File.Move(temp, path, true);
            }

            if (data != null && data.Length != 0 && httpStatusCode == HttpStatusCode.OK) {
                if (ImageCheck.IsEmptyImage(data, request.Tile.Layer.FileExt)) {
                    WriteWithRename(pathNotFound, data);
                    request.Status[request.Tile.X, request.Tile.Y] = TileStatus.NotFound;
                } else {
                    WriteWithRename(pathData, data);
                    request.Status.TotalFileSize += data.Length;
                    switch (request.Status[request.Tile.X, request.Tile.Y]) {
                    case TileStatus.Error:
                        File.Delete(pathError);
                        break;
                    case TileStatus.NotFound:
                        File.Delete(pathNotFound);
                        break;
                    }
                    request.Status[request.Tile.X, request.Tile.Y] = TileStatus.Available;
                    Console.WriteLine("A:{0},M:{1},E:{2}   ({3}/{4})   {5}",
                                      request.Status.TileStatusCount[(int)TileStatus.Available],
                                      request.Status.TileStatusCount[(int)TileStatus.Missing],
                                      request.Status.TileStatusCount[(int)TileStatus.Error],
                                      completedRequests,
                                      totalRequests,
                                      relPath);
                }
            } else if (httpStatusCode == HttpStatusCode.NotFound) {
                if (request.Status[request.Tile.X, request.Tile.Y] == TileStatus.Error) {
                    File.Delete(pathError);
                }
                File.WriteAllBytes(pathNotFound, Array.Empty<byte>());
                request.Status[request.Tile.X, request.Tile.Y] = TileStatus.NotFound;
            } else {
                File.WriteAllBytes(pathError, Array.Empty<byte>());
                request.Status[request.Tile.X, request.Tile.Y] = TileStatus.Error;
                ++downloadErrors;
            }
        }

        private DownloadedDataStatus[]? layerStatus;
        private int totalRequests;
        private int completedRequests;
        private int downloadErrors;

        private readonly HashSet<string> dirCreatedCache = new HashSet<string>();

        private void CreateDirectoryForFile(string path)
        {
            var d = Path.GetDirectoryName(path);
            if (d != null && dirCreatedCache.Add(d)) {
                Directory.CreateDirectory(d);
            }
        }

        class TileRequest : IDownloadRequest
        {
            public string Uri
            {
                get { return Host+Tile.UrlPath; }
            }

            public Tile Tile;
            public required string Host;
            public required DownloadedDataStatus Status;
        }
    }
}
