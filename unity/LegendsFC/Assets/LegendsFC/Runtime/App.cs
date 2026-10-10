// Legends FC app shell (Oct 10): loads the game data, keeps the open world (GameSession), and shows one screen at a time
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
            var go = new GameObject("Legends FC");
            DontDestroyOnLoad(go);
            go.AddComponent<App>();
        }

        private async void Start()
        {
            I = this;
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            // Landscape only, like the match (PROPOSAL Oct 10): the screens are laid out for 16:9 and wider.
            Screen.autorotateToPortrait = false; Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true; Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;   // the week keeps playing if the app loses focus (and the editor keeps rendering)
            BuildFrame();
            Busy("Loading the game data...");
            try
            {
                string dataDir = DataDirectory();
                Data = await Task.Run(() => GameData.LoadFromDirectory(dataDir));
                Store = new SaveStore(Application.persistentDataPath, Data.Saves);
                Debug.Log($"[LFC] data loaded from {dataDir}; saves in {Application.persistentDataPath}");
                Busy(null);
                Show(new TitleScreen());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Busy("Couldn't load the game data:\n" + e.Message);
            }
        }

        /// <summary>The editor reads the repo's data folder; builds read the copy placed in StreamingAssets at build time.</summary>
        private static string DataDirectory()
        {
            if (Application.isEditor) return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "data"));
            return Path.Combine(Application.streamingAssetsPath, "data");
        }

        public static string NowUtc() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // ---- frame

        private void BuildFrame()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                DontDestroyOnLoad(es);
            }
            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
            _headerTitle = UIKit.Label(_header, "Legends FC", 36, UIKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
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
                UIKit.Button(_menu, "Save and exit", () => { Session.Save(NowUtc()); Session = null; Show(new TitleScreen()); }, UIKit.Panel2, 22, -1, 56);
            }
            else
            {
                _headerTitle.text = "Legends FC";
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

        private async void OnContinue()
        {
            if (_running || Session == null) return;
            _running = true;
            Busy("Playing the week...");
            try
            {
                var s = Session;
                int season = s.World.SeasonStartYear;
                string now = NowUtc();
                var report = await Task.Run(() => s.AdvanceWeek(now));
                Busy(null);
                bool seasonEnded = s.World.SeasonStartYear != season;
                if (report.Sacked || s.World.UserClubId == null) Show(new JobsScreen());
                else if (report.UserMatches.Count > 0) Show(new MatchScreen(report.UserMatches, s.World.SeasonStartYear == season ? null : season));
                else if (seasonEnded) Show(new SeasonSummaryScreen());
                else Show(Current is MatchScreen || Current == null ? new HomeScreen() : Current);
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

        /// <summary>Runs work off the main thread behind the busy overlay, then continues on the main thread.</summary>
        public async void Run<T>(string message, Func<T> work, Action<T> then)
        {
            Busy(message);
            try
            {
                var result = await Task.Run(work);
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
