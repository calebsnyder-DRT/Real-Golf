using System.Collections.Generic;
using System.IO;
using Puttshack.Content;
using Puttshack.Runtime.Hud;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Puttshack.EditorTools
{
    /// <summary>Fonts and sprites used by the HUD builder, and the art tables handed to <see cref="HudView"/>.</summary>
    public static partial class HudUIBuilder
    {
        private static TMP_FontAsset _display, _extra, _bold, _semi;
        private static Sprite _glass, _card, _accent, _plate, _plateLine, _pill, _washCorner, _washBottom, _rule, _ruleL, _dividerV;
        private static Sprite _disc, _ring, _ringGlow, _badgeCheck, _windArrow, _windDial, _trophy, _pause, _alert, _players, _stopwatch, _target, _check;
        private static Sprite _laneKey, _laneFrame, _laneGlow, _lanePointer, _meterTrack, _meterFill, _meterTicks, _meterPointer;
        private static Sprite _toggleOn, _toggleOff, _emblem, _emblemSm, _wordCream, _wordInk, _paper, _paperLine, _soft, _keyBlank;
        private static Sprite[] _scoreMarks;
        private static readonly Dictionary<string, Sprite> KeySprites = new Dictionary<string, Sprite>();
        private static readonly string[] LieIds = { "tee", "fairway", "rough", "bunker", "green", "water", "path", "ob" };

        private static void AssignArt(HudView view)
        {
            var keycaps = new List<HudSprite>();
            foreach (KeyValuePair<string, Sprite> kv in KeySprites) keycaps.Add(new HudSprite { Id = kv.Key, Sprite = kv.Value });
            view.Keycaps = keycaps.ToArray();
            view.KeyBlank = _keyBlank;
            var clubs = new List<HudSprite>();
            foreach (ClubDefinition club in ClubCatalog.All)
                clubs.Add(new HudSprite { Id = club.Id, Sprite = Icon("club." + club.Id) });
            view.ClubIcons = clubs.ToArray();
            var lies = new List<HudSprite>();
            foreach (string id in LieIds)
            {
                lies.Add(new HudSprite { Id = id, Sprite = Lie(id) });
                lies.Add(new HudSprite { Id = id + "_sm", Sprite = Lie(id + "_sm") });
            }
            view.LieBadges = lies.ToArray();
            view.ToggleOn = _toggleOn;
            view.ToggleOff = _toggleOff;
            view.MarkBirdie = _scoreMarks[0];
            view.MarkEagle = _scoreMarks[1];
            view.MarkBogey = _scoreMarks[2];
            view.MarkDouble = _scoreMarks[3];
        }

        private static void LoadArt()
        {
            ProShopFonts.EnsureAll();
            _display = ProShopFonts.Display;
            _extra = ProShopFonts.ExtraBold;
            _bold = ProShopFonts.Bold;
            _semi = ProShopFonts.SemiBold;
            if (_display == null || _extra == null || _bold == null || _semi == null)
                throw new System.InvalidOperationException("Pro Shop TMP fonts are missing.");

            _glass = Hud("hud_glass");
            _card = Hud("hud_card");
            _accent = Hud("hud_card_accent");
            _plate = Hud("hud_plate");
            _plateLine = Hud("hud_plate_line");
            _pill = Hud("hud_pill");
            _washCorner = Hud("hud_wash_corner");
            _washBottom = Hud("hud_wash_bottom");
            _rule = Hud("hud_rule_fade");
            _ruleL = Hud("hud_rule_fade_l");
            _dividerV = Hud("hud_divider_v");
            _disc = Hud("hud_disc");
            _ring = Hud("hud_ring");
            _ringGlow = Hud("hud_ring_glow");
            _badgeCheck = Hud("hud_badge_check");
            _windArrow = Hud("icon_wind_arrow");
            _windDial = Hud("hud_wind_dial");
            _trophy = Hud("icon_trophy");
            _pause = Hud("icon_pause");
            _alert = Hud("icon_alert");
            _players = Hud("icon_players");
            _stopwatch = Hud("icon_stopwatch");
            _target = Hud("icon_target");
            _check = Hud("icon_check");
            _laneKey = Hud("hud_lane_key");
            _laneFrame = Hud("hud_lane_frame");
            _laneGlow = Hud("hud_lane_glow");
            _lanePointer = Hud("hud_lane_pointer");
            _meterTrack = Hud("hud_meter_track");
            _meterFill = Hud("hud_meter_fill");
            _meterTicks = Hud("hud_meter_ticks");
            _meterPointer = Hud("hud_meter_pointer");
            _toggleOn = Hud("hud_toggle_on");
            _toggleOff = Hud("hud_toggle_off");
            _emblem = Hud("hud_emblem");
            _emblemSm = Hud("hud_emblem_sm");
            _wordCream = Hud("hud_wordmark_cream");
            _wordInk = Hud("hud_wordmark_ink");
            _keyBlank = Hud("key_blank");
            _scoreMarks = new[] { Hud("score_circle"), Hud("score_circle2"), Hud("score_square"), Hud("score_square2") };
            _paper = ShopSprite("ps_paper");
            _paperLine = ShopSprite("ps_chamfer_line");
            _soft = ShopSprite("ps_soft_fill");

            KeySprites.Clear();
            foreach (string file in Directory.GetFiles(HudBaker.SpriteRoot, "key_*.png"))
            {
                string id = Path.GetFileNameWithoutExtension(file).Substring(4);
                if (id != "blank") KeySprites[id] = Hud("key_" + id);
            }
        }

        private static Sprite Hud(string name)
        {
            string path = HudBaker.SpriteRoot + name + ".png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new FileNotFoundException("Missing HUD sprite; run ArtSource/UI/HUD/make_hud_ui.py, then Puttshack > HUD > Bake HUD.", path);
            return sprite;
        }

        private static Sprite ShopSprite(string name)
        {
            string path = ProShopImportRules.SpriteRoot + name + ".png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new FileNotFoundException("Missing Pro Shop sprite.", path);
            return sprite;
        }

        private static Sprite Lie(string id) => Hud("lie_" + id);

        private static Sprite Icon(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(ProShopImportRules.IconRoot + name + ".png");
    }
}
