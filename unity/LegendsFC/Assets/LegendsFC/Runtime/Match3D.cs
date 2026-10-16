// The FC 3D match, grey boxes (Oct 11). Draws the core's LiveMatch (LegendsFC.Core.Match) and turns touch, mouse and keyboard
// into its ControlInput. No game rules live here: the core decides everything; this only steps it at 50 Hz and draws it.
// Controls (decided Oct 10): a stick that walks and runs, hold the screen to sprint, 3 buttons in a reverse-L.
// Keyboard (PC testing): WASD or arrows move, Left Shift sprints, J / K / L are the 3 buttons (also Space = first button).
using System;
using LegendsFC.Core.Match;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LegendsFC.App
{
    public sealed class Match3D : MonoBehaviour
    {
        // ---- setup
        private LiveMatch _m;
        private MatchSetup _setup;
        private Action<LiveMatchResult> _onEnd;
        private int _userTeam;           // 0 home, 1 away, -1 watching AI v AI
        private float _acc;
        private bool _ended;

        // ---- drawing
        private GameObject _world;
        private Camera _cam;
        private Camera _hiddenCamera;
        private Transform[] _men;
        private Transform _ball, _ballShadow, _marker;
        private readonly Vector3[] _prev = new Vector3[23], _cur = new Vector3[23];
        private readonly float[] _prevFace = new float[22], _curFace = new float[22];
        private Vector3 _camLook;

        // ---- HUD and controls (screen pixels)
        private Canvas _hud;
        private Text _score, _clock, _phase;

        // Banners at the top right for what happens in the match (Carlos, Oct 15): newest on top, each shown a few seconds.
        private const int BannerSlots = 3;
        private const float BannerSeconds = 2.6f;
        private readonly RectTransform[] _bannerBox = new RectTransform[BannerSlots];
        private readonly Image[] _bannerBg = new Image[BannerSlots], _bannerStrip = new Image[BannerSlots];
        private readonly Text[] _bannerText = new Text[BannerSlots];
        private readonly string[] _bannerMsg = new string[BannerSlots];
        private readonly Color[] _bannerColor = new Color[BannerSlots];
        private readonly float[] _bannerAge = new float[BannerSlots];
        private int _eventsSeen;
        private RectTransform _stickBase, _stickKnob;
        private readonly RectTransform[] _buttons = new RectTransform[3];
        private readonly Text[] _buttonText = new Text[3];
        private readonly Image[] _buttonImage = new Image[3];
        private GameObject _endPanel;
        private Text _endText;
        private int _stickTouch = -2;     // -2 none, -1 mouse, else touch id
        private Vector2 _stickOrigin, _stickNow;
        private ControlInput _input;
        private float _fps, _fpsTimer; private int _fpsFrames;

        private static readonly Color HomeShirt = new Color(0.20f, 0.42f, 0.90f), AwayShirt = new Color(0.86f, 0.22f, 0.20f);
        private static readonly Color HomeKeeper = new Color(0.55f, 0.85f, 0.25f), AwayKeeper = new Color(0.15f, 0.15f, 0.15f);

        /// <summary>Starts a match. userTeam: 0 home, 1 away, -1 AI v AI. onEnd gets the result when the user taps Continue at full time.</summary>
        public static Match3D Begin(MatchSetup setup, MatchConfig config, Action<LiveMatchResult> onEnd)
        {
            var go = new GameObject("The FC match");
            var m = go.AddComponent<Match3D>();
            m._setup = setup;
            m._userTeam = setup.Settings != null ? setup.Settings.UserSide : -1;
            m._onEnd = onEnd;
            m._m = new LiveMatch(setup, config);
            m.BuildWorld(config);
            m.BuildHud();
            m.Snapshot(m._cur, m._curFace);
            m.Snapshot(m._prev, m._prevFace);
            Debug.Log($"[LFC] match started: {setup.Home.Name} v {setup.Away.Name}, user side {m._userTeam}");
            return m;
        }

        // ------------------------------------------------------------------ world

        private static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color, string name)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            var col = g.GetComponent<Collider>(); if (col != null) Destroy(col);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().material.color = color;
            return g;
        }

        private void Line(Transform t, float x0, float z0, float x1, float z1)
        {
            var a = new Vector3(x0, 0.02f, z0); var b = new Vector3(x1, 0.02f, z1);
            var g = Prim(PrimitiveType.Cube, t, (a + b) / 2, new Vector3(0.12f, 0.01f, Vector3.Distance(a, b)), Color.white, "Line");
            g.transform.localRotation = Quaternion.LookRotation(b - a);
        }

        private void Box(Transform t, float x0, float z0, float x1, float z1)
        {
            Line(t, x0, z0, x1, z0); Line(t, x1, z0, x1, z1); Line(t, x1, z1, x0, z1); Line(t, x0, z1, x0, z0);
        }

        private void BuildWorld(MatchConfig c)
        {
            _world = new GameObject("Pitch");
            _world.transform.SetParent(transform, false);
            var w = _world.transform;
            float L = c.Pitch.Length / 2, W = c.Pitch.Width / 2;

            // grass with mowing stripes, and a darker surround
            Prim(PrimitiveType.Plane, w, new Vector3(0, -0.02f, 0), new Vector3(c.Pitch.Length / 10 + 2, 1, c.Pitch.Width / 10 + 1.4f), new Color(0.16f, 0.36f, 0.16f), "Surround");
            int stripes = 12;
            for (int i = 0; i < stripes; i++)
            {
                float x = -L + (i + 0.5f) * c.Pitch.Length / stripes;
                Prim(PrimitiveType.Plane, w, new Vector3(x, 0, 0), new Vector3(c.Pitch.Length / stripes / 10, 1, c.Pitch.Width / 10),
                    i % 2 == 0 ? new Color(0.22f, 0.52f, 0.20f) : new Color(0.25f, 0.57f, 0.23f), "Grass");
            }

            // markings
            Box(w, -L, -W, L, W);
            Line(w, 0, -W, 0, W);
            for (int i = 0; i < 36; i++)
            {
                float a0 = i * Mathf.PI * 2 / 36, a1 = (i + 1) * Mathf.PI * 2 / 36;
                Line(w, 9.15f * Mathf.Cos(a0), 9.15f * Mathf.Sin(a0), 9.15f * Mathf.Cos(a1), 9.15f * Mathf.Sin(a1));
            }
            foreach (int s in new[] { -1, 1 })
            {
                Box(w, s * L, -20.16f, s * (L - 16.5f), 20.16f);
                Box(w, s * L, -9.16f, s * (L - 5.5f), 9.16f);
                Prim(PrimitiveType.Cylinder, w, new Vector3(s * (L - 11f), 0.02f, 0), new Vector3(0.3f, 0.01f, 0.3f), Color.white, "Spot");

                // goal: posts, bar and a simple net
                float gx = s * L, half = c.Pitch.GoalWidth / 2, h = c.Pitch.GoalHeight, d = c.Pitch.NetDepth;
                Prim(PrimitiveType.Cube, w, new Vector3(gx, h / 2, -half), new Vector3(0.12f, h, 0.12f), Color.white, "Post");
                Prim(PrimitiveType.Cube, w, new Vector3(gx, h / 2, half), new Vector3(0.12f, h, 0.12f), Color.white, "Post");
                Prim(PrimitiveType.Cube, w, new Vector3(gx, h, 0), new Vector3(0.12f, 0.12f, c.Pitch.GoalWidth + 0.12f), Color.white, "Bar");
                var net = new Color(0.85f, 0.85f, 0.85f);
                Prim(PrimitiveType.Cube, w, new Vector3(gx + s * d, h / 2, 0), new Vector3(0.03f, h, c.Pitch.GoalWidth), net, "Net");
                Prim(PrimitiveType.Cube, w, new Vector3(gx + s * d / 2, h, 0), new Vector3(d, 0.03f, c.Pitch.GoalWidth), net, "Net");
                Prim(PrimitiveType.Cube, w, new Vector3(gx + s * d / 2, h / 2, -half), new Vector3(d, h, 0.03f), net, "Net");
                Prim(PrimitiveType.Cube, w, new Vector3(gx + s * d / 2, h / 2, half), new Vector3(d, h, 0.03f), net, "Net");
            }

            // players: a capsule with a small "nose" so you can see where he faces
            _men = new Transform[22];
            var ps = _m.Players;
            for (int i = 0; i < 22; i++)
            {
                bool keeper = ps[i].IsKeeper;
                var shirt = ps[i].Team == 0 ? (keeper ? HomeKeeper : HomeShirt) : (keeper ? AwayKeeper : AwayShirt);
                var root = new GameObject($"Player {i}").transform;
                root.SetParent(w, false);
                Prim(PrimitiveType.Capsule, root, new Vector3(0, 0.9f, 0), new Vector3(0.6f, 0.9f, 0.6f), shirt, "Body");
                Prim(PrimitiveType.Cube, root, new Vector3(0, 1.45f, 0.3f), new Vector3(0.18f, 0.12f, 0.25f), Color.Lerp(shirt, Color.white, 0.5f), "Nose");
                _men[i] = root;
            }
            _ball = Prim(PrimitiveType.Sphere, w, Vector3.zero, Vector3.one * 0.33f, Color.white, "Ball").transform;
            _ballShadow = Prim(PrimitiveType.Cylinder, w, Vector3.zero, new Vector3(0.36f, 0.005f, 0.36f), new Color(0.08f, 0.2f, 0.08f), "Ball shadow").transform;
            _marker = Prim(PrimitiveType.Cylinder, w, Vector3.zero, new Vector3(1.3f, 0.01f, 1.3f), new Color(1f, 0.85f, 0.1f), "Controlled").transform;

            // light and camera (the scene's own camera is switched off during the match)
            if (FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.transform.SetParent(transform, false);
                sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(55, -30, 0);
            }
            _hiddenCamera = Camera.main;
            if (_hiddenCamera != null) _hiddenCamera.enabled = false;
            _cam = new GameObject("Match camera").AddComponent<Camera>();
            _cam.transform.SetParent(transform, false);
            _cam.fieldOfView = 40; _cam.nearClipPlane = 0.3f; _cam.farClipPlane = 400;
            _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0.10f, 0.12f, 0.16f);
            _cam.tag = "MainCamera";
            _camLook = Vector3.zero;
        }

        // ------------------------------------------------------------------ HUD

        private Text HudLabel(Transform parent, string text, int size, TextAnchor align)
        {
            var t = UIKit.Label(parent, text, size, Color.white, align, FontStyle.Bold);
            t.raycastTarget = false;
            var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.8f); o.effectDistance = new Vector2(2, -2);
            return t;
        }

        private static Sprite _round;

        /// <summary>A soft-edged white disc, made once at runtime (no art files needed for grey boxes).</summary>
        private static Sprite Round()
        {
            if (_round != null) return _round;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            float r = n / 2f - 1;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - n / 2f) * (x + 0.5f - n / 2f) + (y + 0.5f - n / 2f) * (y + 0.5f - n / 2f));
                    byte a = (byte)(255 * Mathf.Clamp01(r - d));
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            _round = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            return _round;
        }

        private RectTransform Circle(Transform parent, float size, Color color, string name, out Image image)
        {
            image = UIKit.Box(parent, color, name);
            image.sprite = Round();
            image.raycastTarget = false;
            var r = image.rectTransform;
            r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(size, size);
            return r;
        }

        private void BuildHud()
        {
            var go = new GameObject("Match HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _hud = go.GetComponent<Canvas>();
            _hud.renderMode = RenderMode.ScreenSpaceOverlay;
            _hud.sortingOrder = 50;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 1;
            var root = (RectTransform)go.transform;

            _score = HudLabel(root, "", 40, TextAnchor.UpperCenter);
            UIKit.Fill(_score.rectTransform, 0, 16, 0, 0);
            _clock = HudLabel(root, "", 30, TextAnchor.UpperCenter);
            UIKit.Fill(_clock.rectTransform, 0, 66, 0, 0);
            _phase = HudLabel(root, "", 54, TextAnchor.MiddleCenter);
            UIKit.Fill(_phase.rectTransform, 0, 0, 0, 0);

            for (int i = 0; i < BannerSlots; i++)
            {
                _bannerBg[i] = UIKit.Box(root, new Color(0.05f, 0.07f, 0.1f, 0.8f), "Banner " + i);
                _bannerBg[i].raycastTarget = false;
                var r = _bannerBox[i] = _bannerBg[i].rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(1, 1);
                r.sizeDelta = new Vector2(430, 54);
                r.anchoredPosition = new Vector2(-24, -24 - i * 62);
                _bannerStrip[i] = UIKit.Box(r, Color.white, "Strip");
                _bannerStrip[i].raycastTarget = false;
                var sr = _bannerStrip[i].rectTransform;
                sr.anchorMin = new Vector2(0, 0); sr.anchorMax = new Vector2(0, 1); sr.pivot = new Vector2(0, 0.5f);
                sr.sizeDelta = new Vector2(8, 0); sr.anchoredPosition = Vector2.zero;
                _bannerText[i] = UIKit.Label(r, "", 26, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
                _bannerText[i].raycastTarget = false;
                UIKit.Fill(_bannerText[i].rectTransform, 22, 0, 12, 0);
                _bannerBox[i].gameObject.SetActive(false);
                _bannerAge[i] = BannerSeconds;
            }

            _stickBase = Circle(root, 220, new Color(1, 1, 1, 0.12f), "Stick", out _);
            _stickKnob = Circle(root, 100, new Color(1, 1, 1, 0.35f), "Knob", out _);
            for (int i = 0; i < 3; i++)
            {
                _buttons[i] = Circle(root, 130, new Color(1, 1, 1, 0.22f), "Button " + i, out _buttonImage[i]);
                _buttonText[i] = HudLabel(_buttons[i], "", 24, TextAnchor.MiddleCenter);
                UIKit.Fill(_buttonText[i].rectTransform);
            }

            var panel = UIKit.Box(root, new Color(0, 0, 0, 0.7f), "Full time");
            _endPanel = panel.gameObject;
            var pr = panel.rectTransform; pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f); pr.sizeDelta = new Vector2(760, 360);
            var col = UIKit.Column(panel.transform, 18, 30);
            UIKit.Fill(col);
            _endText = UIKit.Label(col, "", 40, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(_endText, -1, 180);
            UIKit.Button(col, "Continue", Finish, UIKit.Accent, 34, -1, 90);
            _endPanel.SetActive(false);
        }

        // Button layout in screen pixels: a reverse L at the bottom right. Index 0 = A, 1 = B, 2 = C.
        private Vector2 ButtonCentre(int i)
        {
            float s = Mathf.Clamp(Screen.height / 7f, 90, 170), m = s * 0.9f;
            float x = Screen.width - m, y = m;
            if (i == 1) return new Vector2(x, y);                 // B: bottom right corner
            if (i == 0) return new Vector2(x - s * 1.35f, y);     // A: to its left
            return new Vector2(x, y + s * 1.35f);                  // C: above B
        }

        private float ButtonRadius => Mathf.Clamp(Screen.height / 7f, 90, 170) * 0.6f;

        // ------------------------------------------------------------------ input

        private static readonly string[] AttackLabels = { "Pass", "Shoot", "Through" }, DefendLabels = { "Switch", "Tackle", "Press" };

        private bool Attacking
        {
            get
            {
                if (_userTeam < 0) return false;
                var b = _m.Ball;
                return b.Owner >= 0 && _m.Players[b.Owner].Team == _userTeam;
            }
        }

        private int ButtonAt(Vector2 p)
        {
            for (int i = 0; i < 3; i++) if (Vector2.Distance(p, ButtonCentre(i)) <= ButtonRadius * 1.15f) return i;
            return -1;
        }

        private void ReadInput()
        {
            var held = new bool[3];
            bool sprint = false;
            Vector2 stick = Vector2.zero;
            float stickRadius = Mathf.Clamp(Screen.height / 9f, 70, 140);

            // touch: the left 40% of the screen is the stick (it starts where the finger lands); the buttons are held;
            // any other finger held on the screen is sprint.
            var ts = Touchscreen.current;
            bool anyTouch = false;
            if (ts != null)
            {
                foreach (var t in ts.touches)
                {
                    if (!t.press.isPressed) { if (t.touchId.ReadValue() == _stickTouch) _stickTouch = -2; continue; }
                    anyTouch = true;
                    int id = t.touchId.ReadValue();
                    Vector2 pos = t.position.ReadValue();
                    if (id == _stickTouch) { _stickNow = pos; continue; }
                    int b = ButtonAt(pos);
                    if (b >= 0) { held[b] = true; continue; }
                    if (_stickTouch == -2 && pos.x < Screen.width * 0.4f && t.press.wasPressedThisFrame) { _stickTouch = id; _stickOrigin = _stickNow = pos; continue; }
                    sprint = true;
                }
            }

            // mouse (editor and PC): left button works like one finger
            var mouse = Mouse.current;
            if (!anyTouch && mouse != null)
            {
                Vector2 pos = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame && ButtonAt(pos) < 0 && pos.x < Screen.width * 0.4f) { _stickTouch = -1; _stickOrigin = _stickNow = pos; }
                if (mouse.leftButton.isPressed)
                {
                    if (_stickTouch == -1) _stickNow = pos;
                    else { int b = ButtonAt(pos); if (b >= 0) held[b] = true; else sprint = true; }
                }
                else if (_stickTouch == -1) _stickTouch = -2;
            }
            if (_stickTouch != -2) stick = Vector2.ClampMagnitude((_stickNow - _stickOrigin) / stickRadius, 1f);

            // keyboard
            var k = Keyboard.current;
            if (k != null)
            {
                var kv = new Vector2((k.dKey.isPressed || k.rightArrowKey.isPressed ? 1 : 0) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1 : 0),
                                     (k.wKey.isPressed || k.upArrowKey.isPressed ? 1 : 0) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1 : 0));
                if (kv != Vector2.zero) stick = kv.normalized;
                if (k.leftShiftKey.isPressed) sprint = true;
                if (k.jKey.isPressed || k.spaceKey.isPressed) held[0] = true;
                if (k.kKey.isPressed) held[1] = true;
                if (k.lKey.isPressed) held[2] = true;
            }

            // The camera looks from the side, so screen right = pitch +X and screen up = pitch +Y. The core wants the stick
            // relative to the user's attacking direction, which flips at half time.
            float dir = _userTeam >= 0 ? _m.AttackDirection(_userTeam) : 1;
            _input = new ControlInput { MoveX = stick.x * dir, MoveY = stick.y * dir, Sprint = sprint, A = held[0], B = held[1], C = held[2] };

            // controls on screen
            bool show = _userTeam >= 0 && !_ended;
            _stickBase.gameObject.SetActive(show); _stickKnob.gameObject.SetActive(show);
            Vector2 baseAt = _stickTouch != -2 ? _stickOrigin : new Vector2(Screen.width * 0.13f, Screen.height * 0.22f);
            _stickBase.anchoredPosition = baseAt;
            _stickKnob.anchoredPosition = baseAt + stick * stickRadius;
            string[] labels = Attacking ? AttackLabels : DefendLabels;
            for (int i = 0; i < 3; i++)
            {
                _buttons[i].gameObject.SetActive(show);
                _buttons[i].anchoredPosition = ButtonCentre(i);
                _buttons[i].sizeDelta = Vector2.one * ButtonRadius * 2;
                _buttonText[i].text = labels[i];
                _buttonImage[i].color = new Color(1, 1, 1, held[i] ? 0.45f : 0.22f);
            }
        }

        // ------------------------------------------------------------------ loop

        private void Snapshot(Vector3[] pos, float[] face)
        {
            var ps = _m.Players;
            for (int i = 0; i < 22; i++) { pos[i] = new Vector3(ps[i].Pos.X, 0, ps[i].Pos.Y); face[i] = ps[i].Facing; }
            var b = _m.Ball;
            pos[22] = new Vector3(b.Pos.X, b.Pos.Z, b.Pos.Y);
        }

        private void Update()
        {
            if (_m == null) return;
            ReadInput();
            if (!_m.IsOver)
            {
                _acc += Mathf.Min(Time.deltaTime, 0.25f);
                while (_acc >= LiveMatch.TickSeconds && !_m.IsOver)
                {
                    Array.Copy(_cur, _prev, 23); Array.Copy(_curFace, _prevFace, 22);
                    _m.Step(_input);
                    Snapshot(_cur, _curFace);
                    _acc -= LiveMatch.TickSeconds;
                }
            }
            float a = Mathf.Clamp01(_acc / LiveMatch.TickSeconds);
            Draw(a);
            Hud();
            _fpsFrames++; _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 1f) { _fps = _fpsFrames / _fpsTimer; _fpsFrames = 0; _fpsTimer = 0; }
        }

        private void Draw(float a)
        {
            var ps = _m.Players;
            for (int i = 0; i < 22; i++)
            {
                _men[i].localPosition = Vector3.Lerp(_prev[i], _cur[i], a);
                float f = Mathf.LerpAngle(_prevFace[i] * Mathf.Rad2Deg, _curFace[i] * Mathf.Rad2Deg, a);
                _men[i].localRotation = Quaternion.Euler(0, 90 - f, 0);   // facing 0 = +X; Unity yaw 0 = +Z
            }
            var bp = Vector3.Lerp(_prev[22], _cur[22], a);
            _ball.localPosition = bp + Vector3.up * 0.165f;
            _ballShadow.localPosition = new Vector3(bp.x, 0.015f, bp.z);
            int c = _m.ControlledIndex;
            _marker.gameObject.SetActive(c >= 0);
            if (c >= 0) _marker.localPosition = _men[c].localPosition + Vector3.up * 0.02f;

            // broadcast camera from the side: follows the ball along the pitch, eases across it
            // The ball sits near the middle of the screen (the 30-60% height band, D-09): the camera looks at a point that follows
            // the ball along and across the pitch, so the near touchline comes into view when play goes there.
            var target = new Vector3(Mathf.Clamp(bp.x, -40f, 40f), 0, Mathf.Clamp(bp.z * 0.9f, -30f, 30f));
            _camLook = Vector3.Lerp(_camLook, target, 1 - Mathf.Exp(-4f * Time.deltaTime));
            _cam.transform.position = _camLook + new Vector3(0, 22f, -34f);
            _cam.transform.LookAt(_camLook);
        }

        private static string PhaseText(Phase p, LiveMatch m)
        {
            switch (p)
            {
                case Phase.GoalScored: return "GOAL!";
                case Phase.HalfTime: return "Half time";
                case Phase.ExtraTimeBreak: return "Extra time";
                case Phase.FullTime: return "Full time";
                case Phase.KickOff: return m.Minute == 0 && m.Half == 1 ? "Kick-off" : "";
                case Phase.FreeKick: return "";
                default: return "";
            }
        }

        private string PlayerName(int index)
        {
            if (index < 0 || index > 21) return null;
            var team = index < 11 ? _setup.Home : _setup.Away;
            var p = team.Starters[index % 11];
            return p == null ? null : p.Name;
        }

        private string TeamName(int team) => team == 0 ? _setup.Home.Name : team == 1 ? _setup.Away.Name : null;

        private string BannerText(MatchEventInfo e)
        {
            string team = TeamName(e.Team), who = PlayerName(e.Player);
            switch (e.Kind)
            {
                case MatchEventKind.KickOff: return "Kick-off";
                case MatchEventKind.Goal: return $"GOAL!  {who ?? team}  {e.Minute}'";
                case MatchEventKind.OwnGoal: return $"Own goal  {who}  {e.Minute}'";
                case MatchEventKind.Corner: return $"Corner  {team}";
                case MatchEventKind.ThrowIn: return $"Throw-in  {team}";
                case MatchEventKind.GoalKick: return $"Goal kick  {team}";
                case MatchEventKind.Offside: return $"Offside  {who ?? team}";
                case MatchEventKind.FreeKick: return $"Free kick  {team}";
                case MatchEventKind.Save: return $"Save  {who ?? team}";
                case MatchEventKind.Woodwork: return $"Off the woodwork!  {who ?? team}";
                case MatchEventKind.HalfTime: return $"Half time  {_m.HomeGoals}-{_m.AwayGoals}";
                case MatchEventKind.ExtraTime: return "Extra time";
                case MatchEventKind.FullTime: return $"Full time  {_m.HomeGoals}-{_m.AwayGoals}";
                default: return e.Kind.ToString();
            }
        }

        private void PushBanner(string text, Color color)
        {
            for (int i = BannerSlots - 1; i > 0; i--)
            {
                _bannerMsg[i] = _bannerMsg[i - 1]; _bannerColor[i] = _bannerColor[i - 1]; _bannerAge[i] = _bannerAge[i - 1];
            }
            _bannerMsg[0] = text; _bannerColor[0] = color; _bannerAge[0] = 0;
            Debug.Log("[LFC] banner: " + text);
        }

        private void Banners()
        {
            int count = _m.EventCount;
            for (int k = Mathf.Max(_eventsSeen, count - 32); k < count; k++)
            {
                var e = _m.GetEvent(k);
                var c = e.Team == 0 ? HomeShirt : e.Team == 1 ? AwayShirt : new Color(0.9f, 0.9f, 0.9f);
                PushBanner(BannerText(e), c);
            }
            _eventsSeen = count;
            for (int i = 0; i < BannerSlots; i++)
            {
                _bannerAge[i] += Time.deltaTime;
                bool on = _bannerMsg[i] != null && _bannerAge[i] < BannerSeconds;
                _bannerBox[i].gameObject.SetActive(on);
                if (!on) continue;
                float a = Mathf.Clamp01((BannerSeconds - _bannerAge[i]) / 0.4f);   // fade out over the last 0.4 s
                _bannerText[i].text = _bannerMsg[i];
                _bannerText[i].color = new Color(1, 1, 1, a);
                _bannerBg[i].color = new Color(0.05f, 0.07f, 0.1f, 0.8f * a);
                var sc = _bannerColor[i]; sc.a = a; _bannerStrip[i].color = sc;
            }
        }

        private void Hud()
        {
            Banners();
            _score.text = $"{_setup.Home.Name}  {_m.HomeGoals} - {_m.AwayGoals}  {_setup.Away.Name}";
            _clock.text = $"{_m.Minute}'   {(_fps > 0 ? $"{_fps:F0} fps" : "")}";
            _phase.text = PhaseText(_m.Phase, _m);
            if (_m.IsOver && !_ended)
            {
                _ended = true;
                _phase.text = "";
                var r = _m.Result();
                _endText.text = $"Full time\n{_setup.Home.Name} {r.HomeGoals} - {r.AwayGoals} {_setup.Away.Name}" + (r.NeedsShootout ? "\n(penalties decide it)" : "");
                _endPanel.SetActive(true);
                var k = _m.Counters;
                Debug.Log($"[LFC] full time {r.HomeGoals}-{r.AwayGoals}; shots {k.Shots}, saves {k.Saves}, passes {k.Passes}; {_fps:F0} fps");
            }
        }

        private void Finish()
        {
            var r = _m.Result();
            var done = _onEnd;
            _onEnd = null;
            if (_hiddenCamera != null) _hiddenCamera.enabled = true;
            Destroy(gameObject);
            done?.Invoke(r);
        }

        private void OnDestroy()
        {
            if (_hiddenCamera != null) _hiddenCamera.enabled = true;
        }
    }
}
