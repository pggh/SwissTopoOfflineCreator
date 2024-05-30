using System;
using System.IO;

namespace SwissTopoOfflineCreator
{
    // ReSharper disable InconsistentNaming
    struct CH1903
    {
        public const double yMin = 420000; // west
        public const double yMax = 900000; // east
        public const double xMin = 30000; // south
        public const double xMax = 350000; // north

        public const double yMinDefault = 485000; // west
        public const double yMaxDefault = 834000; // east
        public const double xMinDefault = 75000; // south
        public const double xMaxDefault = 296000; // north

        public double y;
        public double x;

        public CH1903(double y, double x)
        {
            this.y = y;
            this.x = x;
        }

        public CH1903(WGS84 wgs84) : this()
        {
            if (!WgsToSwiss(wgs84.latDeg, wgs84.lonDeg, out x, out y)) {
                throw new ArgumentOutOfRangeException();
            }
        }

        public WGS84 ToWGS84()
        {
            WGS84 wgs84;
            if (!SwissToWgs(x, y, out wgs84.latDeg, out wgs84.lonDeg)) {
                throw new ArgumentOutOfRangeException();
            }
            return wgs84;
        }

        private static bool WgsToSwiss(double latDeg, double lonDeg, out double x, out double y)
        {
            if (latDeg < 45.6 || latDeg > 48.6 || lonDeg < 4.9 || lonDeg > 12.8) {
                x = y = 0;
                return false;
            }
            var φ = 3600 * latDeg;
            var λ = 3600 * lonDeg;
            var φ1 = (φ - 169028.66) / 10000;
            var φ2 = φ1 * φ1;
            var φ3 = φ1 * φ2;
            var λ1 = (λ - 26782.5) / 10000;
            var λ2 = λ1 * λ1;
            var λ3 = λ1 * λ2;
            y = 600072.37 + (211455.93 * λ1) - (10938.51 * λ1 * φ1) - (0.36 * λ1 * φ2) - (44.54 * λ3);
            x = 200147.07 + (308807.95 * φ1) + (3745.25 * λ2) + (76.63 * φ2) - (194.56 * λ2 * φ1) + (119.79 * φ3);
            return true;
        }

        private static bool SwissToWgs(double x, double y, out double latDeg, out double lonDeg)
        {
            //            if (x < 50000 || x >= 400000 || y < 400000 || y >= 1000000) {
            if (x < 10000 || x >= 430000 || y < 400000 || y >= 1000000) {
                latDeg = lonDeg = 0;
                return false;
            }
            var y1 = (y - 600000) / 1000000;
            var y2 = y1 * y1;
            var y3 = y2 * y1;
            var x1 = (x - 200000) / 1000000;
            var x2 = x1 * x1;
            var x3 = x2 * x1;
            var λ1 = 2.6779094 + (4.728982 * y1) + (0.791484 * y1 * x1) + (0.1306 * y1 * x2) - (0.0436 * y3);
            var φ1 = 16.9023892 + (3.238272 * x1) - (0.270978 * y2) - (0.002528 * x2) - (0.0447 * y2 * x1) - (0.0140 * x3);
            lonDeg = λ1 * (100.0 / 36.0);
            latDeg = φ1 * (100.0 / 36.0);
            return true;
        }
    }

    readonly struct CH1903Rectangle
    {
        public CH1903Rectangle(double yMin, double yMax, double xMin, double xMax)
        {
            if (yMax < yMin) {
                var t = yMax;
                yMax = yMin;
                yMin = t;
            }
            if (xMax < xMin) {
                var t = xMax;
                xMax = xMin;
                xMin = t;
            }
            this.yMin = yMin;
            this.yMax = yMax;
            this.xMin = xMin;
            this.xMax = xMax;
        }

        public double yMin { get; }
        public double yMax { get; }
        public double xMin { get; }
        public double xMax { get; }

        public override string ToString()
        {
            return string.Format("{0,10:f3}..{1,10:f3},{2,10:f3}..{3,10:f3}", yMin, yMax, xMin, xMax);
        }

        public static readonly CH1903Rectangle All = new CH1903Rectangle(CH1903.yMin, CH1903.yMax, CH1903.xMin, CH1903.xMax);
    }

    struct WGS84
    {
        public double latDeg;
        public double lonDeg;
    }
    // ReSharper restore InconsistentNaming

    struct Tile
    {
        public int X;
        public int Y;
        public Layer Layer;

        public override string ToString()
        {
            return string.Format("{0}/{1}", Y, X);
        }

        public string UrlPath
        {
            //2016 get { return string.Concat(Layer.UrlPathBase, Y.ToString(), "/", X.ToString(), Layer.FileExt); }
            get { return string.Concat(Layer.UrlPathBase, X.ToString(), "/", Y.ToString(), Layer.FileExt); }
        }

        public string FilePath
        {
            get { return Path.Combine(Layer.TopoZoom.ToString(), Y.ToString(), X + Layer.FileExt.Replace(".jpeg", ".jpg")); }
        }

        public CH1903Rectangle Area
        {
            get
            {
                var min = Layer.TileToCH1903(X, Y + 1);
                var max = Layer.TileToCH1903(X + 1, Y);
                return new CH1903Rectangle(min.y, max.y, min.x, max.x);
            }
        }
    }

    class Layer
    {
        public required int TopoZoom;
        public required double TileWidthMeters;
        public required string[] Servers;
        public required string UrlPathBase;
        public required string MapName;
        public required string FileExt;

        public int Scale
        {
            get { return (int)Math.Round((10000.0 * TileWidthMeters) / 256); }
        }

        public CH1903 TileToCH1903(double xTile, double yTile)
        {
            return new CH1903 {
                y = xTile * TileWidthMeters + 420000,
                x = 350000 - yTile * TileWidthMeters
            };
        }

        public double CH1903XToImageY(double x)
        {
            return (350000 - x) / TileWidthMeters;
        }

        public double CH1903YToImageX(double y)
        {
            return (y - 420000) / TileWidthMeters;
        }

        public Tile CH1903ToTile(CH1903 ch)
        {
            return new Tile {
                X = (int)CH1903YToImageX(ch.y),
                Y = (int)CH1903XToImageY(ch.x),
                Layer = this
            };
        }
    }
}
