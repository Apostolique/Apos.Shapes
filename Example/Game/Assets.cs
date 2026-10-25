using System;
using Apos.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameProject {
    /// <summary>
    /// The drawings and textures the pages share. They're written out in code so the example
    /// needs no files besides the font. A file off disk goes through the same constructors.
    /// </summary>
    public class Assets {
        public Assets(GraphicsDevice graphicsDevice) {
            // Loading parses the file and bakes its outlines, so it happens once. Drawing is cheap.
            Icon = new ShapeSvg(IconSvg);
            NonZero = new ShapeSvg(RulesSvg("nonzero"));
            EvenOdd = new ShapeSvg(RulesSvg("evenodd"));
            Sunrise = new ShapeSvg(SunriseSvg);
            Chart = new ShapeSvg(ChartSvg);
            Heart = new ShapeSvg(HeartSvg);

            Planet = MakePlanet(graphicsDevice, TWColor.Amber200, TWColor.Orange700);
            Moon = MakePlanet(graphicsDevice, TWColor.Gray100, TWColor.Gray500);
            Coins = MakeCoins(graphicsDevice);
        }

        public const int CoinSize = 48;
        public const int CoinFrames = 8;

        public readonly ShapeSvg Icon;
        public readonly ShapeSvg NonZero;
        public readonly ShapeSvg EvenOdd;
        public readonly ShapeSvg Sunrise;
        public readonly ShapeSvg Chart;
        public readonly ShapeSvg Heart;
        public readonly Texture2D Planet;
        public readonly Texture2D Moon;
        public readonly Texture2D Coins;

        // A shaded ball with wavy bands, premultiplied like textures out of the content pipeline.
        private static Texture2D MakePlanet(GraphicsDevice graphicsDevice, Color light, Color dark) {
            const int n = 64;
            var data = new Color[n * n];
            for (int y = 0; y < n; y++) {
                for (int x = 0; x < n; x++) {
                    var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                    float alpha = MathHelper.Clamp((1f - p.Length()) * n * 0.5f, 0f, 1f);
                    float band = 0.5f + 0.5f * MathF.Sin(p.Y * 9f + MathF.Sin(p.X * 3f) * 1.5f);
                    float shade = MathHelper.Clamp(1.15f - Vector2.Distance(p, new Vector2(-0.45f)) * 0.6f, 0.25f, 1f);
                    data[y * n + x] = Premultiply(Vector3.Lerp(dark.ToVector3(), light.ToVector3(), band) * shade, alpha);
                }
            }
            var texture = new Texture2D(graphicsDevice, n, n);
            texture.SetData(data);
            return texture;
        }

        // A strip of coin frames, each turned a little further around its vertical axis.
        private static Texture2D MakeCoins(GraphicsDevice graphicsDevice) {
            const int n = CoinSize;
            var data = new Color[n * CoinFrames * n];
            for (int f = 0; f < CoinFrames; f++) {
                float squash = MathF.Max(MathF.Abs(MathF.Cos(MathF.PI * f / CoinFrames)), 0.08f);
                for (int y = 0; y < n; y++) {
                    for (int x = 0; x < n; x++) {
                        var p = new Vector2(((x + 0.5f) / n * 2f - 1f) / squash, (y + 0.5f) / n * 2f - 1f) / 0.9f;
                        float d = p.Length();
                        float alpha = MathHelper.Clamp((1f - d) * n * 0.45f * squash, 0f, 1f);
                        Vector3 gold = d > 0.75f ? TWColor.Amber500.ToVector3() : TWColor.Amber300.ToVector3();
                        float shine = MathHelper.Clamp(1.2f - Vector2.Distance(p, new Vector2(-0.3f, -0.4f)) * 0.5f, 0.6f, 1.1f);
                        data[y * n * CoinFrames + f * n + x] = Premultiply(gold * shine, alpha);
                    }
                }
            }
            var texture = new Texture2D(graphicsDevice, n * CoinFrames, n);
            texture.SetData(data);
            return texture;
        }

        private static Color Premultiply(Vector3 rgb, float alpha) {
            return new Color(Vector3.Clamp(rgb, Vector3.Zero, Vector3.One) * alpha) { A = (byte)MathF.Round(alpha * 255f) };
        }

        private const string IconSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
              <rect x="5" y="9" width="54" height="46" rx="8" fill="#0f172a" stroke="#38bdf8" stroke-width="4"/>
              <circle cx="21" cy="24" r="6" fill="#fbbf24"/>
              <path d="M9 51 L26 30 L38 44 L45 36 L57 51 Z" fill="#34d399"/>
            </svg>
            """;
        // A star drawn in one stroke, a disc with a second disc inside it, and two overlapping
        // squares. The fill rules only disagree where an outline covers itself twice.
        private static string RulesSvg(string rule) => $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 300 100">
              <path fill="#f472b6" fill-rule="{rule}" d="M50 5 L76.5 86.4 L7.2 36.1 L92.8 36.1 L23.5 86.4 Z"/>
              <path fill="#c084fc" fill-rule="{rule}" d="M150 50 m-45 0 a45 45 0 1 0 90 0 a45 45 0 1 0 -90 0 Z
                                                         M150 50 m-24 0 a24 24 0 1 0 48 0 a24 24 0 1 0 -48 0 Z"/>
              <path fill="#5eead4" fill-rule="{rule}" d="M212 12 H268 V68 H212 Z M232 32 H288 V88 H232 Z"/>
            </svg>
            """;
        private const string SunriseSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 80">
              <defs>
                <linearGradient id="sky" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="0" y2="80">
                  <stop offset="0" stop-color="#1e1b4b"/>
                  <stop offset="0.55" stop-color="#7c3aed"/>
                  <stop offset="1" stop-color="#fb923c"/>
                </linearGradient>
                <radialGradient id="sun">
                  <stop offset="0" stop-color="#fef08a"/>
                  <stop offset="1" stop-color="#f97316"/>
                </radialGradient>
              </defs>
              <rect x="0" y="0" width="120" height="80" rx="10" fill="url(#sky)"/>
              <circle cx="60" cy="50" r="18" fill="url(#sun)"/>
              <path d="M4 64 L30 64 L46 57 L74 57 L90 64 L116 64" fill="none" stroke="#0f172a"
                    stroke-width="5" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
            """;
        private const string HeartSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
              <path d="M32 56 C8 40 2 24 12 14 C20 6 29 10 32 17 C35 10 44 6 52 14 C62 24 56 40 32 56 Z"
                    fill="#f43f5e" stroke="#fda4af" stroke-width="3" stroke-linejoin="round"/>
              <circle cx="21" cy="22" r="4" fill="#ffe4e6"/>
            </svg>
            """;
        private const string ChartSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 140 80">
              <path d="M10 70 H130" fill="none" stroke="#64748b" stroke-width="3"
                    stroke-dasharray="10 7" stroke-dashoffset="4"/>
              <polyline points="10,58 34,24 58,46 82,14 106,40 130,10" fill="none" stroke="#22d3ee"
                        stroke-width="6" stroke-linecap="round" stroke-linejoin="round"/>
              <circle cx="82" cy="14" r="7" fill="#0f172a" stroke="#22d3ee" stroke-width="4"/>
            </svg>
            """;
    }
}
