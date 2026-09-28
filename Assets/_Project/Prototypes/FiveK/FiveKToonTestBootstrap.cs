using Unity.Entities;
using UnityEngine;

namespace Wasteland.Prototypes
{
    /// <summary>
    /// Scene entry for the 5k toon/VAT stress test. Spawn is visual-only.
    /// </summary>
    [RequireComponent(typeof(VatClipChurnDriver))]
    [RequireComponent(typeof(FiveKTestHud))]
    public sealed class FiveKToonTestBootstrap : MonoBehaviour
    {
        [SerializeField] private bool _spawnOnStart = true;
        [SerializeField] private Light _sun;

        public int InfantryCount { get; private set; }
        public int VehicleCount { get; private set; }
        public int ProbeCount { get; private set; }

        private void Awake()
        {
            VatClipChurnDriver churn = GetComponent<VatClipChurnDriver>();
            FiveKTestHud hud = GetComponent<FiveKTestHud>();
            FiveKCameraRig rig = FindFirstObjectByType<FiveKCameraRig>();
            if (hud != null)
            {
                hud.Bind(this, churn, rig);
            }
        }

        private void Start()
        {
            ConfigureSun();
            TeamPaletteEnsure();
            if (!_spawnOnStart)
            {
                return;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("No default Entities world. Enable automatic world bootstrap, then Play.");
                return;
            }

            FiveKUnitSpawner.SpawnStats stats = FiveKUnitSpawner.Spawn(world);
            InfantryCount = stats.Infantry;
            VehicleCount = stats.Vehicles;
            ProbeCount = stats.ProbeCount;
            Debug.Log(string.Format(
                "[FiveK] spawned {0} infantry + {1} vehicles (+{2} probes). Teams never enter Wasteland.Sim.",
                InfantryCount,
                VehicleCount,
                ProbeCount));
        }

        private static void TeamPaletteEnsure()
        {
            Wasteland.Presentation.TeamPaletteRuntime.BindGlobals();
        }

        private void ConfigureSun()
        {
            if (_sun == null)
            {
                _sun = FindFirstObjectByType<Light>();
            }

            if (_sun == null)
            {
                return;
            }

            _sun.type = LightType.Directional;
            _sun.color = new Color(1f, 0.886f, 0.69f);
            _sun.intensity = 1.1f;
            _sun.shadows = LightShadows.Soft;
            _sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }
}
