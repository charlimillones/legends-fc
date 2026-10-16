// The FC app shell (Oct 10; the game was renamed from Legends FC on Oct 11): loads the game data, keeps the open world (GameSession), and shows one screen at a time
// inside a fixed frame (header with the club and the Continue button, a menu, and the screen's body).
// It starts itself in any scene, so no scene setup is needed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LegendsFC.Core.Data;
using LegendsFC.Core.Saves;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace LegendsFC.App
{
    public sealed class App : MonoBehaviour
    {
        public static App I { get; private set; }

        public GameData Data { get; private set; }
        public SaveStore Store { get; private set; }
        public GameSession Session { get; set; }

        private RectTransform _header, _menu, _body, _overlay;
        private Text _headerTitle, _headerInfo, _overlayText, _toast;
        private Button _continue;
        private float _toastUntil;
        public UIScreen Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("The FC");
            DontDestroyOnLoad(go);
            go.AddComponent<App>();
        }

        private async void Start()
        {
            I = this;
#if UNITY_WEBGL && !UNITY_EDITOR
            Application.targetFrameRate = -1;   // in a browser, -1 follows the screen's refresh (requestAnimationFrame); 60 would use a timer
#else
            Application.targetFrameRate = 60;
#endif
            Application.runInBackground = true;
            // Landscape only, like the match (confirmed by Carlos, Oct 10): the screens are laid out for 16:9 and wider.
            Screen.autorotateToPortrait = false; Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true; Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;   // the week keeps playing if the app loses focus (and the editor keeps rendering)
            BuildFrame();
            Busy("Loading the game data...");
            try
            {
                string dataDir = DataDirectory();
#if UNITY_WEBGL && !UNITY_EDITOR
                Data = await LoadDataFromWeb(dataDir);
#else
                Data = await Task.Run(() => GameData.LoadFromDirectory(dataDir));
#endif
                int copied = CopyOldWorlds(Application.persistentDataPath);
                if (copied > 0) Debug.Log($"[LFC] copied {copied} save files from the old LegendsFC folder");
                Store = new SaveStore(Application.persistentDataPath, Data.Saves);
                Debug.Log($"[LFC] data loaded from {dataDir}; saves in {Application.persistentDataPath}; screen {Screen.width}x{Screen.height}");
                Busy(null);
                Show(new TitleScreen());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Busy("Couldn't load the game data:\n" + e.Message);
            }
        }

        /// <summary>The game's name on screen (Carlos, Oct 11: "The FC", not "Legends FC").</summary>
        public const string GameName = "The FC";

        /// <summary>
        /// The app was renamed (Oct 11), and on Windows and in the editor the save folder follows the product name, so worlds saved
        /// under the old name would seem to vanish. Copies every file from the old folder that the new one doesn't have yet.
        /// The old folder is left untouched (never lose a save). Returns the number of files copied.
        /// </summary>
        private static int CopyOldWorlds(string current)
        {
            try
            {
                string trimmed = current.TrimEnd('/', '\\');
                string parent = Path.GetDirectoryName(trimmed);
                if (string.IsNullOrEmpty(parent)) return 0;
                string old = Path.Combine(parent, "LegendsFC");
                if (string.Equals(Path.GetFullPath(old), Path.GetFullPath(trimmed), StringComparison.OrdinalIgnoreCase)) return 0;
                string oldWorlds = Path.Combine(old, "worlds");
                if (!Directory.Exists(oldWorlds)) return 0;
                int n = 0;
                foreach (string file in Directory.GetFiles(oldWorlds, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(trimmed, "worlds", file.Substring(oldWorlds.Length).TrimStart('/', '\\'));
                    if (File.Exists(target)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(file, target);
                    n++;
                }
                return n;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LFC] couldn't copy the old saves: " + e.Message);
                return 0;
            }
        }

        /// <summary>The editor reads the repo's data folder; builds read the copy placed in StreamingAssets at build time.</summary>
        private static string DataDirectory()
        {
            if (Application.isEditor) return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "data"));
            if (Application.platform == RuntimePlatform.WebGLPlayer) return Application.streamingAssetsPath + "/data";   // a web address
            return Path.Combine(Application.streamingAssetsPath, "data");
        }

        /// <summary>
        /// Web version (Oct 15): a browser can't read files, so this downloads the file list the build wrote (data/files.txt,
        /// see DataForBuilds) and then every file on it, and gives GameData.Load the downloaded texts.
        /// </summary>
        private static async Task<GameData> LoadDataFromWeb(string root)
        {
            string list = await Download(root + "/files.txt");
            var paths = list.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            var downloads = paths.Select(x => Download(root + "/" + x)).ToList();   // all at once
            var texts = new Dictionary<string, string>();
            for (int i = 0; i < paths.Count; i++) texts[paths[i]] = await downloads[i];
            Debug.Log($"[LFC] downloaded {texts.Count} data files");
            return GameData.Load(rel => texts.TryGetValue(rel, out var text) ? text : throw new FileNotFoundException("Not in the web version's data: " + rel));
        }

        private static async Task<string> Download(string url)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                var op = req.SendWebRequest();
                while (!op.isDone) await Awaitable.NextFrameAsync();
                if (req.result != UnityWebRequest.Result.Success) throw new IOException($"Couldn't download {url}: {req.error}");
                return req.downloadHandler.text;
            }
        }

        /// <summary>
        /// Runs heavy work behind the busy overlay. On Windows and in the editor it runs on a worker thread; a browser has no
        /// threads for the game, so the web version waits one frame (the overlay gets drawn) and then runs it on the main thread.
        /// </summary>
        private static async Task<T> Work<T>(Func<T> work)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            await Awaitable.NextFrameAsync();
            return work();
#else
            return await Task.Run(work);
#endif
        }

        /// <summary>
        /// Web version: saves live in the browser's storage (IndexedDB). This asks the browser to write them now, after the
        /// game has saved, so closing the tab doesn't lose them. Does nothing elsewhere.
        /// </summary>
        public static void FlushSaves()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { LfcSyncFs(); } catch (Exception e) { Debug.LogWarning("[LFC] couldn't flush the saves: " + e.Message); }
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern void LfcSyncFs();
#endif

        public static string NowUtc() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // ---- frame

        private GameObject _frame;

        /// <summary>Hides the menus while the 3D match is on screen.</summary>
        public void ShowFrame(bool visible) { if (_frame != null) _frame.SetActive(visible); }

        private void BuildFrame()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                DontDestroyOnLoad(es);
            }
            var canvasGo = _frame = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)canvasGo.transform;

            var bg = UIKit.Box(root, UIKit.Bg, "Background"); UIKit.Fill(bg.rectTransform);

            // Safe area (notches) for phones.
            var safe = UIKit.Node("Safe", root); UIKit.Fill(safe);
            safe.gameObject.AddComponent<SafeArea>();

            var header = UIKit.Box(safe, UIKit.Panel, "Header");
            _header = header.rectTransform;
            _header.anchorMin = new Vector2(0, 1); _header.anchorMax = new Vector2(1, 1); _header.pivot = new Vector2(0.5f, 1);
            _header.offsetMin = new Vector2(0, -110); _header.offsetMax = Vector2.zero;
            _headerTitle = UIKit.Label(_header, GameName, 36, UIKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Fill(_headerTitle.rectTransform, 30, 8, 900, 48);
            _headerInfo = UIKit.Label(_header, "", 24, UIKit.Muted);
            UIKit.Fill(_headerInfo.rectTransform, 30, 60, 420, 8);
            _continue = UIKit.Button(_header, "Continue", OnContinue, UIKit.Accent, 32);
            var cr = (RectTransform)_continue.transform;
            cr.anchorMin = new Vector2(1, 0.5f); cr.anchorMax = new Vector2(1, 0.5f); cr.pivot = new Vector2(1, 0.5f);
            cr.sizeDelta = new Vector2(320, 80); cr.anchoredPosition = new Vector2(-24, 0);

            var menu = UIKit.Box(safe, UIKit.Panel, "Menu");
            _menu = menu.rectTransform;
            _menu.anchorMin = new Vector2(0, 0); _menu.anchorMax = new Vector2(0, 1); _menu.pivot = new Vector2(0, 0.5f);
            _menu.offsetMin = new Vector2(0, 0); _menu.offsetMax = new Vector2(250, -116);
            var mv = _menu.gameObject.AddComponent<VerticalLayoutGroup>();
            mv.padding = new RectOffset(12, 12, 16, 16); mv.spacing = 8;
            mv.childControlWidth = true; mv.childControlHeight = true; mv.childForceExpandWidth = true; mv.childForceExpandHeight = false;

            _body = UIKit.Node("Body", safe);
            UIKit.Fill(_body, 274, 132, 24, 16);

            _overlay = UIKit.Box(root, new Color(0, 0, 0, 0.75f), "Overlay").rectTransform;
            UIKit.Fill(_overlay);
            _overlayText = UIKit.Label(_overlay, "", 36, UIKit.Ink, TextAnchor.MiddleCenter);
            UIKit.Fill(_overlayText.rectTransform, 200, 200, 200, 200);
            _overlay.gameObject.SetActive(false);

            var toastBox = UIKit.Box(root, UIKit.Panel2, "Toast");
            var trt = toastBox.rectTransform; trt.anchorMin = new Vector2(0.5f, 0); trt.anchorMax = new Vector2(0.5f, 0); trt.pivot = new Vector2(0.5f, 0);
            trt.sizeDelta = new Vector2(1150, 76); trt.anchoredPosition = new Vector2(120, 36);
            toastBox.raycastTarget = false;
            _toast = UIKit.Label(toastBox.transform, "", 28, UIKit.Ink, TextAnchor.MiddleCenter);
            UIKit.Fill(_toast.rectTransform, 20, 4, 20, 4);
            toastBox.gameObject.SetActive(false);
        }

        private void Update()
        {
            var box = _toast.transform.parent.gameObject;
            if (box.activeSelf && Time.unscaledTime > _toastUntil) box.SetActive(false);
        }

        /// <summary>A short message at the bottom of the screen.</summary>
        public void Toast(string text, float seconds = 3f)
        {
            _toast.text = text; _toast.transform.parent.gameObject.SetActive(true); _toast.transform.parent.SetAsLastSibling(); _toastUntil = Time.unscaledTime + seconds;
            Debug.Log("[LFC] " + text);
        }

        /// <summary>Blocks the screen with a message while the game works (null hides it).</summary>
        public void Busy(string text)
        {
            _overlay.gameObject.SetActive(text != null);
            _overlay.SetAsLastSibling();
            if (text != null) _overlayText.text = text;
        }

        public void Show(UIScreen screen)
        {
            Current?.OnHide();
            Current = screen;
            UIKit.Clear(_body);
            RefreshFrame();
            try { screen.Build(_body); }
            catch (Exception e) { Debug.LogException(e); UIKit.Note(_body, "This screen failed: " + e.Message, UIKit.Bad); }
            Debug.Log("[LFC] screen " + screen.GetType().Name);
        }

        public void Refresh() { if (Current != null) Show(Current); }

        public void RefreshFrame()
        {
            bool inGame = Session != null && Current != null && Current.InGame;
            _menu.gameObject.SetActive(inGame);
            _continue.gameObject.SetActive(inGame);
            _body.offsetMin = new Vector2(inGame ? 274 : 24, 16);
            UIKit.Clear(_menu);
            if (inGame)
            {
                var w = Session.World;
                var club = Session.UserClub;
                _headerTitle.text = club?.Name ?? "No club";
                var cal = w.Calendar;
                string window = w.Market.Window != null ? $" | {char.ToUpper(w.Market.Window[0]) + w.Market.Window.Substring(1)} window open" : "";
                _headerInfo.text = club == null
                    ? $"Season {Fmt.Season(w.SeasonStartYear)} | week {cal.Week} | out of work"
                    : $"Season {Fmt.Season(w.SeasonStartYear)} | week {cal.Week} of 52 | {Fmt.Money(club.Balance)} | board {Session.Career.Confidence:0}/100{window}";
                int unread = w.Inbox.Messages.Count(m => !m.Read);
                foreach (var (label, make) in Menu())
                {
                    bool on = Current != null && Current.GetType() == make().GetType();
                    string text = label == "Inbox" && unread > 0 ? $"Inbox ({unread})" : label;
                    UIKit.Button(_menu, text, () => Show(make()), on ? UIKit.AccentDark : UIKit.Panel2, 26, -1, 66);
                }
                UIKit.Gap(_menu, 20);
                UIKit.Button(_menu, "Save and exit", () => { Session.Save(NowUtc()); FlushSaves(); Session = null; Show(new TitleScreen()); }, UIKit.Panel2, 22, -1, 56);
            }
            else
            {
                _headerTitle.text = GameName;
                _headerInfo.text = "First playable (placeholder screens)";
            }
        }

        private static IEnumerable<(string, Func<UIScreen>)> Menu()
        {
            yield return ("Home", () => new HomeScreen());
            yield return ("Squad", () => new SquadScreen());
            yield return ("Tactics", () => new TacticsScreen());
            yield return ("Transfers", () => new TransfersScreen());
            yield return ("Club", () => new ClubScreen());
            yield return ("Inbox", () => new InboxScreen());
            yield return ("League", () => new LeagueScreen());
        }

        // ---- the week

        private bool _running;

        /// <summary>
        /// Continue: when the user's club plays this week, ask first whether to play the match or simulate it (Oct 11).
        /// The sim limit (1 simulated per 2-3 played, backlog C-04) is not enforced yet.
        /// </summary>
        private void OnContinue()
        {
            if (_running || Session == null) return;
            var fx = Session.NextUserFixture();
            if (fx != null && !(Current is PreMatchScreen)) { Show(new PreMatchScreen(fx)); return; }
            PlayWeek(null);
        }

        /// <summary>Simulates the week, the user's match included.</summary>
        public void SimulateWeek() => PlayWeek(null);

        /// <summary>Plays the user's match in 3D, then the week with that result.</summary>
        public void PlayLive(LegendsFC.Core.Squad.UserFixture fx)
        {
            if (_running || Session == null) return;
            var s = Session;
            ulong seed = (ulong)(s.World.SeasonStartYear * 100 + fx.Week) * 1000003UL + (ulong)(uint)(fx.Fixture.Home + fx.Fixture.Away).GetHashCode();
            var setup = LegendsFC.Core.Match.LiveMatchFactory.FromSheets(s.World, Data, fx.CompetitionId, fx.HomeSheet, fx.AwaySheet,
                new LegendsFC.Core.Match.MatchSettings { UserSide = fx.UserIsHome ? 0 : 1 }, seed);
            setup.ExtraTimeIfLevel = fx.Decide && fx.ExtraTime;
            if (fx.FirstLeg.HasValue)
            {
                // the first leg's own result; the engine swaps it for the aggregate, like the season code does
                setup.FirstLegHomeGoals = fx.FirstLeg.Value.HomeGoals;
                setup.FirstLegAwayGoals = fx.FirstLeg.Value.AwayGoals;
            }
            setup.CompetitionId = fx.CompetitionId;
            ShowFrame(false);
            Match3D.Begin(setup, Data.Match, result =>
            {
                ShowFrame(true);
                PlayWeek(LegendsFC.Core.Match.LiveMatchToSeason.ToPlayedMatch(result));
            });
        }

        /// <summary>A match with two test teams (rated 70), outside any world, for trying the controls.</summary>
        public void QuickMatch(bool watch)
        {
            var c = Data.Match;
            var setup = new LegendsFC.Core.Match.MatchSetup
            {
                CompetitionId = "QUICK",
                Home = LegendsFC.Core.Match.LiveMatchFactory.TestTeam(c, "Blues", "4-4-2", 70, 11),
                Away = LegendsFC.Core.Match.LiveMatchFactory.TestTeam(c, "Reds", "4-3-3", 70, 12),
                Seed = (ulong)DateTime.UtcNow.Ticks,
                Settings = new LegendsFC.Core.Match.MatchSettings { UserSide = watch ? -1 : 0 }
            };
            ShowFrame(false);
            Match3D.Begin(setup, c, result =>
            {
                ShowFrame(true);
                Show(new TitleScreen());
                Toast($"Quick match: Blues {result.HomeGoals} - {result.AwayGoals} Reds");
            });
        }

        private async void PlayWeek(LegendsFC.Core.Squad.PlayedMatch played)
        {
            if (_running || Session == null) return;
            _running = true;
            Busy("Playing the week...");
            try
            {
                var s = Session;
                int season = s.World.SeasonStartYear;
                string now = NowUtc();
                var report = await Work(() => s.AdvanceWeek(now, played));
                FlushSaves();   // the week autosaves
                Busy(null);
                bool seasonEnded = s.World.SeasonStartYear != season;
                if (report.Sacked || s.World.UserClubId == null) Show(new JobsScreen());
                else if (report.UserMatches.Count > 0) Show(new MatchScreen(report.UserMatches, s.World.SeasonStartYear == season ? null : season));
                else if (seasonEnded) Show(new SeasonSummaryScreen());
                else Show(Current is MatchScreen || Current is PreMatchScreen || Current == null ? new HomeScreen() : Current);
                if (s.NewMessages.Count > 0) Toast($"{s.NewMessages.Count} new message{(s.NewMessages.Count > 1 ? "s" : "")} in your inbox");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Busy(null);
                Toast("Something went wrong: " + e.Message, 6);
            }
            finally { _running = false; }
        }

        /// <summary>Runs work off the main thread behind the busy overlay (on the web: after one frame, on the main thread), then continues on the main thread.</summary>
        public async void Run<T>(string message, Func<T> work, Action<T> then)
        {
            Busy(message);
            try
            {
                var result = await Work(work);
                FlushSaves();   // new worlds and loads may have written save files
                Busy(null);
                then(result);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Busy(null);
                Toast("Something went wrong: " + e.Message, 6);
            }
        }
    }

    /// <summary>Keeps the UI inside the phone's safe area (notch, home bar).</summary>
    public sealed class SafeArea : MonoBehaviour
    {
        private Rect _last;
        private void Update()
        {
            var area = Screen.safeArea;
            if (area == _last || Screen.width == 0) return;
            _last = area;
            var r = (RectTransform)transform;
            r.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            r.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }
    }

    /// <summary>One screen of the game. Build() fills the body; InGame screens show the menu and the Continue button.</summary>
    public abstract class UIScreen
    {
        protected App A => App.I;
        protected GameSession S => App.I.Session;
        protected GameData D => App.I.Data;
        public virtual bool InGame => true;
        public abstract void Build(RectTransform body);
        public virtual void OnHide() { }
    }
}
