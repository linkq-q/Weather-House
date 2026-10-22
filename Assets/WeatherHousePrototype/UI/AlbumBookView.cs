using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WeatherHousePrototype
{
    public static class AlbumBookView
    {
        public const int GlyphsPerPage = 22;
        public static readonly Vector2 ReferenceSize = new Vector2(1672f, 941f);
        private static readonly Rect[] Frames =
        {
            new Rect(285f, 145f, 263f, 327f), new Rect(560f, 155f, 241f, 320f),
            new Rect(279f, 477f, 270f, 322f), new Rect(555f, 480f, 245f, 318f)
        };
        private static readonly Rect[] Photos =
        {
            new Rect(301f, 159f, 231f, 293f), new Rect(577f, 173f, 207f, 281f),
            new Rect(291f, 488f, 239f, 286f), new Rect(568f, 492f, 219f, 291f)
        };
        private static readonly float[] Columns = { 950f, 1068f, 1180f, 1294f };
        private static readonly float[] Rows = { 217f, 317f, 418f, 518f, 620f, 720f };
        private static readonly Color[] Papers =
        {
            new Color32(207, 215, 188, 255), new Color32(195, 213, 213, 255),
            new Color32(242, 224, 188, 255)
        };
        private static readonly int[] PaperOrder =
        {
            0, 0, 1, 2, 2, 0, 2, 0, 1, 2, 0, 1, 0, 2, 0, 2, 1, 0, 0, 0, 0, 2
        };

        public static RectTransform Create(Transform parent, bool photosSelected,
            UnityAction selectSymbols, UnityAction selectPhotos, UnityAction close, UnityAction flipGlyphs)
        {
            var book = Node(parent, "Notebook", Vector2.zero, ReferenceSize);
            ArtButton(book, "Symbols tab", photosSelected ? "TabSymbolsIdle" : "TabSymbolsSelected",
                new Rect(261f, 75f, 82f, 69f), selectSymbols);
            ArtButton(book, "Photos tab", photosSelected ? "TabPhotosSelected" : "TabPhotosIdle",
                new Rect(342f, 75f, 82f, 69f), selectPhotos);
            ArtButton(book, "Close album", "ArrowLeft", new Rect(148f, 446f, 88f, 71f), close);
            Art(book, "Book artwork", "Book", new Rect(211f, 108f, 1228f, 742f));
            var pageTurn = Place(book, "Next symbols", new Rect(1170f, 682f, 180f, 120f)).gameObject.AddComponent<Image>();
            pageTurn.color = Color.clear;
            Bind(pageTurn, flipGlyphs);
            return book;
        }

        public static RawImage Photo(Transform book, int slot, Texture2D texture, string name, UnityAction click = null)
        {
            if (slot < 0 || slot >= Photos.Length) throw new ArgumentOutOfRangeException(nameof(slot));
            Art(book, "Photo frame " + (slot + 1), "Frame", Frames[slot]);
            if (texture == null)
                texture = Resources.Load<Texture2D>("AlbumBook/" + (click == null ? "PhotoEmpty" : "PhotoMissing"));
            RawImage photo = Place(book, name, Photos[slot]).gameObject.AddComponent<RawImage>();
            photo.texture = texture;
            photo.raycastTarget = click != null;
            // Crop the source to fill the opening while preserving its original proportions.
            float sourceAspect = texture == null ? 1f : texture.width / (float)texture.height;
            float targetAspect = Photos[slot].width / Photos[slot].height;
            photo.uvRect = sourceAspect > targetAspect
                ? new Rect((1f - targetAspect / sourceAspect) * .5f, 0f, targetAspect / sourceAspect, 1f)
                : new Rect(0f, (1f - sourceAspect / targetAspect) * .5f, 1f, sourceAspect / targetAspect);
            if (texture == null) photo.color = Color.clear;
            if (click != null) Bind(photo, click);
            return photo;
        }

        public static Button Word(Transform book, int slot, GlyphDefinition glyph, bool visible,
            bool selected, UnityAction click, float stroke = 5f)
        {
            if (slot < 0 || slot >= GlyphsPerPage) throw new ArgumentOutOfRangeException(nameof(slot));
            if (glyph == null) throw new ArgumentNullException(nameof(glyph));
            var root = Place(book, "Word " + glyph.id,
                new Rect(Columns[slot % 4] - 43f, Rows[slot / 4] - 44f, 86f, 88f));
            var paper = root.gameObject.AddComponent<AlbumPaperCircleGraphic>();
            paper.seed = slot;
            paper.color = selected ? Color.Lerp(Papers[PaperOrder[slot]], new Color32(161, 175, 136, 255), .24f)
                : Papers[PaperOrder[slot]];
            Button button = Bind(paper, click);
            var symbol = Node(root, "Glyph " + glyph.id, Vector2.zero, new Vector2(53f, 69f))
                .gameObject.AddComponent<AnimalGlyphGraphic>();
            symbol.SvgBody = glyph.body;
            symbol.lineWidth = stroke * 1.25f;
            symbol.color = new Color32(47, 73, 56, 255);
            symbol.raycastTarget = false;
            symbol.gameObject.SetActive(visible);
            return button;
        }

        public static void Count(Transform book, int found, int total, Font font)
        {
            Number(book, "Collected count", found.ToString(), new Rect(1182f, 665f, 105f, 105f),
                76, new Color32(126, 145, 93, 255), FontStyle.BoldAndItalic, font);
            Number(book, "Total count", "/" + total, new Rect(1252f, 729f, 99f, 61f),
                45, new Color32(190, 158, 102, 255), FontStyle.Italic, font);
        }

        public static void AddStickers(Transform book)
        {
            Art(book, "Photo sticker 2", "Sticker", new Rect(750f, 420f, 54f, 50f)).uvRect = new Rect(1f, 0f, -1f, 1f);
            Art(book, "Photo sticker 3", "Sticker", new Rect(269f, 742f, 70f, 60f));
        }

        private static RawImage Art(Transform book, string name, string resource, Rect bounds)
        {
            Texture2D texture = Resources.Load<Texture2D>("AlbumBook/" + resource);
            if (texture == null)
                throw new InvalidOperationException("Album UI asset is missing: Resources/AlbumBook/" + resource + ".png");
            var image = Place(book, name, bounds).gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            return image;
        }

        private static Button ArtButton(Transform book, string name, string resource, Rect bounds, UnityAction click)
        {
            return Bind(Art(book, name, resource, bounds), click);
        }

        private static Button Bind(Graphic graphic, UnityAction click)
        {
            graphic.raycastTarget = true;
            var button = graphic.gameObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, .98f, .92f);
            colors.pressedColor = new Color(.86f, .88f, .80f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = .12f;
            button.colors = colors;
            if (click != null) button.onClick.AddListener(click);
            return button;
        }

        private static void Number(Transform book, string name, string value, Rect bounds,
            int size, Color color, FontStyle style, Font font)
        {
            var label = Place(book, name, bounds).gameObject.AddComponent<Text>();
            label.text = value;
            label.font = font;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
        }

        private static RectTransform Place(Transform book, string name, Rect bounds)
        {
            return Node(book, name,
                new Vector2(bounds.center.x - ReferenceSize.x * .5f, ReferenceSize.y * .5f - bounds.center.y), bounds.size);
        }

        private static RectTransform Node(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var node = new GameObject(name, typeof(RectTransform));
            node.layer = 5;
            var rect = (RectTransform)node.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AlbumPaperCircleGraphic : MaskableGraphic
    {
        public int seed;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            const int segments = 72;
            Rect rect = GetPixelAdjustedRect();
            Vector2 radius = rect.size * .5f - Vector2.one;
            Color transparent = color;
            transparent.a = 0f;
            mesh.AddVert(rect.center, color, Vector2.zero);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float edge = 1f + .012f * Mathf.Sin(angle * 5f + seed) + .008f * Mathf.Sin(angle * 13f + seed * 2f);
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 position = rect.center + Vector2.Scale(direction, radius) * edge;
                Color paper = color * (1f - .012f * (1f + Mathf.Sin(angle * 7f + seed)));
                paper.a = color.a;
                mesh.AddVert(position, paper, Vector2.zero);
                mesh.AddVert(position + direction * .8f, transparent, Vector2.zero);
            }
            for (int i = 0; i < segments; i++)
            {
                int current = 1 + i * 2;
                int next = 1 + (i + 1) % segments * 2;
                mesh.AddTriangle(0, current, next);
                mesh.AddTriangle(current, current + 1, next);
                mesh.AddTriangle(current + 1, next + 1, next);
            }
        }
    }
}
