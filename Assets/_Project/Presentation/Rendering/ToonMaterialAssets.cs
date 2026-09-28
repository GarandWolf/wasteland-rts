using UnityEngine;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Checked-in unit materials. First-open setup also adds the shaders to
    /// Always Included Shaders so player stripping cannot drop them.
    /// </summary>
    public static class ToonMaterialAssets
    {
        public const string VatPath = "Assets/_Project/Art/Materials/M_Toon_VAT.mat";
        public const string StaticPath = "Assets/_Project/Art/Materials/M_Toon_Static.mat";

        public static Material LoadVatTemplate()
        {
            return Load(VatPath, "M_Toon_VAT");
        }

        public static Material LoadStaticTemplate()
        {
            return Load(StaticPath, "M_Toon_Static");
        }

        public static Material InstantiateVat()
        {
            return InstantiateFrom(LoadVatTemplate(), "M_Toon_Infantry");
        }

        public static Material InstantiateStatic()
        {
            return InstantiateFrom(LoadStaticTemplate(), "M_Toon_Vehicle");
        }

        private static Material Load(string assetPath, string resourcesName)
        {
#if UNITY_EDITOR
            Material editor = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (editor != null)
            {
                return editor;
            }
#endif
            return Resources.Load<Material>(resourcesName);
        }

        private static Material InstantiateFrom(Material template, string name)
        {
            if (template == null || template.shader == null)
            {
                Debug.LogError(
                    "Missing checked-in toon material '" + name +
                    "'. Confirm Assets/_Project/Art/Materials exists and Always Included Shaders lists Wasteland/Toon*.");
                return null;
            }

            return new Material(template) { name = name, enableInstancing = true };
        }
    }
}
