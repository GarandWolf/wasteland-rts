using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using Wasteland.Presentation;

namespace Wasteland.Prototypes
{
    /// <summary>
    /// On-screen counters for spec v2 §6 (frame time, counts, churn, VAT clock).
    /// Pause/speed drive <see cref="GameClock"/>, not <c>Time.timeScale</c>.
    /// </summary>
    public sealed class FiveKTestHud : MonoBehaviour
    {
        [SerializeField] private FiveKToonTestBootstrap _bootstrap;
        [SerializeField] private VatClipChurnDriver _churn;
        [SerializeField] private FiveKCameraRig _cameraRig;

        private float _delta;
        private bool _visible = true;
        private FrameTiming[] _timings = new FrameTiming[1];

        public void Bind(FiveKToonTestBootstrap bootstrap, VatClipChurnDriver churn, FiveKCameraRig cameraRig)
        {
            _bootstrap = bootstrap;
            _churn = churn;
            _cameraRig = cameraRig;
        }

        private void Update()
        {
            _delta = Time.unscaledDeltaTime;
            FrameTimingManager.CaptureFrameTimings();
            GameClock clock = GameClock.Instance;

            if (Input.GetKeyDown(KeyCode.H))
            {
                _visible = !_visible;
            }

            if (clock != null)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1))
                {
                    clock.TimeScale = GameClock.Speed1x;
                }

                if (Input.GetKeyDown(KeyCode.Alpha2))
                {
                    clock.TimeScale = GameClock.SpeedHalf;
                }

                if (Input.GetKeyDown(KeyCode.Alpha3))
                {
                    clock.TimeScale = GameClock.SpeedQuarter;
                }

                if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Space))
                {
                    clock.TogglePause();
                }
            }

            if (Input.GetKeyDown(KeyCode.C) && _cameraRig != null)
            {
                _cameraRig.ToggleHeight();
            }

            if (Input.GetKeyDown(KeyCode.O))
            {
                ToonOutlineRendererFeature.BlitEnabled = !ToonOutlineRendererFeature.BlitEnabled;
            }

            if (Input.GetKeyDown(KeyCode.P))
            {
                ToonOutlineRendererFeature.DepthNormalsPrepassEnabled =
                    !ToonOutlineRendererFeature.DepthNormalsPrepassEnabled;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.F))
            {
                FiveKUnitSpawner.SetAllClipSpeeds(world.EntityManager, 0f);
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                FiveKUnitSpawner.SetAllClipSpeeds(world.EntityManager, 1f);
            }

            if (Input.GetKeyDown(KeyCode.V))
            {
                FiveKUnitSpawner.SetBindPose(world.EntityManager, true);
            }

            if (Input.GetKeyDown(KeyCode.B))
            {
                FiveKUnitSpawner.SetBindPose(world.EntityManager, false);
            }

            if (Input.GetKeyDown(KeyCode.G) && _churn != null)
            {
                _churn.ChurnEnabled = !_churn.ChurnEnabled;
            }
        }

        private void OnGUI()
        {
            if (!_visible)
            {
                return;
            }

            GUI.skin.label.fontSize = 14;
            GUI.color = Color.white;
            const float pad = 10f;
            GUILayout.BeginArea(new Rect(pad, pad, 560f, 420f), GUI.skin.box);
            GUILayout.Label("Wasteland 5k toon / VAT prototype  (§6)");
            float scale = GameClock.Instance != null ? GameClock.Instance.TimeScale : 1f;
            GUILayout.Label(string.Format("frame {0:0.00} ms   fps {1:0.0}   clock x{2:0.00}", _delta * 1000f, _delta > 0f ? 1f / _delta : 0f, scale));

            uint n = FrameTimingManager.GetLatestTimings(1, _timings);
            if (n > 0)
            {
                FrameTiming t = _timings[0];
                GUILayout.Label(string.Format("cpu main {0:0.00} ms   cpu render {1:0.00} ms   gpu {2:0.00} ms", t.cpuFrameTime, t.cpuRenderFrameTime, t.gpuFrameTime));
            }

            float vatTime = Shader.GetGlobalFloat(PresentationRenderGlobalsSystem.VatTimeProperty);
            GUILayout.Label(string.Format("VATTime {0:0.00} s (wrap 3600, interpolated clock)", vatTime));

            if (_bootstrap != null)
            {
                GUILayout.Label(string.Format("infantry {0}   vehicles {1}   probes {2}", _bootstrap.InfantryCount, _bootstrap.VehicleCount, _bootstrap.ProbeCount));
            }

            if (_churn != null)
            {
                GUILayout.Label(string.Format("churn {0}   last {1:0.00} ms   {2}", _churn.SwitchCount, _churn.LastCostMs, _churn.ChurnEnabled ? "ON" : "OFF"));
            }

            if (_cameraRig != null)
            {
                GUILayout.Label(string.Format("camera {0:0} m  pitch 70  fov 35", _cameraRig.Height));
            }

            GUILayout.Label(string.Format(
                "outline blit {0}   DepthNormals prepass {1}",
                ToonOutlineRendererFeature.BlitEnabled ? "ON" : "OFF",
                ToonOutlineRendererFeature.DepthNormalsPrepassEnabled ? "ON" : "OFF"));
            GUILayout.Label("5k outline cost is the DepthNormals prepass (VAT sampled again), not the blit.");

            GUILayout.Label("1/2/3 speed   0/Space pause   C 65/110m   F freeze anim   R run");
            GUILayout.Label("V bind pose   B restore VAT   G churn   O outline   P prepass   H hud");
            GUILayout.EndArea();
        }
    }
}
