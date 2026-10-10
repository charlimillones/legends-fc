# Unity setup on the Windows PC (one time)

1. **Git for Windows:** run `Git-2.56.0.2-64-bit.exe` from Downloads and accept the defaults.
2. **Unity Hub:** sign in at unity.com (free account), download Unity Hub and install it.
3. **Unity editor:** in Unity Hub, go to Installs → Install Editor → **Unity 6 LTS**, and tick these modules:
   - **iOS Build Support** (exports the Xcode project; it's compiled on a cloud Mac later)
   - Android Build Support (optional)
4. **Get the code:** clone this repo into `Documents\Legends FC\05 Local Files\legends-fc`:
   `git clone https://github.com/charlimillones/legends-fc.git`
5. **Create the Unity project** in `unity/LegendsFC` from the Universal 3D (URP) template.
6. **Add the core package:** edit `unity/LegendsFC/Packages/manifest.json` and add:
   `"com.legendsfc.core": "file:../../../core"`
7. **iPhone and iPad testing:**
   - **Every day:** Window → General → Device Simulator, then choose an iPhone or iPad.
   - **Touch on your device:** install Unity Remote 5 on the iPhone, plus the Apple Devices app on Windows (for USB). In the editor: Project Settings → Editor → Device: Any iOS Device.
   - **Real builds (later):** GitHub Actions on a cloud Mac uploads to TestFlight. This needs the Apple Developer Program ($99/yr).

## Status (Oct 9, 2026)
- Done: Unity 6.6 (6000.6.5f1) with iOS, Web and Windows build support; the URP project `unity/LegendsFC` was created from the Universal 3D template (local project, no Unity Cloud); the core package is added to the manifest.
- Smoke test (Legends FC → Smoke Test) passed in the editor: "222 clubs, 7305 players, ENG-1 champion Blackstead Town, 4937 ms", with 0 errors and 0 warnings.

## First playable (Oct 10, 2026)
- **Code:** `unity/LegendsFC/Assets/LegendsFC/Runtime` (placeholder screens built from code with uGUI, so they work before the real designs) and `Assets/LegendsFC/EditorTools` (editor-only helpers).
- **Play in the editor:** open the project and press Play in any scene. The app starts itself, reads the game data from the repo's `data` folder and saves worlds in the user's app-data folder (`AppData/LocalLow/DefaultCompany/LegendsFC/worlds`).
- **Windows version:** menu Legends FC → Build Windows version. Output: `unity/LegendsFC/Builds/Windows/LegendsFC.exe` (git-ignored). Builds read a copy of the data placed in `Assets/StreamingAssets/data` before each build (also git-ignored).
- **Screens:** worlds list, new world (name, currency), pick a club, first protégé, home (objective, league position, money, recent results, job offers), squad (regimes), player (scouted ranges, renew, list for sale, make an offer), tactics (formations, mentality, auto pick, swaps), transfers (search, bids, talks), negotiation (fee, wage, years, live chance, counters), club (facilities, coaches, scouts, yearly protégé), inbox (decisions), league tables, the sim-mode match replay (events minute by minute, other results at half time and full time), season summary with awards, jobs.
- **Dev bridge (for Claude):** small files in `unity/LegendsFC/Logs` drive the open editor: `lfc-refresh.flag` (recompile), `lfc-play.flag` / `lfc-stop.flag`, `lfc-shot.flag` (Game view to `lfc-shot.png`), `lfc-tap.flag` (press a button by its label), `lfc-type.flag` (fill the input field), `lfc-build.flag` (Windows build). Results: `lfc-compile.txt`, `lfc-console.txt`.
