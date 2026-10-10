// Placeholder UI kit (Oct 10): plain uGUI built from code, so every screen works before the real art and layouts exist.
// When the designs arrive, screens keep calling the same GameSession API and only this look changes.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace LegendsFC.App
{
    public static class UIKit
    {
        public static readonly Color Bg = Hex("0F1520"), Panel = Hex("18212F"), Panel2 = Hex("212C3D"), Line = Hex("2C394D");
        public static readonly Color Accent = Hex("2FBF71"), AccentDark = Hex("23915A"), Ink = Hex("E8EDF5"), Muted = Hex("8E9AAD");
        public static readonly Color Bad = Hex("E5534B"), Warn = Hex("E8B339"), Info = Hex("4C9BE8");

        private static Font _font;
        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Fill(RectTransform r, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top);
            return r;
        }

        public static Image Box(Transform parent, Color color, string name = "Box")
        {
            var r = Node(name, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(Transform parent, string text, int size = 26, Color? color = null, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var r = Node("Label", parent);
            var t = r.gameObject.AddComponent<Text>();
            t.font = Font; t.fontSize = size; t.color = color ?? Ink; t.alignment = align; t.fontStyle = style;
            t.text = text; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        public static Button Button(Transform parent, string text, Action onClick, Color? bg = null, int size = 26, float width = -1, float height = 64)
        {
            var img = Box(parent, bg ?? Panel2, "Button");
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors; colors.highlightedColor = new Color(1, 1, 1, 0.92f); colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1); colors.disabledColor = new Color(1, 1, 1, 0.35f); b.colors = colors;
            var t = Label(img.transform, text, size, null, TextAnchor.MiddleCenter);
            Fill(t.rectTransform, 12, 4, 12, 4);
            if (onClick != null) b.onClick.AddListener(() => onClick());
            Size(img, width, height);
            return b;
        }

        public static LayoutElement Size(Component c, float width = -1, float height = -1, float flexW = -1, float flexH = -1)
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (width >= 0) { le.preferredWidth = width; le.minWidth = width; }
            if (height >= 0) { le.preferredHeight = height; le.minHeight = height; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        /// <summary>A vertical stack that sizes itself to its children.</summary>
        public static RectTransform Column(Transform parent, float spacing = 8, int padding = 0, bool fit = false)
        {
            var r = Node("Column", parent);
            var v = r.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing; v.padding = new RectOffset(padding, padding, padding, padding);
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            if (fit) r.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return r;
        }

        /// <summary>A horizontal row of a fixed height.</summary>
        public static RectTransform Row(Transform parent, float height = 64, float spacing = 8, Color? bg = null, int padding = 0)
        {
            RectTransform r;
            if (bg.HasValue) r = Box(parent, bg.Value, "Row").rectTransform; else r = Node("Row", parent);
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.padding = new RectOffset(padding, padding, 0, 0);
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;
            Size(r, -1, height);
            return r;
        }

        /// <summary>A cell in a row: a label with a fixed width (or flexible when width is 0).</summary>
        public static Text Cell(Transform row, string text, float width, int size = 24, Color? color = null, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var t = Label(row, text, size, color, align, style);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (width > 0) Size(t, width, -1, 0); else Size(t, 0, -1, 1);
            return t;
        }

        /// <summary>A vertical scroll list filling its parent; returns the content to add rows to.</summary>
        public static RectTransform Scroll(Transform parent, float spacing = 6)
        {
            var root = Node("Scroll", parent);
            Fill(root);
            var sr = root.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 40;
            var viewport = Node("Viewport", root);
            Fill(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0);   // catches drags between rows
            var content = Column(viewport, spacing, 0, fit: true);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            sr.viewport = viewport; sr.content = content;
            return content;
        }

        public static InputField Input(Transform parent, string value, string placeholder, float height = 64)
        {
            var img = Box(parent, Panel2, "Input");
            var field = img.gameObject.AddComponent<InputField>();
            var text = Label(img.transform, "", 26);
            Fill(text.rectTransform, 16, 4, 16, 4);
            text.supportRichText = false;
            var ph = Label(img.transform, placeholder, 26, Muted);
            Fill(ph.rectTransform, 16, 4, 16, 4);
            field.textComponent = text; field.placeholder = ph; field.text = value ?? "";
            Size(img, -1, height);
            return field;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        public static Text Title(Transform parent, string text) { var t = Label(parent, text, 34, Ink, TextAnchor.MiddleLeft, FontStyle.Bold); Size(t, -1, 56); return t; }
        public static Text Note(Transform parent, string text, Color? color = null, int size = 24)
        {
            var t = Label(parent, text, size, color ?? Muted);
            t.verticalOverflow = VerticalWrapMode.Overflow;   // a layout group above sizes it to its text
            return t;
        }
        public static void Gap(Transform parent, float h = 12) => Size(Node("Gap", parent), -1, h);
    }
}
