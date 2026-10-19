using System;
using Puttshack.Domain;
using Puttshack.Input;
using Puttshack.Protocol;
using Puttshack.Rules;
using Puttshack.Session;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Puttshack.Runtime.Hud
{
    public sealed class HudContext
    {
        public SessionSnapshot Snapshot;
        public double NowMs;
        public ShotResult Preview;
        public InputAck LastAck;
        public bool ShowScorecard;
        public string Notice;
        public string Hints;
        public AssistanceProfileId LobbyAssistance;
        public bool LobbyPaceCap;
        public bool HomeInProShop;
        /// <summary>The round flow's hole title is up: the briefing card waits behind it.</summary>
        public bool HoleIntro;
        /// <summary>The round flow's winner moment is up: the round-complete card waits behind it.</summary>
        public bool RoundFinale;
        /// <summary>The round-complete card offers play again (ENTER) and home (ESC).</summary>
        public bool PlayAgain;
        /// <summary>Overrides the scorecard's TAB CLOSE hint when the round flow shows the card as a page.</summary>
        public string ScorecardFooter;
    }

    /// <summary>A player pill: initial disc, name and an optional trailing label (stroke, shot or score).</summary>
    [Serializable]
    public sealed class HudBadge
    {
        public GameObject Root;
        public Image Fill;
        public Image Disc;
        public TMP_Text Initial;
        public TMP_Text Name;
        public TMP_Text Extra;
    }

    /// <summary>A large identity circle with an initial; <see cref="Ring"/> outlines it.</summary>
    [Serializable]
    public sealed class HudDisc
    {
        public GameObject Root;
        public Image Fill;
        public Image Ring;
        public TMP_Text Initial;
    }

    /// <summary>A pill sized to its text.</summary>
    [Serializable]
    public sealed class HudChip
    {
        public GameObject Root;
        public Image Fill;
        public TMP_Text Text;
    }

    /// <summary>
    /// A row of outlined keycaps (key name printed inside) and labels laid out by a HorizontalLayoutGroup; pooled children
    /// are ordered per hint.
    /// </summary>
    [Serializable]
    public sealed class HudHintRow
    {
        public RectTransform Root;
        public float KeyHeight = 32f;
        public Image[] Keys = new Image[0];
        public LayoutElement[] KeyLayouts = new LayoutElement[0];
        public TMP_Text[] Legends = new TMP_Text[0];
        public TMP_Text[] Labels = new TMP_Text[0];
        public GameObject[] Gaps = new GameObject[0];
        [NonSerialized] public string Current;
    }

    /// <summary>A full-screen overlay. <see cref="Footer"/> carries the primary action; <see cref="Secondary"/> any others.</summary>
    [Serializable]
    public sealed class HudCard
    {
        public GameObject Root;
        public HudHintRow Footer;
        public HudHintRow Secondary;
    }

    [Serializable]
    public sealed class HudStat
    {
        public GameObject Root;
        public Image Fill;
        public TMP_Text Label;
        public TMP_Text Value;
    }

    [Serializable]
    public sealed class HudLobbySlot
    {
        public GameObject Root;
        public Image Fill;
        public Image Outline;
        public HudDisc Disc;
        public TMP_Text Name;
    }

    [Serializable]
    public sealed class HudHoleColumn
    {
        public GameObject Root;
        public LayoutElement Layout;
        public Image Header;
        public TMP_Text Name;
        public TMP_Text Score;
        public Image WordFill;
        public Image WordLine;
        public TMP_Text Word;
    }

    [Serializable]
    public sealed class HudStandingRow
    {
        public GameObject Root;
        public Image Fill;
        public TMP_Text Rank;
        public HudDisc Disc;
        public TMP_Text Name;
        public TMP_Text Total;
        public TMP_Text ToPar;
        public TMP_Text Thru;
    }

    [Serializable]
    public sealed class HudScoreRow
    {
        public GameObject Root;
        public Image Band;
        public Image Bar;
        public TMP_Text[] Cells = new TMP_Text[0];
        public Image[] Marks = new Image[0];
    }
}
