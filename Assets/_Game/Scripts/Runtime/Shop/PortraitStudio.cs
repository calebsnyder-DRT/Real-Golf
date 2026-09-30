using Puttshack.Domain;
using UnityEngine;

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

        private const int Slots = 6;
        private readonly RenderTexture[] _textures = new RenderTexture[Slots];
        private readonly string[] _keys = new string[Slots];
        private readonly string[] _wantedKeys = new string[Slots];
        private readonly Loadout[] _wantedLooks = new Loadout[Slots];
        private readonly bool[] _wantedStandIn = new bool[Slots];
        private int _staged = -1;
        private string _stagedKey;

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
        public Texture Portrait(int index, Loadout look, bool standIn)
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
            string key = GolferAppearance.Key(look, standIn);
            if (_wantedKeys[index] != key)
            {
                _wantedKeys[index] = key;
                _wantedLooks[index] = look.Clone();
                _wantedStandIn[index] = standIn;
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
                Subject.Apply(_wantedLooks[i], _wantedStandIn[i]);
                if (SubjectAnimator != null) SubjectAnimator.Update(0f);
                _staged = i;
                _stagedKey = _wantedKeys[i];
                break;
            }
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
