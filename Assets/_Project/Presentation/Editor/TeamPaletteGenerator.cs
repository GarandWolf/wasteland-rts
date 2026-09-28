using UnityEditor;
using UnityEngine;
using Wasteland.Presentation;

namespace Wasteland.Presentation.Editor
{
    public static class TeamPaletteGenerator
    {
        public const string DefaultAssetPath = "Assets/_Project/Art/Textures/TeamPalette.png";

        [MenuItem("Wasteland/Art/Generate Team Palette Texture")]
        public static void Generate()
        {
            string path = GenerateTo(DefaultAssetPath);
            EditorUtility.RevealInFinder(path);
            Debug.Log("Wrote team palette to " + path);
        }

        public static string GenerateTo(string assetPath)
        {
            Texture2D texture = TeamPaletteRuntime.CreateTexture();
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            string directory = System.IO.Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            System.IO.File.WriteAllBytes(assetPath, png);
            AssetDatabase.ImportAsset(assetPath);

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.isReadable = false;
                importer.SaveAndReimport();
            }

            return assetPath;
        }
    }
}
