using Puttshack.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace Puttshack.Runtime.Shop
{
    /// <summary>
    /// Off-stage photo booth under the shop floor: a second dressed golfer, its own lights and a cream backdrop. Renders
    /// each player tab's head-and-shoulders portrait only when that player's look changes. The subject is dressed in one
    /// frame and photographed in the next, one portrait at a time, so part toggles and material swaps have reached the
    /// renderer before the shot.
    /// </summary>
    public sealed class PortraitStudio : MonoBehaviour
    {
        public Camera Camera;
        public GolferAppearance Subject;
        public Animator SubjectAnimator;
        public int Width = 400;
        public int Height = 288;
        [Tooltip("Head-bone height (studio space) the baked camera framing was set up for.")]
        public float HeadReference = 1.58f;

        private const int Slots = 6;
        private readonly RenderTexture[] _textures = new RenderTexture[Slots];
        private readonly string[] _keys = new string[Slots];
        private readonly string[] _wantedKeys = new string[Slots];
        private readonly Loadout[] _wantedLooks = new Loadout[Slots];
        private readonly int[] _wantedCharacter = new int[Slots];
        private readonly PlayerProfile[] _wantedProfiles = new PlayerProfile[Slots];
        private int _staged = -1;
        private string _stagedKey;
        private bool _framed;
        private Vector3 _cameraHome;

        private void Awake()
        {
            // The booth subject never shadows its backdrop, including bodies added to the golfer prefab after the shop bake.
            if (Subject == null) return;
            foreach (Renderer r in Subject.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>True while a requested portrait has not been photographed yet.</summary>
        public bool Busy
        {
            get
            {
                for (int i = 0; i < Slots; i++)
                {
                    if (_wantedKeys[i] != null && _wantedKeys[i] != _keys[i]) return true;
                }
                return false;
            }
        }

        /// <summary>The player's portrait texture; it updates in place a frame or two after the look changes.</summary>
        public Texture Portrait(int index, Loadout look, bool standIn) => Portrait(index, look, standIn ? GolferAppearance.StandIn : 0);

        /// <summary>The player's portrait texture for a character index (see <see cref="GolferAppearance.CharacterOf"/>).</summary>
        public Texture Portrait(int index, Loadout look, int character, PlayerProfile profile = null)
        {
            if (Camera == null || Subject == null || index < 0 || index >= Slots || look == null) return null;
            RenderTexture rt = _textures[index];
            if (rt == null)
            {
                rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
                {
                    name = $"Portrait P{index + 1}",
                    useMipMap = true,
                    autoGenerateMips = true,
                    antiAliasing = 1,
                };
                rt.Create();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                GL.Clear(true, true, Camera.backgroundColor);
                RenderTexture.active = previous;
                _textures[index] = rt;
            }
            if (!rt.IsCreated())
            {
                rt.Create();
                _keys[index] = null;
            }
            string key = GolferAppearance.Key(look, character, profile);
            if (_wantedKeys[index] != key)
            {
                _wantedKeys[index] = key;
                _wantedLooks[index] = look.Clone();
                _wantedCharacter[index] = character;
                _wantedProfiles[index] = profile?.Clone();
            }
            return rt;
        }

        /// <summary>Forces every portrait to re-render (e.g. after the lighting changed).</summary>
        public void Invalidate()
        {
            for (int i = 0; i < Slots; i++) _keys[i] = null;
        }

        private void LateUpdate()
        {
            if (_staged >= 0)
            {
                Camera.targetTexture = _textures[_staged];
                Camera.Render();
                Camera.targetTexture = null;
                _keys[_staged] = _stagedKey;
                _staged = -1;
            }
            for (int i = 0; i < Slots; i++)
            {
                if (_wantedKeys[i] == null || _wantedKeys[i] == _keys[i] || _textures[i] == null) continue;
                Subject.Apply(_wantedLooks[i], _wantedCharacter[i], _wantedProfiles[i]);
                Animator posed = Subject.ShownAnimator != null ? Subject.ShownAnimator : SubjectAnimator;
                if (posed != null && posed.isActiveAndEnabled) posed.Update(0f);
                FrameHead(posed);
                _staged = i;
                _stagedKey = _wantedKeys[i];
                break;
            }
        }

        /// <summary>Raises or lowers the camera with the subject's head so taller and shorter golfers frame the same.</summary>
        private void FrameHead(Animator posed)
        {
            Transform head = posed != null && posed.isHuman ? posed.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null || Camera == null) return;
            if (!_framed)
            {
                _cameraHome = Camera.transform.localPosition;
                _framed = true;
            }
            float lift = transform.InverseTransformPoint(head.position).y - HeadReference;
            Camera.transform.localPosition = _cameraHome + Vector3.up * lift;
        }

        private void OnDestroy()
        {
            foreach (RenderTexture rt in _textures)
            {
                if (rt != null) rt.Release();
            }
        }
    }
}
