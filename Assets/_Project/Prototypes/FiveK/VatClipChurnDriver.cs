using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using Wasteland.Presentation;

namespace Wasteland.Prototypes
{
    /// <summary>
    /// Switches 1,000 random infantry clips every second. Component writes only;
    /// no structural changes.
    /// </summary>
    public sealed class VatClipChurnDriver : MonoBehaviour
    {
        [SerializeField] private int _churnCount = 1000;
        [SerializeField] private float _interval = 1f;

        private float _timer;
        private int _switches;
        private double _lastCostMs;
        private bool _enabled = true;
        private readonly ProfilerMarker _marker = new ProfilerMarker("FiveK.VatClipChurn");

        public int SwitchCount
        {
            get { return _switches; }
        }

        public double LastCostMs
        {
            get { return _lastCostMs; }
        }

        public bool ChurnEnabled
        {
            get { return _enabled; }
            set { _enabled = value; }
        }

        private void Update()
        {
            if (!_enabled)
            {
                return;
            }

            _timer += Time.unscaledDeltaTime;
            if (_timer < _interval)
            {
                return;
            }

            _timer = 0f;
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return;
            }

            double start = Time.realtimeSinceStartupAsDouble;
            using (_marker.Auto())
            {
                SwitchClips(world.EntityManager);
            }

            _lastCostMs = (Time.realtimeSinceStartupAsDouble - start) * 1000.0;
        }

        private void SwitchClips(EntityManager em)
        {
            using EntityQuery query = em.CreateEntityQuery(ComponentType.ReadWrite<VatClipState>(), ComponentType.ReadWrite<VatStartTime>(), ComponentType.ReadOnly<FiveKInfantryTag>());
            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0)
            {
                return;
            }

            int n = mathMin(_churnCount, entities.Length);
            float vatTime = Shader.GetGlobalFloat(PresentationRenderGlobalsSystem.VatTimeProperty);
            uint seed = (uint)(Time.frameCount * 2654435761u);
            for (int i = 0; i < n; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                int index = (int)(seed % (uint)entities.Length);
                Entity e = entities[index];
                VatClipState state = em.GetComponentData<VatClipState>(e);
                int current = VatClipPacking.GetClip(state.Value);
                int next = (current + 1 + (int)(seed & 1u)) % 3;
                bool valid = VatClipPacking.GetValid(state.Value);
                float speed = VatClipPacking.GetSpeed(state.Value);
                if (speed <= 0f)
                {
                    speed = 1f;
                }

                em.SetComponentData(e, new VatClipState { Value = VatClipPacking.Pack(next, hold: false, valid, speed) });
                em.SetComponentData(e, new VatStartTime { Value = vatTime });
                _switches++;
            }
        }

        private static int mathMin(int a, int b)
        {
            return a < b ? a : b;
        }
    }
}
