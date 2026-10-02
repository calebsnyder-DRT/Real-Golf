using Puttshack.Content;
using Puttshack.Domain;
using UnityEngine;

namespace Puttshack.Runtime.Shop
{
    /// <summary>
    /// The golfer standing at the ball on the course, dressed in the shooter's saved Pro Shop loadout, with their bag
    /// beside them. Addresses the ball with the selected club, swings when the shot resolves (the presentation waits for
    /// the clubhead to reach the ball) and reacts to the result. Baked into Main.unity next to the Pro Shop; purely
    /// visual, like everything the shop sells.
    /// </summary>
    public sealed class CourseGolfer : MonoBehaviour
    {
        public GolferAppearance Appearance;
        public BagDisplay Bag;
        [Tooltip("Distance from the ball to the golfer's feet, across the aim line (metres), when no address pose was measured.")]
        public float AddressOffset = 0.75f;
        [Tooltip("Where the bag stands relative to the golfer: x = behind (m), z = further from the ball (m).")]
        public Vector2 BagOffset = new Vector2(1.1f, 0.9f);

        private string _dressedKey;
        private int _swungShot = -1;
        private int _reactedShot = -1;

        private GolferMotion Motion => Appearance != null ? Appearance.GetComponent<GolferMotion>() : null;

        public void Show(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        public void Stand(Vector3 ballPosition, Vector3 aimForward, PlayerState player) => Stand(ballPosition, aimForward, player, null, false);

        /// <summary>
        /// Places and dresses the golfer at the ball; with <paramref name="address"/> they take their stance for the club,
        /// with <paramref name="wait"/> they stand by with the club put away until their turn starts.
        /// </summary>
        public void Stand(Vector3 ballPosition, Vector3 aimForward, PlayerState player, string clubId, bool address, bool wait = false)
        {
            Dress(player);
            Vector3 fwd = new Vector3(aimForward.x, 0f, aimForward.z).normalized;
            if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward;
            Vector3 left = Vector3.Cross(fwd, Vector3.up);
            Quaternion facing = Quaternion.LookRotation(-left, Vector3.up);
            GolferMotion motion = Motion;
            if (clubId != null && motion != null && motion.BallOffset(clubId, out Vector3 ball))
            {
                Vector3 flat = new Vector3(ball.x, 0f, ball.z);
                transform.SetPositionAndRotation(ballPosition - facing * flat, facing);
            }
            else
            {
                transform.SetPositionAndRotation(ballPosition + left * AddressOffset - fwd * 0.15f, facing);
            }
            if (Bag != null)
            {
                Bag.transform.position = transform.position - fwd * BagOffset.x + left * BagOffset.y;
                Bag.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
            }
            if (motion == null) return;
            if (wait) motion.Wait();
            else if (address && clubId != null) motion.Address(clubId);
        }

        /// <summary>Swings once per shot; returns seconds until impact so the ball can leave on contact.</summary>
        public float Swing(ShotRecord shot)
        {
            GolferMotion motion = Motion;
            if (shot?.Intent == null || motion == null || !gameObject.activeInHierarchy || shot.ShotId == _swungShot) return 0f;
            _swungShot = shot.ShotId;
            return motion.Swing(shot.Intent.ClubId);
        }

        public void React(ShotRecord shot, Vector3 cup)
        {
            GolferMotion motion = Motion;
            if (shot == null || motion == null || !gameObject.activeInHierarchy || shot.ShotId == _reactedShot) return;
            _reactedShot = shot.ShotId;
            motion.React(GolferMotion.ReactionTo(shot, cup));
        }

        private void Dress(PlayerState player)
        {
            if (player == null || Appearance == null) return;
            Loadout look = CosmeticCatalog.LoadoutOf(player);
            int character = GolferAppearance.CharacterOf(player);
            string key = player.Id + "|" + GolferAppearance.Key(look, character) + "|" + look.Bag;
            if (key == _dressedKey) return;
            int before = Appearance.Shown;
            Appearance.Apply(look, character);
            GolferMotion motion = Motion;
            if (Appearance.Shown != before && motion != null) motion.Refresh();
            if (Bag != null) Bag.ShowBag(look.Bag);
            _dressedKey = key;
        }
    }
}
