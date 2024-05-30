using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SwissTopoOfflineCreator
{
    static class Program
    {
        static void Main(string[] args)
        {
            try {
                CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

                if (args.Length >= 2 && args.Length <= 3 && args[0] == "download") {
                    var mf = MapFile.Load(args[1]);
                    var mfFilter = args.Length > 2 ? MapFile.Load(args[2]) : null;

                    var dj = new DownloadJob();
                    dj.Layers = mf.Layers;
                    dj.DownloadDirectory = mf.DownloadDir;
                    dj.Area = mf.Area;
                    if (mfFilter != null) {
                        dj.TileFilters = new DownloadedDataStatus[mf.Layers.Length];
                        for (int i = 0; i < mf.Layers.Length; i++) {
                            var fl = mfFilter.Layers.FirstOrDefault(ff => ff.TopoZoom == mf.Layers[i].TopoZoom);
                            if (fl != null) {
                                dj.TileFilters[i] = new DownloadedDataStatus(fl, mfFilter.DownloadDir, mf.Area);
                                dj.TileFilters[i]!.ImportFiles();
                                if (i > 0 && dj.TileFilters[i - 1] != null) {
                                    dj.TileFilters[i]!.MarkUncoveredInParentLayerAsOutOfMap(dj.TileFilters[i - 1]!);
                                }
                            }
                        }
                    }
                    Directory.CreateDirectory(dj.DownloadDirectory);
                    dj.Download();
                } else if (args.Length == 2 && args[0] == "export_orux") {
                    var mf = MapFile.Load(args[1]);
                    OruxExport.Export(tileDir : mf.DownloadDir,
                                      layers : mf.Layers,
                                      area : mf.Area,
                                      oruxMapDir : mf.OutputDir,
                                      mapName : mf.OutputMapName);
                } else if (args.Length == 3 && args[0] == "export_bmp") {
                    var mf = MapFile.Load(args[1]);
                    BmpExport.Export(tileDir: mf.DownloadDir,
                                     layers: mf.Layers,
                                     area: mf.Area,
                                     bitmapFilename: args[2]);
                } else if (args.Length == 2 && args[0] == "status") {
                    var mf = MapFile.Load(args[1]);
                    var dss = DownloadedDataStatus.FromLayers(mf.Layers, mf.DownloadDir, mf.Area);
                    foreach (var ds in dss) {
                        ds.DumpToFile(Path.Combine(mf.DownloadDir, ds.Layer.TopoZoom + ".status"));
                    }
                } else {
                    Console.WriteLine("SwissTopoOfflineCreator Version " + (typeof(Program).Assembly.GetName().Version?.ToString() ?? "?"));
                    Console.WriteLine();
                    Console.WriteLine(@"usage:
   SwissTopoOfflineCreator download    <mapdef.map> [<filtermapdef.map>]
   SwissTopoOfflineCreator export_orux <mapdef.map>
   SwissTopoOfflineCreator export_bmp  <mapdef.map> <image.bmp>
   SwissTopoOfflineCreator status      <mapdef.map>
");
                }
            }
            catch (Exception ex) {
                Console.WriteLine(ex);
            }
        }
    }
}
