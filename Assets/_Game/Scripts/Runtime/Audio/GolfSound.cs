namespace Puttshack.Runtime.Audio
{
    /// <summary>Mixer buses. Names match the child groups of GolfMixer and its exposed "{Bus}Volume" parameters.</summary>
    public enum AudioBus
    {
        Music,
        Ambience,
        SFX,
        UI,
        Crowd,
    }

    /// <summary>Every sound the game asks the bank for. Serialized by value in <see cref="GolfSoundBank"/>: append, never renumber.</summary>
    public enum GolfSound
    {
        SurfBed = 0,
        SurfShore = 1,
        Wind = 2,
        Birds = 3,
        Gull = 4,
        CartHum = 5,

        CrowdMurmur = 10,
        ApplauseSmall = 11,
        ApplauseBig = 12,
        Cheer = 13,
        Ovation = 14,
        CrowdOoh = 15,
        CrowdAww = 16,

        StrikeDriver = 20,
        StrikeWood = 21,
        StrikeIron = 22,
        StrikeWedge = 23,
        StrikePutter = 24,
        /// <summary>Layered on the strike from a bunker lie.</summary>
        StrikeSand = 25,
        /// <summary>Layered on the strike from a rough lie.</summary>
        StrikeRough = 26,
        Whoosh = 27,
        ClubChange = 28,

        LandFairway = 30,
        LandRough = 31,
        LandGreen = 32,
        LandBunker = 33,
        LandCartPath = 34,
        Splash = 35,
        TreeHit = 36,

        RollGreen = 40,
        RollFairway = 41,
        RollCartPath = 42,
        CupDrop = 43,
        LipOut = 44,

        StingerAce = 50,
        StingerEagle = 51,
        StingerBirdie = 52,
        StingerPar = 53,
        StingerBogey = 54,
        StingerDouble = 55,
        StingerRound = 56,

        UiSelect = 60,
        UiConfirm = 61,
        UiBack = 62,
        UiTitle = 63,

        MusicMenu = 70,
        MusicRound = 71,
    }
}
