namespace RealGolf.Data
{
    public enum SurfaceType
    {
        TeeBox,
        Fairway,
        Rough,
        DeepRough,
        Green,
        /// <summary>Fringe cut around the putting surface: quicker than rough, slower than green.</summary>
        Collar,
        Bunker,
        Water,
        OutOfBounds
    }

    public enum GamePhase
    {
        Loading,
        HoleIntro,
        Aiming,
        Swinging,
        BallInMotion,
        HoleComplete,
        RoundComplete
    }

    /// <summary>
    /// Stages of the classic three-click swing: the first click starts the meter,
    /// the second sets power, the third sets face contact.
    /// </summary>
    public enum SwingStage
    {
        Ready,
        Power,
        Accuracy,
        Complete
    }

    public static class SurfaceExtensions
    {
        /// <summary>Fraction of clubhead speed that survives contact from this lie.</summary>
        public static float PowerMultiplier(this SurfaceType surface) => surface switch
        {
            SurfaceType.TeeBox => 1f,
            SurfaceType.Fairway => 1f,
            SurfaceType.Green => 1f,
            SurfaceType.Collar => 0.96f,
            SurfaceType.Rough => 0.82f,
            SurfaceType.DeepRough => 0.62f,
            SurfaceType.Bunker => 0.7f,
            _ => 1f
        };

        /// <summary>Extra dispersion in degrees applied to shots struck from this lie.</summary>
        public static float AccuracyPenaltyDegrees(this SurfaceType surface) => surface switch
        {
            SurfaceType.Collar => 0.5f,
            SurfaceType.Rough => 2.5f,
            SurfaceType.DeepRough => 5f,
            SurfaceType.Bunker => 3.5f,
            _ => 0f
        };

        /// <summary>Linear drag applied to a ball rolling on this surface.</summary>
        public static float RollDrag(this SurfaceType surface) => surface switch
        {
            SurfaceType.Green => 0.6f,
            SurfaceType.Collar => 1.0f,
            SurfaceType.Fairway => 1.4f,
            SurfaceType.TeeBox => 1.4f,
            SurfaceType.Rough => 3.5f,
            SurfaceType.DeepRough => 6f,
            SurfaceType.Bunker => 9f,
            _ => 2f
        };

        /// <summary>How much vertical energy a bounce retains on this surface.</summary>
        public static float Bounciness(this SurfaceType surface) => surface switch
        {
            SurfaceType.Green => 0.42f,
            SurfaceType.Collar => 0.40f,
            SurfaceType.Fairway => 0.38f,
            SurfaceType.TeeBox => 0.38f,
            SurfaceType.Rough => 0.2f,
            SurfaceType.DeepRough => 0.1f,
            SurfaceType.Bunker => 0.05f,
            _ => 0.3f
        };

        public static bool IsPlayable(this SurfaceType surface) =>
            surface != SurfaceType.Water && surface != SurfaceType.OutOfBounds;

        public static string DisplayName(this SurfaceType surface) => surface switch
        {
            SurfaceType.TeeBox => "Tee Box",
            SurfaceType.DeepRough => "Deep Rough",
            SurfaceType.OutOfBounds => "Out of Bounds",
            _ => surface.ToString()
        };
    }
}
