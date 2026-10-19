using Puttshack.Runtime.Shop;
using UnityEngine;

namespace Puttshack.Runtime.Clubs
{
    /// <summary>
    /// Shows the shooter's own club from <see cref="ClubLibrary"/> in the golfer's hands. Rides the same lead-hand seat
    /// (<c>ClubGrip_&lt;Kind&gt;</c>) the baked <c>Club_&lt;Kind&gt;</c> skin is bound to, follows its visibility (which
    /// <see cref="GolferAppearance.Hold"/> drives) and hides that skin's renderer while it stands in. Grip IK and impact
    /// timing still come from the seat, so nothing else changes. Without a library model the baked club shows as before.
    /// </summary>
    public sealed class HeldClubView : MonoBehaviour
    {
        public GolferAppearance Appearance;

        private string _clubId;
        private int _colorIndex;
        private ClubModel _shown;
        private string _shownKey;
        private SkinnedMeshRenderer _hidden;

        public ClubModel Shown => _shown != null && _shown.gameObject.activeSelf ? _shown : null;

        /// <summary>The club the golfer will hold for <paramref name="colorIndex"/>'s set (null: none).</summary>
        public void Hold(string clubId, int colorIndex)
        {
            _clubId = clubId;
            _colorIndex = colorIndex;
            Sync();
        }

        private void LateUpdate() => Sync();

        private void OnDisable() => Restore();

        private void OnDestroy()
        {
            Restore();
            if (_shown != null) Destroy(_shown.gameObject);
        }

        private void Sync()
        {
            if (Appearance == null) Appearance = GetComponent<GolferAppearance>();
            ClubLibrary library = ClubLibrary.Load();
            string held = Appearance != null ? Appearance.HeldClub : null;
            var skin = held != null ? Appearance.ShownPart(GolferAppearance.ClubPrefix + held) as SkinnedMeshRenderer : null;
            Transform seat = skin != null ? Seat(skin) : null;
            ClubLibrary.ClubSet set = library != null ? library.SetFor(_colorIndex) : null;
            ClubModel model = library != null && _clubId != null ? library.ModelFor(set, _clubId) : null;
            if (seat == null || model == null || model.Kind != held)
            {
                Restore();
                if (_shown != null) _shown.gameObject.SetActive(false);
                return;
            }
            string key = _clubId + "|" + set.Id;
            if (_shown == null || _shownKey != key)
            {
                if (_shown != null) Destroy(_shown.gameObject);
                _shown = library.Spawn(_clubId, set, seat);
                _shownKey = key;
            }
            if (_shown.transform.parent != seat)
            {
                _shown.transform.SetParent(seat, false);
                _shown.transform.localPosition = Vector3.zero;
                _shown.transform.localRotation = Quaternion.identity;
                _shown.transform.localScale = Vector3.one;
            }
            bool visible = skin.gameObject.activeInHierarchy;
            if (_shown.gameObject.activeSelf != visible) _shown.gameObject.SetActive(visible);
            if (_hidden != skin) Restore();
            if (skin.enabled)
            {
                skin.enabled = false;
                _hidden = skin;
            }
        }

        private void Restore()
        {
            if (_hidden != null) _hidden.enabled = true;
            _hidden = null;
        }

        private static Transform Seat(SkinnedMeshRenderer skin)
        {
            foreach (Transform b in skin.bones)
            {
                if (b != null && b.name.StartsWith("ClubGrip_", System.StringComparison.Ordinal)) return b;
            }
            return null;
        }
    }
}
