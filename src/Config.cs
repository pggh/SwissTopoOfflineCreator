using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace SwissTopoOfflineCreator;

class MapFile
{
    public readonly Layer[] Layers;
    public readonly CH1903Rectangle Area;
    public readonly int MaxZoom;
    public readonly string DownloadDir;
    public readonly string OutputDir;
    public readonly string OutputMapName;

    public static MapFile Load(string path)
    {
        return new MapFile(XDocument.Load(path).Root!);
    }

    private MapFile(XElement root)
    {
        double GetCoordinate(string name, double defaultValue, double min, double max)
        {
            double v = (double?)root.Element(name) ?? defaultValue;
            if (v < min || v > max) {
                throw new XmlException($"'{name}' is not within the allowed range {min} .. {max}");
            }
            return v;
        }

        var mapSourceName = (string?)root.Element("map_source") ?? throw new XmlException("missing 'map_source' element");
        if (!MapSources.Sources.TryGetValue(mapSourceName, out var layers)) {
            throw new XmlException($"'{mapSourceName}' is not a valid map source.");
        }
        Layers = layers;
        Area = new CH1903Rectangle(GetCoordinate("y_min", CH1903.yMinDefault, CH1903.yMin, CH1903.yMax),
                                   GetCoordinate("y_max", CH1903.yMaxDefault, CH1903.yMin, CH1903.yMax),
                                   GetCoordinate("x_min", CH1903.xMinDefault, CH1903.xMin, CH1903.xMax),
                                   GetCoordinate("x_max", CH1903.xMaxDefault, CH1903.xMin, CH1903.xMax));
        if (Area.yMin >= Area.yMax || Area.xMin >= Area.xMax) {
            throw new XmlException("Defined map area is empty.");
        }
        MaxZoom = (int?)root.Element("max_zoom") ?? throw new XmlException("missing 'max_zoom' element");
        if (MaxZoom < 16 || MaxZoom > 28) {
            throw new XmlException("invalid value for 'max_zoom'");
        }
        Layers = (from l in Layers where l.TopoZoom <= MaxZoom select l).ToArray();
        DownloadDir = (string?)root.Element("download_dir") ?? throw new XmlException("missing 'download_dir' element");
        OutputDir = (string?)root.Element("output_dir") ?? throw new XmlException("missing 'output_dir' element");
        OutputMapName = (string?)root.Element("output_map_name") ?? throw new XmlException("missing 'output_map_name' element");
    }
}

static class MapSources
{
    public static readonly Dictionary<string, Layer[]> Sources = new();

    static MapSources()
    {
        var progDir = Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]) ?? ".";
        var xd = XDocument.Load(Path.Combine(progDir, "map_sources.xml"));
        foreach (var xmap in xd.Root!.Elements("map")) {
            var name = (string?)xmap.Element("name") ?? throw new XmlException("missing 'name' element in map.");
            var server = (string?)xmap.Element("server") ?? throw new XmlException("missing 'server' element in map.");
            var filetype = (string?)xmap.Element("filetype") ?? throw new XmlException("missing 'filetype' element in map.");
            List<Layer> layers = new List<Layer>();
            foreach (var xz in xmap.Elements("zoom")) {
                var level = (int?)xz.Element("level") ?? throw new XmlException("missing 'level' element in zoom.");
                var tileMeters = (double?)xz.Element("tile_meters") ?? throw new XmlException("missing 'tile_meters' element in zoom.");
                var zoomName = (string?)xz.Element("name") ?? throw new XmlException("missing 'name' element in zoom.");
                var url = (string?)xz.Element("url_base") ?? throw new XmlException("missing 'url_base' element in zoom.");
                layers.Add(new Layer {
                    TopoZoom = level,
                    TileWidthMeters = tileMeters,
                    Servers = new []{ server },
                    UrlPathBase = url,
                    MapName = zoomName,
                    FileExt = "." + filetype
                });
            }
            Sources[name] = layers.OrderBy(layer => layer.TopoZoom).ToArray();
        }
    }
}
