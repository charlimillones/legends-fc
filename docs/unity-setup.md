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
