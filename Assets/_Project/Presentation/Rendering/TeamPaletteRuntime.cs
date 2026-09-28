using UnityEngine;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Builds the 2x16 team LUT (16 shade rows, primary/accent columns) and binds it as the global <c>_TeamPaletteTex</c>.
    /// </summary>
    public static class TeamPaletteRuntime
    {
        public const string GlobalTextureName = "_TeamPaletteTex";

        private static Texture2D _texture;
        private static bool _bound;

        public static Texture2D Texture
        {
            get
            {
                EnsureCreated();
                return _texture;
            }
        }

        public static Texture2D EnsureCreated()
        {
            if (_texture != null)
            {
                return _texture;
            }

            _texture = CreateTexture();
            return _texture;
        }

        public static Texture2D CreateTexture()
        {
            Texture2D texture = new Texture2D(
                FactionPalette.Width,
                FactionPalette.Height,
                TextureFormat.RGBA32,
                mipChain: false,
                linear: false)
            {
                name = "TeamPalette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };

            byte[] pixels = new byte[FactionPalette.Width * FactionPalette.Height * 4];
            FactionPalette.FillRgba32(pixels);
            texture.LoadRawTextureData(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return texture;
        }

        public static void BindGlobals()
        {
            Shader.SetGlobalTexture(GlobalTextureName, EnsureCreated());
            _bound = true;
        }

        public static bool IsBound
        {
            get { return _bound && _texture != null; }
        }
    }
}
