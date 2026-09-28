using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Wasteland.Presentation.Editor
{
    /// <summary>
    /// Builds Shader Graph wrappers around the HLSL includes. The handwritten
    /// URP shaders are the compile-safe path; these graphs are the art-authoring surface.
    /// </summary>
    public static class ToonShaderGraphGenerator
    {
        public const string OutputFolder = "Assets/_Project/Art/Shaders";

        [MenuItem("Wasteland/Shaders/Generate Toon Shader Graphs")]
        public static void Generate()
        {
            Directory.CreateDirectory(OutputFolder);
            string vatHlsl = AssetDatabase.AssetPathToGUID(OutputFolder + "/Includes/VAT.hlsl");
            string paletteHlsl = AssetDatabase.AssetPathToGUID(OutputFolder + "/Includes/TeamPalette.hlsl");
            string toonHlsl = AssetDatabase.AssetPathToGUID(OutputFolder + "/Includes/ToonLighting.hlsl");

            WriteGraph("WastelandToonVAT.shadergraph", vat: true, vatHlsl, paletteHlsl, toonHlsl);
            WriteGraph("WastelandToonStatic.shadergraph", vat: false, vatHlsl, paletteHlsl, toonHlsl);

            if (!TryCreateWithShaderGraphApi())
            {
                Debug.LogWarning(
                    "Wrote Shader Graph JSON stubs. Open them in Unity 6.3 Shader Graph, set the target to URP Lit, " +
                    "and wire Custom Function nodes (File mode) to Includes/VAT.hlsl, TeamPalette.hlsl, ToonLighting.hlsl. " +
                    "Set _TeamIndex/_AnimIndex/_AnimTime to Hybrid Per Instance. The handwritten Wasteland/ToonVAT and " +
                    "Wasteland/ToonStatic shaders already implement the same includes and are what the 5k scene uses.");
            }

            AssetDatabase.Refresh();
        }

        private static bool TryCreateWithShaderGraphApi()
        {
            Type graphDataType = FindType("UnityEditor.ShaderGraph.GraphData, Unity.ShaderGraph.Editor");
            Type fileUtilType = FindType("UnityEditor.ShaderGraph.FileUtilities, Unity.ShaderGraph.Editor");
            if (graphDataType == null || fileUtilType == null)
            {
                return false;
            }

            try
            {
                object graph = Activator.CreateInstance(graphDataType, nonPublic: true);
                MethodInfo write = fileUtilType.GetMethod(
                    "WriteShaderGraphToDisk",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (write == null || graph == null)
                {
                    return false;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Shader Graph API generation skipped: " + ex.Message);
                return false;
            }
        }

        private static Type FindType(string display)
        {
            Type t = Type.GetType(display);
            if (t != null)
            {
                return t;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = assembly.GetType(display.Split(',')[0]);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        private static void WriteGraph(string fileName, bool vat, string vatGuid, string paletteGuid, string toonGuid)
        {
            string path = Path.Combine(OutputFolder, fileName);
            string graphId = Guid.NewGuid().ToString("N");
            string noteId = Guid.NewGuid().ToString("N");
            string teamProp = Guid.NewGuid().ToString("N");
            string animProp = Guid.NewGuid().ToString("N");
            string timeProp = Guid.NewGuid().ToString("N");
            string catId = Guid.NewGuid().ToString("N");

            string extraPropRefs = vat
                ? ",\n { \"m_Id\": \"" + animProp + "\" },\n { \"m_Id\": \"" + timeProp + "\" }"
                : "";

            string extraCat = vat
                ? ",\n { \"m_Id\": \"" + animProp + "\" },\n { \"m_Id\": \"" + timeProp + "\" }"
                : "";

            string body =
@"{
    ""m_SGVersion"": 3,
    ""m_Type"": ""UnityEditor.ShaderGraph.GraphData"",
    ""m_ObjectId"": """ + graphId + @""",
    ""m_Properties"": [
        { ""m_Id"": """ + teamProp + @""" }" + extraPropRefs + @"
    ],
    ""m_Keywords"": [],
    ""m_Dropdowns"": [],
    ""m_CategoryData"": [ { ""m_Id"": """ + catId + @""" } ],
    ""m_Nodes"": [ { ""m_Id"": """ + noteId + @""" } ],
    ""m_GroupDatas"": [],
    ""m_StickyNoteDatas"": [ { ""m_Id"": """ + noteId + @""" } ],
    ""m_Edges"": [],
    ""m_VertexContext"": { ""m_Position"": { ""x"": 0.0, ""y"": 0.0 }, ""m_Blocks"": [] },
    ""m_FragmentContext"": { ""m_Position"": { ""x"": 200.0, ""y"": 0.0 }, ""m_Blocks"": [] },
    ""m_PreviewData"": { ""serializedMesh"": { ""m_SerializedMesh"": ""{\""mesh\"":{\""instanceID\"":0}}"", ""m_Guid"": """" }, ""preventRotation"": false },
    ""m_Path"": ""Shader Graphs"",
    ""m_GraphPrecision"": 1,
    ""m_PreviewMode"": 2,
    ""m_OutputNode"": { ""m_Id"": """" },
    ""m_SubDatas"": [],
    ""m_ActiveTargets"": []
}

{
    ""m_SGVersion"": 1,
    ""m_Type"": ""UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty"",
    ""m_ObjectId"": """ + teamProp + @""",
    ""m_Guid"": { ""m_GuidSerialized"": """ + Guid.NewGuid() + @""" },
    ""m_Name"": ""Team Index"",
    ""m_DefaultRefNameVersion"": 1,
    ""m_RefNameGeneratedByDisplayName"": ""Team Index"",
    ""m_DefaultReferenceName"": ""_TeamIndex"",
    ""m_OverrideReferenceName"": ""_TeamIndex"",
    ""m_GeneratePropertyBlock"": true,
    ""m_UseCustomSlotLabel"": false,
    ""m_CustomSlotLabel"": """",
    ""m_DismissedVersion"": 0,
    ""m_Precision"": 0,
    ""overrideHLSLDeclaration"": true,
    ""hlslDeclarationOverride"": 3,
    ""m_Hidden"": false,
    ""m_Value"": 0.0,
    ""m_FloatType"": 0,
    ""m_RangeValues"": { ""x"": 0.0, ""y"": 1.0 }
}
" + (vat ? PropertyObject("Anim Index", "_AnimIndex", animProp) + "\n" + PropertyObject("Anim Time", "_AnimTime", timeProp) : "") + @"
{
    ""m_SGVersion"": 0,
    ""m_Type"": ""UnityEditor.ShaderGraph.CategoryData"",
    ""m_ObjectId"": """ + catId + @""",
    ""m_Name"": """",
    ""m_ChildObjectList"": [
        { ""m_Id"": """ + teamProp + @""" }" + extraCat + @"
    ]
}

{
    ""m_SGVersion"": 0,
    ""m_Type"": ""UnityEditor.ShaderGraph.StickyNoteData"",
    ""m_ObjectId"": """ + noteId + @""",
    ""m_Title"": ""Wasteland toon (" + (vat ? "VAT" : "static") + @")"",
    ""m_Content"": ""Set URP Lit as the target. Custom Function File mode:\nVAT_float <- Includes/VAT.hlsl (guid " + vatGuid + @")\nDecodeTeamIndex_float / SampleTeamPalette_float / ApplyTeamMask_float <- TeamPalette.hlsl (" + paletteGuid + @")\nToonLighting_float <- ToonLighting.hlsl (" + toonGuid + @")\nWire VAT Pos/Nrm to Vertex Position/Normal so shadows and depth follow the pose.\nHybrid Per Instance: _TeamIndex" + (vat ? ", _AnimIndex, _AnimTime" : "") + @".\n_VATTime and _TeamPaletteTex are globals. The handwritten Wasteland/Toon" + (vat ? "VAT" : "Static") + @" shader is the runtime path."",
    ""m_TextSize"": 0,
    ""m_Theme"": 1,
    ""m_Position"": { ""serializedVersion"": ""2"", ""x"": 40.0, ""y"": -180.0, ""width"": 420.0, ""height"": 220.0 },
    ""m_Group"": { ""m_Id"": """" }
}
";
            File.WriteAllText(path, body);
            Debug.Log("Wrote " + path);
        }

        private static string PropertyObject(string name, string reference, string id)
        {
            return
@"{
    ""m_SGVersion"": 1,
    ""m_Type"": ""UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty"",
    ""m_ObjectId"": """ + id + @""",
    ""m_Guid"": { ""m_GuidSerialized"": """ + Guid.NewGuid() + @""" },
    ""m_Name"": """ + name + @""",
    ""m_DefaultRefNameVersion"": 1,
    ""m_RefNameGeneratedByDisplayName"": """ + name + @""",
    ""m_DefaultReferenceName"": """ + reference + @""",
    ""m_OverrideReferenceName"": """ + reference + @""",
    ""m_GeneratePropertyBlock"": true,
    ""m_UseCustomSlotLabel"": false,
    ""m_CustomSlotLabel"": """",
    ""m_DismissedVersion"": 0,
    ""m_Precision"": 0,
    ""overrideHLSLDeclaration"": true,
    ""hlslDeclarationOverride"": 3,
    ""m_Hidden"": false,
    ""m_Value"": 0.0,
    ""m_FloatType"": 0,
    ""m_RangeValues"": { ""x"": 0.0, ""y"": 1.0 }
}
";
        }
    }
}
