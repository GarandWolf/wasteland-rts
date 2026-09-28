using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Wasteland.Presentation;

namespace Wasteland.Presentation.Editor
{
    /// <summary>
    /// Creates WastelandURP + Forward+ renderer, assigns them, and tries to keep BRG variants.
    /// Safe to re-run; it will not overwrite existing pipeline assets.
    /// </summary>
    public static class WastelandUrpSetup
    {
        public const string SettingsFolder = "Assets/_Project/Settings";
        public const string PipelinePath = SettingsFolder + "/WastelandURP.asset";
        public const string RendererPath = SettingsFolder + "/WastelandURP_Renderer.asset";

        [MenuItem("Wasteland/Rendering/Create URP Forward+ Assets")]
        public static void CreatePipelineAssets()
        {
            Directory.CreateDirectory(SettingsFolder);

            UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }

            SetForwardPlus(renderer);
            AddOutlineFeature(renderer);

            UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                if (pipeline == null)
                {
                    pipeline = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
                }

                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                SerializedObject pipelineSo = new SerializedObject(pipeline);
                SerializedProperty list = pipelineSo.FindProperty("m_RendererDataList") ?? pipelineSo.FindProperty("m_RendererData");
                if (list != null)
                {
                    if (list.isArray)
                    {
                        if (list.arraySize < 1)
                        {
                            list.arraySize = 1;
                        }

                        list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                    }
                    else
                    {
                        list.objectReferenceValue = renderer;
                    }

                    pipelineSo.ApplyModifiedProperties();
                }
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            TryKeepBatchRendererGroupVariants();
            TryAlwaysIncludeToonShaders();
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
            Debug.Log("Wasteland URP assigned. Confirm renderer Rendering Path = Forward+ and shadow distance ~150 m.");
        }

        public static void SetForwardPlus(UniversalRendererData renderer)
        {
            renderer.renderingMode = RenderingMode.ForwardPlus;
            EditorUtility.SetDirty(renderer);
        }

        public static void AddOutlineFeature(UniversalRendererData renderer)
        {
            SerializedObject so = new SerializedObject(renderer);
            SerializedProperty features = so.FindProperty("m_RendererFeatures");
            if (features == null)
            {
                Debug.LogWarning("Could not add ToonOutlineRendererFeature automatically.");
                return;
            }

            for (int i = 0; i < features.arraySize; i++)
            {
                Object existing = features.GetArrayElementAtIndex(i).objectReferenceValue;
                if (existing is ToonOutlineRendererFeature)
                {
                    return;
                }
            }

            ToonOutlineRendererFeature feature = ScriptableObject.CreateInstance<ToonOutlineRendererFeature>();
            feature.name = "Toon Outline";
            SerializedObject featureSo = new SerializedObject(feature);
            SerializedProperty shaderProp = featureSo.FindProperty("_shader");
            if (shaderProp != null)
            {
                shaderProp.objectReferenceValue = Shader.Find(ToonOutlineRendererFeature.ShaderName);
                featureSo.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.AddObjectToAsset(feature, renderer);
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(renderer);
        }

        public static void TryKeepBatchRendererGroupVariants()
        {
            Object graphics = AssetDatabase.LoadMainAssetAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphics == null)
            {
                Debug.LogWarning("Set Project Settings > Graphics > Shader Stripping > BatchRendererGroup Variants = Keep All (verify the Unity 6.3 name).");
                return;
            }

            SerializedObject so = new SerializedObject(graphics);
            SerializedProperty brg = so.FindProperty("m_BatchRendererGroupShaderStrippingMode") ??
                                     so.FindProperty("m_BrgStrippingMode") ??
                                     so.FindProperty("m_BatchRendererGroupVariants");
            if (brg != null && brg.propertyType == SerializedPropertyType.Enum && brg.enumNames.Length > 0)
            {
                int keepAll = 1;
                for (int i = 0; i < brg.enumDisplayNames.Length; i++)
                {
                    if (brg.enumDisplayNames[i].IndexOf("Keep", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        keepAll = i;
                        break;
                    }
                }

                brg.enumValueIndex = keepAll;
                so.ApplyModifiedProperties();
                Debug.Log("Set BatchRendererGroup shader stripping via " + brg.propertyPath + " = " + brg.enumDisplayNames[brg.enumValueIndex]);
            }
            else
            {
                Debug.LogWarning("Set Project Settings > Graphics > Shader Stripping > BatchRendererGroup Variants = Keep All (verify the Unity 6.3 name).");
            }
        }

        public static void TryAlwaysIncludeToonShaders()
        {
            Shader[] shaders =
            {
                Shader.Find("Wasteland/ToonVAT"),
                Shader.Find("Wasteland/ToonStatic"),
                Shader.Find("Wasteland/ToonOutline")
            };

            Object graphics = AssetDatabase.LoadMainAssetAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphics == null)
            {
                Debug.LogWarning("Add Wasteland/ToonStatic, Wasteland/ToonVAT, and Wasteland/ToonOutline to Always Included Shaders.");
                return;
            }

            SerializedObject so = new SerializedObject(graphics);
            SerializedProperty list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null || !list.isArray)
            {
                Debug.LogWarning("Add Wasteland/ToonStatic, Wasteland/ToonVAT, and Wasteland/ToonOutline to Project Settings > Graphics > Always Included Shaders.");
                return;
            }

            foreach (Shader shader in shaders)
            {
                if (shader == null)
                {
                    continue;
                }

                bool found = false;
                for (int i = 0; i < list.arraySize; i++)
                {
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                }
            }

            so.ApplyModifiedProperties();
            Debug.Log("Always Included Shaders now lists Wasteland/ToonStatic, Wasteland/ToonVAT, and Wasteland/ToonOutline.");
        }
    }
}
