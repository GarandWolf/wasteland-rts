using UnityEngine;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Presentation-only clock for pause and game speed. Does not write
    /// <see cref="Time.timeScale"/> so UI, audio, and editor tooling stay at 1×.
    /// The VAT shader reads wrapped seconds derived from this clock or from
    /// interpolated sim time — never from <c>Time.deltaTime</c>.
    /// </summary>
    public sealed class GameClock : MonoBehaviour
    {
        public const float Speed1x = 1f;
        public const float SpeedHalf = 0.5f;
        public const float SpeedQuarter = 0.25f;

        [SerializeField] private float _timeScale = Speed1x;

        private static GameClock _instance;
        private float _time;

        public static GameClock Instance
        {
            get { return _instance; }
        }

        public float TimeScale
        {
            get { return _timeScale; }
            set { _timeScale = value; }
        }

        public float Time
        {
            get { return _time; }
        }

        public float DeltaTime
        {
            get { return UnityEngine.Time.unscaledDeltaTime * _timeScale; }
        }

        public bool IsPaused
        {
            get { return _timeScale <= 0f; }
        }

        public static bool TryGetTime(out float seconds)
        {
            if (_instance != null)
            {
                seconds = _instance._time;
                return true;
            }

            seconds = 0f;
            return false;
        }

        public void Pause()
        {
            _timeScale = 0f;
        }

        public void TogglePause()
        {
            _timeScale = _timeScale > 0f ? 0f : Speed1x;
        }

        private void OnEnable()
        {
            _instance = this;
        }

        private void OnDisable()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            _time += DeltaTime;
        }
    }
}
