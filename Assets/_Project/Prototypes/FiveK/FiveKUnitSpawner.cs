using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using Wasteland.Presentation;

namespace Wasteland.Prototypes
{
    /// <summary>
    /// Throwaway 5k-unit Entities Graphics spawn. Visual only — no sim components.
    /// </summary>
    public static class FiveKUnitSpawner
    {
        public const int InfantryCount = 4000;
        public const int VehicleCount = 1000;
        public const int GroupCount = 9;

        public struct SpawnStats
        {
            public int Infantry;
            public int Vehicles;
            public int ProbeCount;
            public string[] VatReports;
        }

        public static SpawnStats Spawn(World world)
        {
            EntityManager em = world.EntityManager;
            SpawnStats stats = new SpawnStats { VatReports = new string[ProceduralUnitMeshes.InfantryArchetypeCount] };

            PlaceholderVatSet[] vat = new PlaceholderVatSet[ProceduralUnitMeshes.InfantryArchetypeCount];
            Material[] infantryMats = new Material[vat.Length];
            Mesh[] infantryMeshes = new Mesh[vat.Length];
            Material[] vehicleMats = new Material[ProceduralUnitMeshes.VehicleArchetypeCount];
            Mesh[] vehicleMeshes = new Mesh[vehicleMats.Length];
            AABB[] infantryBounds = new AABB[vat.Length];
            AABB[] vehicleBounds = new AABB[vehicleMats.Length];

            Shader vatShader = Shader.Find("Wasteland/ToonVAT");
            Shader staticShader = Shader.Find("Wasteland/ToonStatic");
            if (vatShader == null || staticShader == null)
            {
                Debug.LogError("Wasteland toon shaders are missing. Check Assets/_Project/Art/Shaders/.");
                return stats;
            }

            TeamPaletteRuntime.BindGlobals();

            for (int i = 0; i < vat.Length; i++)
            {
                vat[i] = PrototypeVatFactory.BuildInfantry(i);
                stats.VatReports[i] = PrototypeVatFactory.FormatReport(vat[i]);
                infantryMeshes[i] = vat[i].BindMesh;
                infantryMats[i] = CreateVatMaterial(vatShader, vat[i]);
                infantryBounds[i] = ToAabb(vat[i].RenderBounds);
                Debug.Log("[FiveK] " + stats.VatReports[i]);
            }

            for (int i = 0; i < vehicleMeshes.Length; i++)
            {
                ProceduralUnitMeshes.BuiltMesh built = ProceduralUnitMeshes.CreateVehicle(i);
                vehicleMeshes[i] = built.Mesh;
                vehicleMats[i] = CreateStaticMaterial(staticShader);
                vehicleBounds[i] = ToAabb(built.AnimationBounds);
            }

            RenderMeshArray infantryArray = new RenderMeshArray(infantryMats, infantryMeshes);
            RenderMeshArray vehicleArray = new RenderMeshArray(vehicleMats, vehicleMeshes);
            RenderMeshDescription desc = new RenderMeshDescription(
                shadowCastingMode: ShadowCastingMode.On,
                receiveShadows: true);

            int infantryPerGroup = InfantryCount / GroupCount;
            int infantryExtra = InfantryCount - infantryPerGroup * GroupCount;
            int vehiclePerGroup = VehicleCount / GroupCount;
            int vehicleExtra = VehicleCount - vehiclePerGroup * GroupCount;

            int infantrySpawned = 0;
            int vehicleSpawned = 0;
            int serial = 0;

            for (int group = 0; group < GroupCount; group++)
            {
                int faction = group / 3;
                int shade = group % 3;
                int pips = shade;
                uint team = TeamIndexPacking.Pack(faction, shade, pips, hatch: false, valid: true);

                float3 cluster = ClusterOrigin(group);
                int nInf = infantryPerGroup + (group < infantryExtra ? 1 : 0);
                int nVeh = vehiclePerGroup + (group < vehicleExtra ? 1 : 0);

                for (int i = 0; i < nInf; i++)
                {
                    int arch = (group + i) % vat.Length;
                    float3 pos = cluster + InfantrySlot(i);
                    int clip = PickClip(serial);
                    float length = ClipLength(vat[arch], clip);
                    float start = Shader.GetGlobalFloat(PresentationRenderGlobalsSystem.VatTimeProperty) - Hash01(serial) * length;
                    SpawnInfantry(em, desc, infantryArray, arch, infantryBounds[arch], pos, team, clip, start, speed: 1f);
                    infantrySpawned++;
                    serial++;
                }

                for (int i = 0; i < nVeh; i++)
                {
                    int arch = (group + i) % vehicleMeshes.Length;
                    float3 pos = cluster + VehicleSlot(i);
                    SpawnVehicle(em, desc, vehicleArray, arch, vehicleBounds[arch], pos, team);
                    vehicleSpawned++;
                    serial++;
                }
            }

            stats.ProbeCount = SpawnProbes(em, desc, infantryArray, infantryBounds);
            stats.Infantry = infantrySpawned;
            stats.Vehicles = vehicleSpawned;
            return stats;
        }

        public static void SetAllClipSpeeds(EntityManager em, float speed)
        {
            using EntityQuery query = em.CreateEntityQuery(ComponentType.ReadWrite<VatClipState>());
            using NativeArray<VatClipState> states = query.ToComponentDataArray<VatClipState>(Allocator.Temp);
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                VatClipState s = states[i];
                int clip = VatClipPacking.GetClip(s.Value);
                bool hold = VatClipPacking.GetHold(s.Value);
                bool valid = VatClipPacking.GetValid(s.Value);
                s.Value = VatClipPacking.Pack(clip, hold, valid, speed);
                em.SetComponentData(entities[i], s);
            }
        }

        public static void SetBindPose(EntityManager em, bool bindPose)
        {
            using EntityQuery query = em.CreateEntityQuery(ComponentType.ReadWrite<VatClipState>());
            using NativeArray<VatClipState> states = query.ToComponentDataArray<VatClipState>(Allocator.Temp);
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                VatClipState s = states[i];
                int clip = VatClipPacking.GetClip(s.Value);
                bool hold = VatClipPacking.GetHold(s.Value);
                float speed = VatClipPacking.GetSpeed(s.Value);
                s.Value = VatClipPacking.Pack(clip, hold, valid: !bindPose, speed);
                em.SetComponentData(entities[i], s);
            }
        }

        private static int SpawnProbes(
            EntityManager em,
            RenderMeshDescription desc,
            RenderMeshArray array,
            AABB[] bounds)
        {
            int count = 0;
            uint invalid = TeamIndexPacking.Pack(0, 0, 0, false, valid: false);
            for (int clip = 0; clip < 3; clip++)
            {
                for (int i = 0; i < 3; i++)
                {
                    float3 pos = new float3(-18f + clip * 4f, 0f, -18f + i * 2.5f);
                    uint team = TeamIndexPacking.Pack(i, 0, 0, false, true);
                    SpawnInfantry(em, desc, array, 0, bounds[0], pos, team, clip, startTime: 0f, speed: 0f);
                    count++;
                }
            }

            SpawnInfantry(em, desc, array, 0, bounds[0], new float3(-6f, 0f, -18f), invalid, 0, 0f, 1f);
            count++;
            return count;
        }

        private static void SpawnInfantry(
            EntityManager em,
            RenderMeshDescription desc,
            RenderMeshArray array,
            int archetype,
            AABB bounds,
            float3 position,
            uint team,
            int clip,
            float startTime,
            float speed)
        {
            Entity entity = em.CreateEntity();
            em.AddComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1.2f));
            RenderMeshUtility.AddComponents(
                entity,
                em,
                desc,
                array,
                MaterialMeshInfo.FromRenderMeshArrayIndices(archetype, archetype));
            em.SetComponentData(entity, new RenderBounds { Value = bounds });
            em.AddComponentData(entity, new TeamColorIndex { Value = team });
            em.AddComponentData(entity, new VatClipState { Value = VatClipPacking.Pack(clip, hold: false, valid: true, speed) });
            em.AddComponentData(entity, new VatStartTime { Value = startTime });
            em.AddComponentData(entity, new FiveKInfantryTag());
        }

        private static void SpawnVehicle(
            EntityManager em,
            RenderMeshDescription desc,
            RenderMeshArray array,
            int archetype,
            AABB bounds,
            float3 position,
            uint team)
        {
            Entity entity = em.CreateEntity();
            em.AddComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1.1f));
            RenderMeshUtility.AddComponents(
                entity,
                em,
                desc,
                array,
                MaterialMeshInfo.FromRenderMeshArrayIndices(archetype, archetype));
            em.SetComponentData(entity, new RenderBounds { Value = bounds });
            em.AddComponentData(entity, new TeamColorIndex { Value = team });
            em.AddComponentData(entity, new FiveKVehicleTag());
        }

        private static Material CreateVatMaterial(Shader shader, PlaceholderVatSet vat)
        {
            Material mat = new Material(shader) { name = "M_Toon_Infantry_" + vat.BindMesh.name, enableInstancing = true };
            mat.SetTexture("_VATPos", vat.Positions);
            mat.SetTexture("_VATNrm", vat.Normals);
            mat.SetTexture("_VATClipTex", vat.ClipTable);
            mat.SetFloat("_VATRowsPerFrame", vat.RowsPerFrame);
            mat.SetFloat("_UseVertexColorMask", 1f);
            mat.SetFloat("_EnableRim", 1f);
            ApplyToonDefaults(mat);
            return mat;
        }

        private static Material CreateStaticMaterial(Shader shader)
        {
            Material mat = new Material(shader) { name = "M_Toon_Vehicle", enableInstancing = true };
            mat.SetFloat("_UseVertexColorMask", 1f);
            mat.SetFloat("_EnableRim", 1f);
            ApplyToonDefaults(mat);
            return mat;
        }

        private static void ApplyToonDefaults(Material mat)
        {
            mat.SetColor("_BaseColor", new Color(0.45f, 0.42f, 0.38f, 1f));
            mat.SetColor("_ShadowTint", new Color(0.227f, 0.247f, 0.333f, 1f));
            mat.SetFloat("_ShadowStrength", 0.4f);
            mat.SetFloat("_SmoothStep", 0.08f);
            mat.SetColor("_RimColor", new Color(0.72f, 0.76f, 0.82f, 1f));
            mat.SetFloat("_RimWidth", 0.2f);
            mat.SetFloat("_RimStrength", 0.35f);
        }

        private static float3 ClusterOrigin(int group)
        {
            int gx = group % 3;
            int gz = group / 3;
            return new float3((gx - 1) * 28f, 0f, (gz - 1) * 28f);
        }

        private static float3 InfantrySlot(int i)
        {
            int cols = 24;
            int x = i % cols;
            int z = i / cols;
            return new float3((x - cols * 0.5f) * 1.1f, 0f, (z - 8) * 1.1f);
        }

        private static float3 VehicleSlot(int i)
        {
            int cols = 12;
            int x = i % cols;
            int z = i / cols;
            return new float3((x - cols * 0.5f) * 2.4f, 0f, 10f + z * 3.2f);
        }

        private static int PickClip(int serial)
        {
            float u = Hash01(serial * 3 + 17);
            if (u < 0.4f)
            {
                return VatClipPacking.ClipIdle;
            }

            if (u < 0.8f)
            {
                return VatClipPacking.ClipWalk;
            }

            return VatClipPacking.ClipFire;
        }

        private static float ClipLength(PlaceholderVatSet set, int clip)
        {
            if (set.Clips == null || clip < 0 || clip >= set.Clips.Length)
            {
                return 1f;
            }

            return math.max(set.Clips[clip].LengthSeconds, 0.1f);
        }

        private static float Hash01(int value)
        {
            uint x = (uint)value * 747796405u + 2891336453u;
            uint word = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
            word = (word >> 22) ^ word;
            return (word & 0xFFFFFFu) / 16777215f;
        }

        private static AABB ToAabb(Bounds bounds)
        {
            return new AABB { Center = bounds.center, Extents = bounds.extents };
        }
    }

    public struct FiveKInfantryTag : IComponentData
    {
    }

    public struct FiveKVehicleTag : IComponentData
    {
    }
}
