using System;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Xml.Linq;

namespace SwissTopoOfflineCreator
{
    static class OruxExport
    {
        private static readonly XNamespace oruxNs = "http://oruxtracker.com/app/res/calibration";

        public static void Export(string tileDir,
                                  Layer[] layers,
                                  CH1903Rectangle area,
                                  string oruxMapDir,
                                  string mapName)
        {
            var dss = DownloadedDataStatus.FromLayers(layers, tileDir, area);
            byte[]? transparentImage = null;

            bool anyMissing = false;
            foreach (var ds in dss) {
                var errors = ds.TileStatusCount[(int)TileStatus.Error];
                if (errors != 0) {
                    Console.Error.WriteLine($"Warning: {errors} download errors on zoom-level {ds.Layer.TopoZoom}.");
                }
                var missing = ds.TileStatusCount[(int)TileStatus.Missing];
                if (missing != 0) {
                    anyMissing = true;
                    Console.Error.WriteLine($"Warning: {missing} missing tiles on zoom-level {ds.Layer.TopoZoom}.");
                }
                if (transparentImage == null && ds.Layer.FileExt.ToLowerInvariant().EndsWith("png")) {
                    foreach (var tile in ds.AllTiles()) {
                        if (ds[tile.X, tile.Y] == TileStatus.NotFound) {
                            string imageFile = Path.Combine(tileDir, Path.ChangeExtension(tile.FilePath, DownloadedDataStatus.FileExtNotFoundOrEmpty));
                            if (File.Exists(imageFile)) {
                                transparentImage = File.ReadAllBytes(imageFile);
                                break;
                            }
                        }
                    }
                }
            }
            if (anyMissing) {
                Console.Error.WriteLine("Note: missing-warnings are normal if you used a filter-map on download.");
            }

            if (transparentImage != null) {
                Console.Error.WriteLine("Info: map is a transparent overlay.");
            }

            Directory.CreateDirectory(oruxMapDir);

            XElement oruxTracker = new XElement(oruxNs + "OruxTracker");
            oruxTracker.Add(new XAttribute("versionCode", "3.0"));
            XElement mapCalibration = new XElement(oruxNs+"MapCalibration",
                                                   new XAttribute("layers", "true"),
                                                   new XAttribute("layerLevel", "0"),
                                                   new XElement(oruxNs+"MapName", mapName));
            oruxTracker.Add(mapCalibration);

            var csb = new SqliteConnectionStringBuilder {
                DataSource = Path.Combine(oruxMapDir, "OruxMapsImages.db"),
                ForeignKeys = true,
                Mode = SqliteOpenMode.ReadWriteCreate,
            };
            File.Delete(csb.DataSource);

            using (var db = new SqliteConnection(csb.ConnectionString)) {
                db.Open();
                using (var command = db.CreateCommand()) {
                    command.CommandText = "CREATE TABLE android_metadata (locale TEXT)";
                    command.ExecuteNonQuery();
                    command.CommandText = "INSERT INTO android_metadata (locale) VALUES (\"de_CH\")";
                    command.ExecuteNonQuery();
                    if (transparentImage != null) {
                        command.CommandText = "CREATE TABLE tiles_tbl (x int, y int, z int, image blob, PRIMARY KEY (x,y,z))";
                        command.ExecuteNonQuery();
                        using (var transaction = db.BeginTransaction()) {
                            command.Transaction = transaction;
                            command.CommandText = "INSERT INTO tiles_tbl (x, y, z, image) VALUES ($x, $y, $z, $image)";
                            command.Prepare();

                            for (int li = 0; li < layers.Length; li++) {
                                AddLayer(tileDir, area, dss[li], li + 9, command, true, mapCalibration);
                            }

                            command.CommandText = "INSERT INTO tiles_tbl (x, y, z, image) VALUES (-1, -1, -1, $image)";
                            command.Prepare();
                            command.Parameters.Clear();
                            command.Parameters.AddWithValue("$image", transparentImage);
                            command.ExecuteNonQuery();

                            transaction.Commit();
                        }
                        command.Transaction = null;
                        command.CommandText = "CREATE VIEW tiles (x, y, z, image) as "+
                                              "SELECT x, y, z, coalesce(image, (SELECT image FROM tiles_tbl WHERE x = -1 and y = -1 and z = -1)) FROM tiles_tbl";
                        command.ExecuteNonQuery();
                    } else {
                        command.CommandText = "CREATE TABLE tiles (x int, y int, z int, image blob, PRIMARY KEY (x,y,z))";
                        command.ExecuteNonQuery();
                        using (var transaction = db.BeginTransaction()) {
                            command.Transaction = transaction;
                            command.CommandText = "INSERT INTO tiles (x, y, z, image) VALUES ($x, $y, $z, $image)";
                            command.Prepare();

                            for (int li = 0; li < layers.Length; li++) {
                                AddLayer(tileDir, area, dss[li], li + 9, command, false, mapCalibration);
                            }

                            transaction.Commit();
                        }
                    }
                }
                db.Close();
            }

            oruxTracker.Save(Path.Combine(oruxMapDir, "mapinfo.otrk2.xml"));
        }

        static void AddLayer(string tileDir, CH1903Rectangle area, DownloadedDataStatus status, int oruxZoom, SqliteCommand tileInsertCommand, bool addNullImage, XElement xml)
        {
            var layer = status.Layer;

            var tileMin = layer.CH1903ToTile(new CH1903(area.yMin, area.xMax));
            var tileMax = layer.CH1903ToTile(new CH1903(area.yMax, area.xMin));

            var tilesRange = status.AvailableTilesRange;
            tilesRange.xMin = Math.Max(tilesRange.xMin, tileMin.X);
            tilesRange.xMax = Math.Min(tilesRange.xMax, tileMax.X);
            tilesRange.yMin = Math.Max(tilesRange.yMin, tileMin.Y);
            tilesRange.yMax = Math.Min(tilesRange.yMax, tileMax.Y);

            Console.WriteLine("Exporting layer {0} range {1}", layer.TopoZoom, tilesRange);

            var pX = new SqliteParameter("$x", SqliteType.Integer);
            var pY = new SqliteParameter("$y", SqliteType.Integer);
            var pZ = new SqliteParameter("$z", SqliteType.Integer);
            var pImage = new SqliteParameter("$image", SqliteType.Blob);

            tileInsertCommand.Parameters.Clear();
            tileInsertCommand.Parameters.Add(pX);
            tileInsertCommand.Parameters.Add(pY);
            tileInsertCommand.Parameters.Add(pZ);
            tileInsertCommand.Parameters.Add(pImage);

            // add images to DB
            for (int y = tilesRange.yMin; y <= tilesRange.yMax; y++) {
                for (int x = tilesRange.xMin; x <= tilesRange.xMax; x++) {
                    if (status[x, y] == TileStatus.Available) {
                        var tile = new Tile {X = x, Y = y, Layer = layer};
                        string imageFile = Path.Combine(tileDir, tile.FilePath);
                        pX.Value = x - tilesRange.xMin;
                        pY.Value = y - tilesRange.yMin;
                        pZ.Value = oruxZoom;
                        pImage.Value = File.ReadAllBytes(imageFile);
                        tileInsertCommand.ExecuteNonQuery();
                    } else if (addNullImage) {
                        pX.Value = x - tilesRange.xMin;
                        pY.Value = y - tilesRange.yMin;
                        pZ.Value = oruxZoom;
                        pImage.Value = DBNull.Value;
                        tileInsertCommand.ExecuteNonQuery();
                    }
                }
            }

            // add layer info
            XElement oruxTracker = new XElement(oruxNs+"OruxTracker");
            xml.Add(oruxTracker);
            XElement mapCalibration = new XElement(oruxNs+"MapCalibration",
                                               new XAttribute("layers", "false"),
                                               new XAttribute("layerLevel", oruxZoom),
                                               new XElement(oruxNs+"MapName", layer.MapName));
            oruxTracker.Add(mapCalibration);
            mapCalibration.Add(new XElement(oruxNs + "MapChunks",
                                             new XAttribute("xMax", tilesRange.xMax - tilesRange.xMin + 1),
                                             new XAttribute("yMax", tilesRange.yMax - tilesRange.yMin + 1),
                                             new XAttribute("datum", "CH-1903:Swiss@WGS 1984:Global Definition"),
                                             new XAttribute("projection", "(SUI) Swiss Grid"),
                                             new XAttribute("img_height", 256),
                                             new XAttribute("img_width", 256),
                                             new XAttribute("file_name", "Swiss")));
            mapCalibration.Add(new XElement(oruxNs + "MapDimensions",
                                             new XAttribute("height", (tilesRange.yMax - tilesRange.yMin + 1)*256),
                                             new XAttribute("width", (tilesRange.xMax - tilesRange.xMin + 1)*256)));
            var tl = layer.TileToCH1903(tilesRange.xMin, tilesRange.yMin).ToWGS84();
            var tr = layer.TileToCH1903(tilesRange.xMax+1, tilesRange.yMin).ToWGS84();
            var bl = layer.TileToCH1903(tilesRange.xMin, tilesRange.yMax+1).ToWGS84();
            var br = layer.TileToCH1903(tilesRange.xMax+1, tilesRange.yMax+1).ToWGS84();
            mapCalibration.Add(new XElement(oruxNs + "MapBounds",
               new XAttribute("minLat", Math.Min(Math.Min(tl.latDeg, tr.latDeg), Math.Min(bl.latDeg, br.latDeg))),
               new XAttribute("maxLat", Math.Max(Math.Max(tl.latDeg, tr.latDeg), Math.Max(bl.latDeg, br.latDeg))),
               new XAttribute("minLon", Math.Min(Math.Min(tl.lonDeg, tr.lonDeg), Math.Min(bl.lonDeg, br.lonDeg))),
               new XAttribute("maxLon", Math.Max(Math.Max(tl.lonDeg, tr.lonDeg), Math.Max(bl.lonDeg, br.lonDeg)))));
            XElement calibrationPoints = new XElement(oruxNs+"CalibrationPoints");
            mapCalibration.Add(calibrationPoints);
            calibrationPoints.Add(new XElement(oruxNs + "CalibrationPoint",
                                               new XAttribute("corner", "TL"),
                                               new XAttribute("lon", tl.lonDeg),
                                               new XAttribute("lat", tl.latDeg)));
            calibrationPoints.Add(new XElement(oruxNs + "CalibrationPoint",
                                               new XAttribute("corner", "BR"),
                                               new XAttribute("lon", br.lonDeg),
                                               new XAttribute("lat", br.latDeg)));
            calibrationPoints.Add(new XElement(oruxNs + "CalibrationPoint",
                                               new XAttribute("corner", "TR"),
                                               new XAttribute("lon", tr.lonDeg),
                                               new XAttribute("lat", tr.latDeg)));
            calibrationPoints.Add(new XElement(oruxNs + "CalibrationPoint",
                                               new XAttribute("corner", "BL"),
                                               new XAttribute("lon", bl.lonDeg),
                                               new XAttribute("lat", bl.latDeg)));
        }
    }
}
