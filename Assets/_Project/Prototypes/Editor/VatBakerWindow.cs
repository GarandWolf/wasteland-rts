using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

namespace Wasteland.Prototypes.Editor
{
    public sealed class VatBakerWindow : EditorWindow
    {
        private VatBakeProfile _profile;
        private Vector2 _scroll;
        private string _lastReport = "";

        [MenuItem("Wasteland/VAT/Baker")]
        public static void Open()
        {
            GetWindow<VatBakerWindow>("VAT Baker");
        }

        [MenuItem("Wasteland/VAT/Generate Placeholder Sample")]
        public static void GeneratePlaceholder()
        {
            string folder = "Assets/_Project/Prototypes/Generated/VAT";
            Directory.CreateDirectory(folder);
            StringBuilder report = new StringBuilder();
            for (int i = 0; i < ProceduralUnitMeshes.InfantryArchetypeCount; i++)
            {
                PlaceholderVatSet set = PrototypeVatFactory.BuildInfantry(i);
                string name = "Infantry" + i;
                SaveTexture(set.Positions, folder + "/" + name + "_VATPos.asset");
                SaveTexture(set.Normals, folder + "/" + name + "_VATNrm.asset");
                SaveTexture(set.ClipTable, folder + "/" + name + "_VATClip.asset");
                AssetDatabase.CreateAsset(Instantiate(set.BindMesh), folder + "/" + name + "_Mesh.asset");
                report.AppendLine(PrototypeVatFactory.FormatReport(set));
            }

            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
            EditorUtility.DisplayDialog("VAT placeholder", report.ToString(), "OK");
        }

        private void OnGUI()
        {
            _profile = (VatBakeProfile)EditorGUILayout.ObjectField("Profile", _profile, typeof(VatBakeProfile), false);
            if (_profile == null)
            {
                EditorGUILayout.HelpBox("Create a VAT Bake Profile (Create > Wasteland > VAT Bake Profile), or use Generate Placeholder Sample when no character prefab exists.", MessageType.Info);
                if (GUILayout.Button("Generate placeholder infantry VAT"))
                {
                    GeneratePlaceholder();
                }

                return;
            }

            EditorGUI.BeginChangeCheck();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            SerializedObject so = new SerializedObject(_profile);
            EditorGUILayout.PropertyField(so.FindProperty("SourcePrefab"));
            EditorGUILayout.PropertyField(so.FindProperty("Clips"), true);
            EditorGUILayout.PropertyField(so.FindProperty("OutputFolder"));
            EditorGUILayout.PropertyField(so.FindProperty("TextureWidth"));
            EditorGUILayout.PropertyField(so.FindProperty("AssetName"));
            so.ApplyModifiedProperties();
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Bake profile"))
            {
                _lastReport = Bake(_profile);
            }

            if (GUILayout.Button("Generate placeholder instead"))
            {
                GeneratePlaceholder();
            }

            if (!string.IsNullOrEmpty(_lastReport))
            {
                EditorGUILayout.HelpBox(_lastReport, MessageType.None);
            }
        }

        [MenuItem("Wasteland/VAT/Bake All Profiles")]
        public static void BakeAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:VatBakeProfile");
            StringBuilder all = new StringBuilder();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                VatBakeProfile profile = AssetDatabase.LoadAssetAtPath<VatBakeProfile>(path);
                all.AppendLine(Bake(profile));
            }

            Debug.Log(all.Length == 0 ? "No VatBakeProfile assets found." : all.ToString());
        }

        public static string Bake(VatBakeProfile profile)
        {
            if (profile == null)
            {
                return "No profile.";
            }

            if (profile.SourcePrefab == null)
            {
                return "Profile has no source prefab. Use Generate Placeholder Sample for procedural infantry.";
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(profile.SourcePrefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            SkinnedMeshRenderer smr = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null)
            {
                DestroyImmediate(instance);
                return "Prefab has no SkinnedMeshRenderer.";
            }

            Mesh bind = smr.sharedMesh;
            int vertexCount = bind.vertexCount;
            int width = Mathf.Max(8, profile.TextureWidth);
            if (width < vertexCount && width < 4096)
            {
                width = Mathf.Min(4096, Mathf.NextPowerOfTwo(vertexCount));
            }

            int rowsPerFrame = Mathf.CeilToInt(vertexCount / (float)width);
            int totalFrames = 0;
            foreach (VatBakeClip clip in profile.Clips)
            {
                if (clip == null || clip.Clip == null)
                {
                    continue;
                }

                totalFrames += Mathf.Max(1, Mathf.RoundToInt(clip.Clip.length * Mathf.Max(1f, clip.Fps)));
            }

            if (totalFrames == 0)
            {
                DestroyImmediate(instance);
                return "No animation clips assigned. Assign clips or generate a placeholder.";
            }

            int height = totalFrames * rowsPerFrame;
            Texture2D posTex = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
            {
                name = profile.AssetName + "_VATPos",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Texture2D nrmTex = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
            {
                name = profile.AssetName + "_VATNrm",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] posPixels = new Color[width * height];
            Color[] nrmPixels = new Color[width * height];
            Texture2D clipTex = new Texture2D(64, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = profile.AssetName + "_VATClip",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] clipPixels = new Color[64];

            Mesh baked = new Mesh { name = "VatBakeScratch" };
            Bounds bounds = bind.bounds;
            int frameRow = 0;
            int clipIndex = 0;
            float seamDelta = 0f;

            foreach (VatBakeClip clipInfo in profile.Clips)
            {
                if (clipInfo == null || clipInfo.Clip == null)
                {
                    continue;
                }

                AnimationClip clip = clipInfo.Clip;
                int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * Mathf.Max(1f, clipInfo.Fps)));
                Vector3[] firstFrame = null;
                Vector3[] lastFrame = null;
                clipPixels[clipIndex] = new Color(frameRow, frames, clipInfo.Fps, clipInfo.Loop ? 1f : 0f);

                for (int f = 0; f < frames; f++)
                {
                    float t = frames <= 1 ? 0f : (f / (float)clipInfo.Fps);
                    Sample(instance, clip, t);
                    smr.BakeMesh(baked, true);
                    Vector3[] verts = baked.vertices;
                    Vector3[] nrms = baked.normals;
                    if (f == 0)
                    {
                        firstFrame = (Vector3[])verts.Clone();
                    }

                    if (f == frames - 1)
                    {
                        lastFrame = verts;
                    }

                    for (int v = 0; v < vertexCount && v < verts.Length; v++)
                    {
                        int col = v % width;
                        int sub = v / width;
                        int y = (frameRow + f) * rowsPerFrame + sub;
                        int i = y * width + col;
                        Vector3 p = verts[v];
                        Vector3 n = v < nrms.Length ? nrms[v] : Vector3.up;
                        posPixels[i] = new Color(p.x, p.y, p.z, 1f);
                        nrmPixels[i] = new Color(n.x, n.y, n.z, 0f);
                        bounds.Encapsulate(p);
                    }
                }

                if (clipInfo.Loop && firstFrame != null && lastFrame != null)
                {
                    float max = 0f;
                    int n = Mathf.Min(firstFrame.Length, lastFrame.Length);
                    for (int v = 0; v < n; v++)
                    {
                        max = Mathf.Max(max, Vector3.Distance(firstFrame[v], lastFrame[v]));
                    }

                    seamDelta = Mathf.Max(seamDelta, max);
                }

                frameRow += frames;
                clipIndex++;
            }

            posTex.SetPixels(posPixels);
            nrmTex.SetPixels(nrmPixels);
            posTex.Apply(false, false);
            nrmTex.Apply(false, false);
            clipTex.SetPixels(clipPixels);
            clipTex.Apply(false, false);

            Mesh outputMesh = Instantiate(bind);
            outputMesh.name = profile.AssetName + "_VATMesh";
            Vector2[] uv3 = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                uv3[i] = new Vector2(i, 0f);
            }

            outputMesh.SetUVs(3, uv3);
            outputMesh.bounds = bounds;

            Directory.CreateDirectory(profile.OutputFolder);
            string folder = profile.OutputFolder;
            SaveTexture(posTex, folder + "/" + posTex.name + ".asset");
            SaveTexture(nrmTex, folder + "/" + nrmTex.name + ".asset");
            SaveTexture(clipTex, folder + "/" + clipTex.name + ".asset");
            AssetDatabase.CreateAsset(outputMesh, folder + "/" + outputMesh.name + ".asset");
            AssetDatabase.SaveAssets();

            DestroyImmediate(baked);
            DestroyImmediate(instance);

            float mb = width * height * 8f * 2f / (1024f * 1024f);
            string warn = rowsPerFrame > 1 ? " WARNING: rowsPerFrame > 1." : "";
            if (seamDelta > 0.01f)
            {
                warn += " Loop seam " + seamDelta.ToString("0.000") + " m (target ≤ 1 cm).";
            }

            return string.Format(
                "{0}: {1} verts, {2} frames, {3}x{4}, rowsPerFrame={5}, {6:0.00} MB, worst half-step n/a (CPU float bake), seam {7:0.000} m.{8}",
                profile.AssetName,
                vertexCount,
                totalFrames,
                width,
                height,
                rowsPerFrame,
                mb,
                seamDelta,
                warn);
        }

        private static void Sample(GameObject instance, AnimationClip clip, float time)
        {
            if (clip.isHumanMotion)
            {
                Animator animator = instance.GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    PlayableGraph graph = PlayableGraph.Create("WastelandVatBake");
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
                    AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "out", animator);
                    output.SetSourcePlayable(playable);
                    playable.SetTime(time);
                    graph.Evaluate();
                    graph.Destroy();
                    return;
                }
            }

            clip.SampleAnimation(instance, time);
        }

        private static void SaveTexture(Texture2D texture, string path)
        {
            Texture2D copy = Instantiate(texture);
            copy.name = texture.name;
            AssetDatabase.CreateAsset(copy, path);
        }
    }
}
