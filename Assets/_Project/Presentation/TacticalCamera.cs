#nullable disable
using UnityEngine;
using UnityEngine.InputSystem;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Top-down orthographic camera. North (+Z) is up. The listener stays on this transform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TacticalCamera : MonoBehaviour
    {
        Camera _camera;
        float _minSize = 150f;
        float _maxSize = 80000f;
        float _minX;
        float _maxX;
        float _minZ;
        float _maxZ;
        bool _bounded;

        public Camera Camera
        {
            get { return _camera != null ? _camera : (_camera = GetComponent<Camera>()); }
        }

        public float OrthographicSize
        {
            get { return Camera.orthographicSize; }
        }

        void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 20000f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.768f, 0.733f, 0.675f, 1f);
        }

        public void Frame(double minX, double maxX, double minZ, double maxZ)
        {
            _minX = (float)minX;
            _maxX = (float)maxX;
            _minZ = (float)minZ;
            _maxZ = (float)maxZ;
            _bounded = true;
            float centerX = (float)((minX + maxX) * 0.5);
            float centerZ = (float)((minZ + maxZ) * 0.5);
            float halfW = (float)((maxX - minX) * 0.5);
            float halfH = (float)((maxZ - minZ) * 0.5);
            float aspect = Camera.aspect > 0.01f ? Camera.aspect : (16f / 9f);
            float size = halfH;
            if (halfW / aspect > size)
            {
                size = halfW / aspect;
            }

            size *= 1.04f;
            _maxSize = size;
            _minSize = Mathf.Min(200f, size * 0.02f);
            Camera.orthographicSize = size;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Vector3 position = transform.position;
            position.x = centerX;
            position.z = centerZ;
            if (position.y < 10f)
            {
                position.y = 80f;
            }

            transform.position = position;
        }

        public void SetHeight(float metres)
        {
            Vector3 position = transform.position;
            position.y = metres;
            transform.position = position;
        }

        void Update()
        {
            Camera cam = Camera;
            float worldPerPixel = MapSpace.WorldUnitsPerPixel(cam.orthographicSize, Screen.height);
            Vector3 position = transform.position;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                float pan = cam.orthographicSize * 0.85f * Time.unscaledDeltaTime;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                {
                    position.z += pan;
                }

                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                {
                    position.z -= pan;
                }

                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                {
                    position.x += pan;
                }

                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                {
                    position.x -= pan;
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (mouse.middleButton.isPressed || mouse.rightButton.isPressed)
                {
                    position.x -= delta.x * worldPerPixel;
                    position.z -= delta.y * worldPerPixel;
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    Vector3 before = GroundPoint(cam, mouse.position.ReadValue());
                    float size = cam.orthographicSize * (scroll > 0f ? 0.9f : 1.111f);
                    cam.orthographicSize = Mathf.Clamp(size, _minSize, _maxSize);
                    Vector3 after = GroundPoint(cam, mouse.position.ReadValue());
                    position.x += before.x - after.x;
                    position.z += before.z - after.z;
                }
            }

            if (_bounded)
            {
                position.x = Mathf.Clamp(position.x, _minX, _maxX);
                position.z = Mathf.Clamp(position.z, _minZ, _maxZ);
            }

            transform.position = position;
        }

        public bool TryPickScreen(out Vector2 screen)
        {
            screen = default;
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return false;
            }

            if (mouse.position.ReadValue().y > Screen.height - 110f)
            {
                return false;
            }

            screen = mouse.position.ReadValue();
            return true;
        }

        public static Vector3 GroundPoint(Camera cam, Vector2 screen)
        {
            Ray ray = cam.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.y) < 0.0001f)
            {
                return ray.origin;
            }

            float t = -ray.origin.y / ray.direction.y;
            return ray.origin + ray.direction * t;
        }
    }
}
