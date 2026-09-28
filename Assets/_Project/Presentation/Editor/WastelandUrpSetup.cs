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
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
            Debug.Log("Wasteland URP assigned. Confirm renderer Rendering Path = Forward+ and shadow distance ~150 m.");
        }

        public static void SetForwardPlus(UniversalRendererData renderer)
        {
            SerializedObject so = new SerializedObject(renderer);
            SerializedProperty mode = so.FindProperty("m_RenderingMode") ?? so.FindProperty("m_RenderingModePath");
            if (mode != null)
            {
                // URP RenderingMode.ForwardPlus is 1 in URP 14–17.
                mode.intValue = 1;
                so.ApplyModifiedProperties();
            }
            else
            {
                Debug.LogWarning("Could not find UniversalRendererData rendering mode property. Set Forward+ manually.");
            }
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
    }
}
