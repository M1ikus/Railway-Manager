using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RailwayManager.SharedUI
{
    public enum UIShapePreset
    {
        PanelLarge,
        Panel,
        Inset,
        Button,
        Pill,
        Tab
    }

    public static partial class UITheme
    {
        private static readonly Dictionary<int, Sprite> RoundedSpriteCache = new();

        public static void ApplySurface(Image image, Color color, UIShapePreset shape)
        {
            if (image == null)
                return;

            image.sprite = GetRoundedSprite(shape);
            image.type = Image.Type.Sliced;
            image.color = color;
            image.preserveAspect = false;
        }

        public static Sprite GetRoundedSprite(UIShapePreset shape)
        {
            int radius = Mathf.RoundToInt(GetCornerRadius(shape));
            if (RoundedSpriteCache.TryGetValue(radius, out Sprite sprite) && sprite != null)
                return sprite;

            sprite = CreateRoundedSprite(radius);
            RoundedSpriteCache[radius] = sprite;
            return sprite;
        }

        public static float GetCornerRadius(UIShapePreset shape)
        {
            return shape switch
            {
                UIShapePreset.PanelLarge => 18f,
                UIShapePreset.Panel => 16f,
                UIShapePreset.Inset => 12f,
                UIShapePreset.Button => 12f,
                UIShapePreset.Pill => 20f,
                UIShapePreset.Tab => 16f,
                _ => 12f
            };
        }

        private static Sprite CreateRoundedSprite(int radius)
        {
            int clampedRadius = Mathf.Clamp(radius, 4, 24);
            int size = Mathf.Max(32, clampedRadius * 4);
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.name = $"UI Rounded {clampedRadius}";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color32[] pixels = new Color32[size * size];
            Color32 solid = new Color32(255, 255, 255, 255);
            Color32 clear = new Color32(255, 255, 255, 0);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[(y * size) + x] = IsInsideRoundedRect(x, y, size, clampedRadius) ? solid : clear;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(clampedRadius, clampedRadius, clampedRadius, clampedRadius));
        }

        private static bool IsInsideRoundedRect(int x, int y, int size, int radius)
        {
            int max = size - 1;

            if (x >= radius && x <= max - radius)
                return true;
            if (y >= radius && y <= max - radius)
                return true;

            float px = x + 0.5f;
            float py = y + 0.5f;

            if (x < radius && y < radius)
                return DistanceSquared(px, py, radius, radius) <= radius * radius;

            if (x > max - radius && y < radius)
                return DistanceSquared(px, py, size - radius, radius) <= radius * radius;

            if (x < radius && y > max - radius)
                return DistanceSquared(px, py, radius, size - radius) <= radius * radius;

            if (x > max - radius && y > max - radius)
                return DistanceSquared(px, py, size - radius, size - radius) <= radius * radius;

            return true;
        }

        private static float DistanceSquared(float ax, float ay, float bx, float by)
        {
            float dx = ax - bx;
            float dy = ay - by;
            return (dx * dx) + (dy * dy);
        }
    }
}
