using Puttshack.Runtime.Shop;
using UnityEngine;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// Keeps the scene's directional light at the course's low golden-hour sun and hands the Pro Shop its original
    /// daylight while the shop is open: the shop's window wall faces the old high sun (ProShopBaker.ShopYaw), so a
    /// low coastal sun would leave its floor unlit. Values are written by CourseEnvironmentBaker.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class CourseSunRig : MonoBehaviour
    {
        public Vector3 CourseEuler = new Vector3(20f, 75f, 0f);
        public Color CourseColor = new Color(1f, 0.74f, 0.5f);
        public float CourseIntensity = 3.3f;
        public float CourseShadowStrength = 0.9f;

        public Vector3 ShopEuler = new Vector3(48f, -35f, 0f);
        public Color ShopColor = new Color(1f, 0.96f, 0.88f);
        public float ShopIntensity = 1.25f;
        public float ShopShadowStrength = 1f;

        private Light _light;
        private ProShopController _shop;
        private bool _applied;
        private bool _shopLit;

        private void Awake()
        {
            _light = GetComponent<Light>();
            _shop = FindAnyObjectByType<ProShopController>(FindObjectsInactive.Include);
            Apply(false);
        }

        private void LateUpdate()
        {
            bool open = _shop != null && _shop.IsOpen;
            if (!_applied || open != _shopLit) Apply(open);
        }

        private void Apply(bool shop)
        {
            _applied = true;
            _shopLit = shop;
            transform.rotation = Quaternion.Euler(shop ? ShopEuler : CourseEuler);
            _light.color = shop ? ShopColor : CourseColor;
            _light.intensity = shop ? ShopIntensity : CourseIntensity;
            _light.shadowStrength = shop ? ShopShadowStrength : CourseShadowStrength;
        }
    }
}
