using Puttshack.Runtime.Shop;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Puttshack.Runtime.Course
{
    /// <summary>
    /// Keeps the scene's directional light at the course's low golden-hour sun and hands the Pro Shop its original
    /// daylight while the shop is open: the shop's window wall faces the old high sun (ProShopBaker.ShopYaw), so a
    /// low coastal sun would leave its floor unlit. On the course the sun carries a drifting cloud-shadow cookie and
    /// its lens flare; the shop gets neither. Values are written by CourseEnvironmentBaker / CourseAtmosphere.
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

        [Header("Cloud shadows")]
        public Texture CloudCookie;
        public float CloudScale = 700f;
        public Vector2 CloudWind = new Vector2(2.4f, 1.1f);

        private Light _light;
        private UniversalAdditionalLightData _lightData;
        private LensFlareComponentSRP _flare;
        private ProShopController _shop;
        private bool _applied;
        private bool _shopLit;
        private Vector2 _cloudOffset;

        private void Awake()
        {
            _light = GetComponent<Light>();
            _lightData = _light.GetUniversalAdditionalLightData();
            _flare = GetComponent<LensFlareComponentSRP>();
            _shop = FindAnyObjectByType<ProShopController>(FindObjectsInactive.Include);
            if (CloudCookie == null) CloudCookie = _light.cookie;
            Apply(false);
        }

        private void LateUpdate()
        {
            bool open = _shop != null && _shop.IsOpen;
            if (!_applied || open != _shopLit) Apply(open);
            if (!_shopLit && _light.cookie != null)
            {
                _cloudOffset += CloudWind * UnityEngine.Time.deltaTime;
                _cloudOffset.x %= CloudScale;
                _cloudOffset.y %= CloudScale;
                _lightData.lightCookieOffset = _cloudOffset;
            }
        }

        /// <summary>Re-applies the current course or shop values after they were changed (e.g. a new lighting preset).</summary>
        public void Refresh()
        {
            if (_light == null) return;
            Apply(_shopLit);
        }

        private void Apply(bool shop)
        {
            _applied = true;
            _shopLit = shop;
            transform.rotation = Quaternion.Euler(shop ? ShopEuler : CourseEuler);
            _light.color = shop ? ShopColor : CourseColor;
            _light.intensity = shop ? ShopIntensity : CourseIntensity;
            _light.shadowStrength = shop ? ShopShadowStrength : CourseShadowStrength;
            _light.cookie = shop ? null : CloudCookie;
            if (_light.cookie != null) _lightData.lightCookieSize = new Vector2(CloudScale, CloudScale);
            if (_flare != null) _flare.enabled = !shop;
        }
    }
}
