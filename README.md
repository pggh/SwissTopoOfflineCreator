# SwissTopoOfflineCreator

SwissTopoOfflineCreator allows you to download swiss topo maps (and other layers)
for private use with [OruxMaps](https://www.oruxmaps.com/cs/en/more/downloads) or
[Locus](https://www.locusmap.app/) on your Android device.

## Usage

1. Download the windows binaries from the [releases page](releases) or compile
   the sources yourself.

2. Create a working directory on you computer.
   (about 12GB required for a full 1:25000 map, about 50GB for a 1:10000 map)

3. Extract the binaries into that directory.

4. Open a console window in the directory.

5. Device what map and area you would like to use. There are a few templates in 
   the [map](map) directory. You can use them directly or create a modified copy
   of them with changed area or zoom level. It is possible to use different
   areas or zoom levels for download and export, as long the export is a subset
   of the download.\
   A 1:25000 map equivalent of the whole country is as about 5 GB on your
   device, a 1:10000 map about 4 to 5 times more.
   The area to download or export in the *.map files is in 
   [CH1903 coordinates](https://de.wikipedia.org/wiki/Schweizer_Landeskoordinaten#Umrechnung_WGS84_auf_CH1903)

6. run \
   `SwissTopoOfflineCreator download map\pixelkarte_25000.map`\
   as an example.\
   You can stop the download at any time and continue it by starting it again.
   The download of a 1:25000 map takes about 6 hours due to the fact that the
   [Terms of Use](https://www.geo.admin.ch/en/general-terms-of-use-fsdi) allows
   to download at most 10 tiles per second. A 1:10000 map will take about a day.\
   To ensure that everything was been downloaded without any errors, repeat the
   download until no more downloads are made.

7. If you would like to download other lyers, I suggest you use a already
   downloaded 'pixelkarte' as a filter for further downloads as this redusces
   the number of tiles that need to be downloaded:\
   `SwissTopoOfflineCreator download map\swissimage_25000.map map\pixelkarte_25000.map`

8. Export the maps (or a sub-area of them) using:\
   `SwissTopoOfflineCreator extract_orux map\pixelkarte_25000.map`\
   (The virus scanner on your machine might slow down the extract by an
   order of magnitude)

## Installing on OruxMaps

Copy the created directories (not just the files) into the oruxmaps/mapfiles
directory on your device. Restart OruxMaps

## Installing on Locus
   
{tbd}

## Downloading other layers

You have to extend the map_sources.xml based on the specifications found in the
[Capabilities](https://wmts.geo.admin.ch/1.0.0/WMTSCapabilities.xml)
