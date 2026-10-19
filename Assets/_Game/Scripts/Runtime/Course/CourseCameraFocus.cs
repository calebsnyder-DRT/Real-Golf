using Puttshack.Runtime.Shop;
using Puttshack.Runtime.Views;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// Depth of field that follows the gameplay camera's mode: putting and celebrations soften what lies beyond the
    /// green or the golfer, tee and flight views only blur the far coast. Gaussian (cheap, no bokeh) through a runtime
    /// global volume that outranks the course post volume; it stands down while the Pro Shop is open.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CourseCameraFocus : MonoBehaviour
    {
        [System.Serializable]
        public struct Focus
        {
            public float Start;
            public float End;
            [Range(0.5f, 1.5f)] public float MaxRadius;

            public Focus(float start, float end, float maxRadius)
            {
                Start = start;
                End = end;
                MaxRadius = maxRadius;
            }
        }

        public Focus Putting = new Focus(16f, 70f, 1f);
        public Focus Celebration = new Focus(9f, 45f, 1.2f);
        public Focus Setup = new Focus(240f, 900f, 0.6f);
        public Focus Flight = new Focus(420f, 1400f, 0.5f);
        [Tooltip("Seconds to settle on a new mode's focus.")]
        public float Blend = 0.6f;

        private GolfCameraRig _rig;
        private ProShopController _shop;
        private Volume _volume;
        private VolumeProfile _profile;
        private DepthOfField _dof;
        private Focus _current;

        private void Awake()
        {
            _rig = GetComponent<GolfCameraRig>();
            _shop = FindAnyObjectByType<ProShopController>(FindObjectsInactive.Include);
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "Camera Focus (Runtime)";
            _dof = _profile.Add<DepthOfField>(true);
            _dof.mode.Override(DepthOfFieldMode.Gaussian);
            _dof.highQualitySampling.Override(true);
            var go = new GameObject("Camera Focus Volume") { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 5f;
            _volume.sharedProfile = _profile;
            _current = Target();
            Write();
        }

        private void OnDestroy()
        {
            if (_profile != null) Destroy(_profile);
        }

        private void LateUpdate()
        {
            _volume.weight = _shop != null && _shop.IsOpen ? 0f : 1f;
            Focus target = Target();
            float t = Blend > 0f ? 1f - Mathf.Exp(-UnityEngine.Time.unscaledDeltaTime * 3f / Blend) : 1f;
            _current.Start = Mathf.Lerp(_current.Start, target.Start, t);
            _current.End = Mathf.Lerp(_current.End, target.End, t);
            _current.MaxRadius = Mathf.Lerp(_current.MaxRadius, target.MaxRadius, t);
            Write();
        }

        private Focus Target()
        {
            if (_rig == null) return Setup;
            switch (_rig.Mode)
            {
                case CameraMode.Putting: return Putting;
                case CameraMode.Celebration: return Celebration;
                case CameraMode.Setup: return Setup;
                default: return Flight;
            }
        }

        private void Write()
        {
            _dof.gaussianStart.Override(_current.Start);
            _dof.gaussianEnd.Override(Mathf.Max(_current.End, _current.Start + 1f));
            _dof.gaussianMaxRadius.Override(_current.MaxRadius);
        }
    }
}
