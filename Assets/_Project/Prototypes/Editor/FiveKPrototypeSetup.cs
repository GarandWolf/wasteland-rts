using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wasteland.Presentation;
using Wasteland.Presentation.Editor;

namespace Wasteland.Prototypes.Editor
{
    public static class FiveKPrototypeSetup
    {
        public const string ScenePath = "Assets/_Project/Prototypes/FiveKToonTest.unity";

        [MenuItem("Wasteland/Prototypes/Setup Toon Prototype (First Open)")]
        public static void SetupAll()
        {
            WastelandUrpSetup.CreatePipelineAssets();
            TeamPaletteGenerator.GenerateTo(TeamPaletteGenerator.DefaultAssetPath);
            VatBakerWindow.GeneratePlaceholder();
            CreateOrUpdateScene();
            EditorUtility.DisplayDialog(
                "Wasteland toon prototype",
                "Generated URP Forward+ assets, team palette, placeholder VAT, Always Included Shaders, and the 5k scene.\n\nStill verify:\n1. WastelandURP_Renderer Rendering Path = Forward+\n2. Graphics > Shader Stripping > BatchRendererGroup Variants = Keep All\n3. Always Included Shaders lists Wasteland/Toon*\n4. Shadow distance ~150 m\n5. TAA / motion blur off (no MotionVectors pass yet)\n6. Open FiveKToonTest and Play\n7. O/P keys isolate outline blit vs DepthNormals prepass cost",
                "OK");
        }

        [MenuItem("Wasteland/Prototypes/Create 5k Toon Test Scene")]
        public static void CreateOrUpdateScene()
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.886f, 0.69f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(12f, 1f, 12f);
            MeshRenderer groundRenderer = ground.GetComponent<MeshRenderer>();
            if (groundRenderer != null)
            {
                Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
                if (urpUnlit != null)
                {
                    groundRenderer.sharedMaterial = new Material(urpUnlit) { color = new Color(0.64f, 0.61f, 0.42f, 1f) };
                }
            }

            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera camera = camGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.72f, 0.737f, 0.714f, 1f);
            camera.fieldOfView = FiveKCameraRig.VerticalFov;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 500f;
            camera.allowMSAA = false;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<FiveKCameraRig>();

            GameObject bootstrap = new GameObject("FiveKToonTest");
            FiveKToonTestBootstrap boot = bootstrap.AddComponent<FiveKToonTestBootstrap>();
            bootstrap.AddComponent<VatClipChurnDriver>();
            bootstrap.AddComponent<FiveKTestHud>();
            bootstrap.AddComponent<GameClock>();

            SerializedObject so = new SerializedObject(boot);
            SerializedProperty sun = so.FindProperty("_sun");
            if (sun != null)
            {
                sun.objectReferenceValue = light;
            }

            SerializedProperty vatMat = so.FindProperty("_vatMaterial");
            if (vatMat != null)
            {
                vatMat.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ToonMaterialAssets.VatPath);
            }

            SerializedProperty staticMat = so.FindProperty("_staticMaterial");
            if (staticMat != null)
            {
                staticMat.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(ToonMaterialAssets.StaticPath);
            }

            so.ApplyModifiedProperties();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("Saved " + ScenePath);
        }
    }
}
