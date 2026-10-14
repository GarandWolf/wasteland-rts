# Attribution

Credits for the Oct 17–19 Windows playable build.

## Kenney

Kenney assets (Kenney Vleugels, https://kenney.nl/assets/ui-audio) are CC0 1.0, public domain dedication: https://creativecommons.org/publicdomain/zero/1.0/

The UI clicks and the three HUD cues (`ui_click.wav`, `under_attack.wav`, `research_done.wav`, `order_deny.wav`) come from the Kenney UI Audio pack.

## OpenStreetMap

Map data © OpenStreetMap contributors, available under the Open Database License (ODbL): https://www.openstreetmap.org/copyright

The ODbL source offer for the Columbia Gorge clip is `gorge-clip.osm.pbf`, attached to the playable GitHub release. The tag in this link is a placeholder (`playable-2026-10`). The real release tag must match the file name pattern `playable-*`.

https://github.com/GarandWolf/wasteland-rts/releases/download/playable-2026-10/gorge-clip.osm.pbf

17,153,637 bytes. SHA-256 `3eebb2957d33270446cbb2f46ea4a87631086da7c6418f7700693b7dc8b87176`.

The Columbia Gorge map is built from these per-region OpenStreetMap extracts:

https://download.geofabrik.de/north-america/us/oregon-260927.osm.pbf

https://download.geofabrik.de/north-america/us/washington-260927.osm.pbf

Those Geofabrik Oregon and Washington extracts (replication timestamp 2026-09-27T20:23:36Z) are clipped to `gorge/archive/osm/gorge-clip.osm.pbf`. The clip is not stored in this repository. See `data/golden/OSM-EXTRACT.txt`.

## Elevation and land cover

Contains modified Copernicus DEM data (GLO-30), © DLR e.V. 2010–2014 and © Airbus Defence and Space GmbH 2014–2018, provided under COPERNICUS by the European Union and ESA.

The organisations in charge of the Copernicus programme by law or by delegation do not incur any liability for any use of the Copernicus WorldDEM-30. The shipped elevation grid is a modified product: resampled onto the map grid, stored as integer centimetres, with derived slope.

ESA WorldCover © ESA 2021, CC BY 4.0.

USGS 3DEP elevation data (public domain). The map pipeline uses U.S. Geological Survey 3D Elevation Program (3DEP) data as an elevation difference check. It is not the shipped elevation grid.

## Audio

The table below is the placeholder audio record from `Assets/_Project/Audio/Placeholder/ATTRIBUTION.md`. Every file is CC0 1.0. Freesound files are the public HQ previews.

| Output file | Source title | Author | Source page (licence checked here) | Licence as stated on that page | Source file kept in `art-sources/audio/placeholder/_sources/` | Edits made |
|---|---|---|---|---|---|---|
| `ui_click.wav` | UI Audio (pack), file `Audio/click1.ogg` | Kenney (Kenney Vleugels, kenney.nl) | https://kenney.nl/assets/ui-audio | "Creative Commons CC0" (page); `License.txt`: "License (Creative Commons Zero, CC0) http://creativecommons.org/publicdomain/zero/1.0/" | `kenney_ui-audio.zip`, `kenney_ui-audio/Audio/click1.ogg` | Whole file (0.091 s); 10 ms fade-out; downmixed to mono; resampled to 48 kHz; normalised to -1 dBFS |
| `order_ack.wav` | Walkie-talkie end of transmission | LukaCafuka | https://freesound.org/people/LukaCafuka/sounds/760245/ | Creative Commons 0 (links to http://creativecommons.org/publicdomain/zero/1.0/) | `freesound/fs760245_LukaCafuka_preview-hq.ogg` | Trimmed 0.00–0.29 s; 2 ms fade-in and 20 ms fade-out; downmixed to mono; 48 kHz; -1 dBFS |
| `raise_battalion.wav` | Marcha_se_acerca.wav | josecruz98 | https://freesound.org/people/josecruz98/sounds/393019/ | Creative Commons 0 | `freesound/fs393019_josecruz98_preview-hq.ogg` | Trimmed 0.25–2.75 s (4 steps); 10 ms fade-in and 250 ms fade-out; 48 kHz stereo; -1 dBFS |
| `depot_built.wav` | Sledgehammer Hitting Spike | petebuchwald | https://freesound.org/people/petebuchwald/sounds/351312/ | Creative Commons 0 | `freesound/fs351312_petebuchwald_preview-hq.ogg` | Trimmed 12.45–13.45 s (the last strike and its ring); 3 ms fade-in and 450 ms fade-out; 48 kHz stereo; -1 dBFS |
| `combat_loop.wav` | BattleField Ambience (Loop) | banan_ | https://freesound.org/people/banan_/sounds/385895/ | Creative Commons 0 | `freesound/fs385895_banan__preview-hq.ogg` | Full length; 30 Hz 2-pole high-pass; seamless loop made with a 1.0 s equal-power crossfade of the tail into the head (59.15 s becomes 58.15 s); 48 kHz stereo; -1 dBFS |
| `town_captured.wav` | Military Snaredrum | aUREa | https://freesound.org/people/aUREa/sounds/475246/ | Creative Commons 0 | `freesound/fs475246_aUREa_preview-hq.ogg` | Trimmed 7.92–10.22 s (closing bar of the cadence plus reverb tail); 5 ms fade-in and 400 ms fade-out; 48 kHz stereo; -1 dBFS |
| `town_lost.wav` | TOLLING BELL | SamuelGremaud | https://freesound.org/people/SamuelGremaud/sounds/454855/ | Creative Commons 0 | `freesound/fs454855_SamuelGremaud_preview-hq.ogg` | Trimmed 48.40–52.40 s (the last strikes of the peal); 20 ms fade-in and 1.2 s fade-out; 48 kHz stereo; -1 dBFS |
| `win_stinger.wav` | Trumpet Revellie.wav | trumpetplayer | https://freesound.org/people/trumpetplayer/sounds/651200/ | Creative Commons 0 | `freesound/fs651200_trumpetplayer_preview-hq.ogg` | Trimmed 0.00–6.00 s (opening phrase of Reveille); 5 ms fade-in and 250 ms fade-out; 48 kHz stereo; -1 dBFS |
| `lose_stinger.wav` | Trumpet Taps.wav | trumpetplayer | https://freesound.org/people/trumpetplayer/sounds/651204/ | Creative Commons 0 | `freesound/fs651204_trumpetplayer_preview-hq.ogg` | Trimmed 0.15–6.00 s (first phrase of Taps, "Day is done"); 20 ms fade-in and 300 ms fade-out; 48 kHz stereo; -1 dBFS |
| `draw_stinger.wav` | Trumpet Taps.wav (same source as the lose stinger) | trumpetplayer | https://freesound.org/people/trumpetplayer/sounds/651204/ | Creative Commons 0 | `freesound/fs651204_trumpetplayer_preview-hq.ogg` | Lose stinger cut short: trimmed 0.15–3.25 s, with a 1.0 s fade-out over the held note so the phrase never resolves; 48 kHz stereo; -1 dBFS |
| `under_attack.wav` | UI Audio (pack), file `Audio/switch2.ogg` | Kenney (Kenney Vleugels, kenney.nl) | https://kenney.nl/assets/ui-audio | "Creative Commons CC0" (page); `License.txt`: "License (Creative Commons Zero, CC0) http://creativecommons.org/publicdomain/zero/1.0/" | `kenney_ui-audio.zip`, `kenney_ui-audio/Audio/switch2.ogg` | Whole file; 20 ms fade-out; downmixed to mono; resampled to 48 kHz; normalised to -1 dBFS |
| `research_done.wav` | UI Audio (pack), file `Audio/switch3.ogg` | Kenney (Kenney Vleugels, kenney.nl) | https://kenney.nl/assets/ui-audio | "Creative Commons CC0" (page); `License.txt`: "License (Creative Commons Zero, CC0) http://creativecommons.org/publicdomain/zero/1.0/" | `kenney_ui-audio.zip`, `kenney_ui-audio/Audio/switch3.ogg` | Whole file; 20 ms fade-out; downmixed to mono; resampled to 48 kHz; normalised to -1 dBFS |
| `order_deny.wav` | UI Audio (pack), file `Audio/click2.ogg` | Kenney (Kenney Vleugels, kenney.nl) | https://kenney.nl/assets/ui-audio | "Creative Commons CC0" (page); `License.txt`: "License (Creative Commons Zero, CC0) http://creativecommons.org/publicdomain/zero/1.0/" | `kenney_ui-audio.zip`, `kenney_ui-audio/Audio/click2.ogg` | Whole file; 10 ms fade-out; downmixed to mono; resampled to 48 kHz; normalised to -1 dBFS |
