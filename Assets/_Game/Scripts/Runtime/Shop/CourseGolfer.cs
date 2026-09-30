using Puttshack.Content;
using Puttshack.Domain;
using UnityEngine;

namespace Puttshack.Runtime.Shop
{
    /// <summary>
    /// The golfer standing at the ball on the course, dressed in the shooter's saved Pro Shop loadout, with their bag
    /// beside them. Baked into Main.unity next to the Pro Shop; purely visual, like everything the shop sells.
    /// </summary>
    public sealed class CourseGolfer : MonoBehaviour
    {
        public GolferAppearance Appearance;
        public BagDisplay Bag;
        [Tooltip("Distance from the ball to the golfer's feet, across the aim line (metres).")]
        public float AddressOffset = 0.75f;
        [Tooltip("Where the bag stands relative to the golfer: x = behind (m), z = further from the ball (m).")]
        public Vector2 BagOffset = new Vector2(1.1f, 0.9f);

        private string _dressedKey;

        public void Show(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        public void Stand(Vector3 ballPosition, Vector3 aimForward, PlayerState player)
        {
            Vector3 fwd = new Vector3(aimForward.x, 0f, aimForward.z).normalized;
            if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward;
            Vector3 left = Vector3.Cross(fwd, Vector3.up);
            transform.position = ballPosition + left * AddressOffset - fwd * 0.15f;
            transform.rotation = Quaternion.LookRotation(-left, Vector3.up);
            if (Bag != null)
            {
                Bag.transform.position = transform.position - fwd * BagOffset.x + left * BagOffset.y;
                Bag.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
            }
            Dress(player);
        }

        private void Dress(PlayerState player)
        {
            if (player == null || Appearance == null) return;
            Loadout look = CosmeticCatalog.LoadoutOf(player);
            bool standIn = GolferAppearance.IsStandIn(player);
            string key = player.Id + "|" + GolferAppearance.Key(look, standIn) + "|" + look.Bag;
            if (key == _dressedKey) return;
            Appearance.Apply(look, standIn);
            if (Bag != null) Bag.ShowBag(look.Bag);
            _dressedKey = key;
        }
    }
}
