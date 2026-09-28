using UnityEngine;

namespace Wasteland.Prototypes
{
    /// <summary>
    /// Style-guide live camera (v1.6 §2): 70° pitch, 35° FOV, 65 m default / 110 m max.
    /// </summary>
    public sealed class FiveKCameraRig : MonoBehaviour
    {
        public const float DefaultHeight = 65f;
        public const float MaxHeight = 110f;
        public const float PitchFromHorizontal = 70f;
        public const float VerticalFov = 35f;

        [SerializeField] private Transform _lookTarget;
        [SerializeField] private Camera _camera;

        private float _height = DefaultHeight;

        public float Height
        {
            get { return _height; }
        }

        private void Awake()
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }

            if (_camera != null)
            {
                _camera.fieldOfView = VerticalFov;
                _camera.nearClipPlane = 0.3f;
                _camera.farClipPlane = 500f;
            }

            Apply();
        }

        public void ToggleHeight()
        {
            _height = Mathf.Approximately(_height, DefaultHeight) ? MaxHeight : DefaultHeight;
            Apply();
        }

        public void SetHeight(float height)
        {
            _height = height;
            Apply();
        }

        private void Apply()
        {
            Vector3 target = _lookTarget != null ? _lookTarget.position : Vector3.zero;
            float pitchRad = PitchFromHorizontal * Mathf.Deg2Rad;
            float back = _height / Mathf.Tan(pitchRad);
            transform.position = target + new Vector3(0f, _height, -back);
            transform.rotation = Quaternion.Euler(PitchFromHorizontal, 0f, 0f);
        }
    }
}
