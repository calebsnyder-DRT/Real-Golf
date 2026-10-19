using System.Collections.Generic;
using Puttshack.Content;
using Puttshack.Runtime.Clubs;
using UnityEngine;

namespace Puttshack.Runtime.Shop
{
    /// <summary>The plinth beside the golfer: shows the edited player's bag, or the focused club on its stand.</summary>
    public sealed class BagDisplay : MonoBehaviour
    {
        public CosmeticArt Art;
        public List<BagAppearance> Styles = new List<BagAppearance>();
        [Tooltip("Club models named Display_Club_<Kind>, shown instead of the bag on the Clubs tab when the club library has no model for the club.")]
        public List<GameObject> Clubs = new List<GameObject>();
        [Tooltip("Roster colour whose club set (ClubLibrary) the stand shows.")]
        public int ClubSet;
        [Tooltip("How the stand leans a library club: Euler degrees applied to the upright, face-forward club.")]
        public Vector3 ClubLean = new Vector3(-12f, 0f, 8f);

        private ClubModel _libraryClub;
        private string _libraryKey;

        public string ShownItem { get; private set; }
        public string ShownClub { get; private set; }

        public void ShowBag(string itemId)
        {
            ShownClub = null;
            foreach (GameObject club in Clubs) club.SetActive(false);
            if (_libraryClub != null) _libraryClub.gameObject.SetActive(false);
            if (!CosmeticCatalog.TryGet(itemId, out CosmeticItem bag)) return;
            foreach (BagAppearance style in Styles)
            {
                bool on = style.Style == bag.Style;
                style.gameObject.SetActive(on);
                if (on) style.Apply(bag, Art);
            }
            ShownItem = itemId;
        }

        public void ShowClub(string clubId)
        {
            foreach (BagAppearance style in Styles) style.gameObject.SetActive(false);
            bool library = ShowLibraryClub(clubId);
            string kind = ClubKind(clubId);
            foreach (GameObject club in Clubs) club.SetActive(!library && club.name.EndsWith(kind));
            ShownClub = clubId;
            ShownItem = null;
        }

        /// <summary>Stands the set's model for the club on the plinth, sole down; false when the library has none.</summary>
        private bool ShowLibraryClub(string clubId)
        {
            ClubLibrary library = ClubLibrary.Load();
            ClubLibrary.ClubSet set = library != null ? library.SetFor(ClubSet) : null;
            if (set == null || library.ModelFor(set, clubId) == null)
            {
                if (_libraryClub != null) _libraryClub.gameObject.SetActive(false);
                return false;
            }
            string key = clubId + "|" + set.Id;
            if (_libraryClub == null || _libraryKey != key)
            {
                if (_libraryClub != null)
                {
                    if (Application.isPlaying) Destroy(_libraryClub.gameObject);
                    else DestroyImmediate(_libraryClub.gameObject);
                }
                _libraryClub = library.Spawn(clubId, set, transform);
                _libraryKey = key;
                Transform t = _libraryClub.transform;
                t.localRotation = Quaternion.Euler(ClubLean) * Quaternion.Euler(0f, 0f, 180f);
                t.localPosition = Vector3.zero;
                if (_libraryClub.Body != null)
                {
                    float floor = _libraryClub.Body.bounds.min.y;
                    t.position += Vector3.up * (transform.position.y + 0.005f - floor);
                }
            }
            _libraryClub.gameObject.SetActive(true);
            return true;
        }

        /// <summary>Which display model stands in for a club id (the shop has five club silhouettes).</summary>
        public static string ClubKind(string clubId)
        {
            switch (clubId)
            {
                case "driver": return "Driver";
                case "wood3": return "Wood";
                case "pw":
                case "sw":
                case "lw": return "Wedge";
                case ClubCatalog.PutterId: return "Putter";
                default: return "Iron";
            }
        }
    }
}
