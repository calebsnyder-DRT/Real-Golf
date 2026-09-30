using RealGolf.Data;
using UnityEngine;

namespace RealGolf.Gameplay
{
    public sealed class GolfPlayer
    {
        public const int HoleCount = 18;
        public const int NotPlayed = 0;

        public string Name;
        public int Index;
        public GolfBall Ball;

        public readonly int[] Strokes = new int[HoleCount];

        public int StrokesThisHole;
        public bool HoledOut;

        /// <summary>Where the ball sits between shots, used for replays after a penalty.</summary>
        public Vector3 LastSafePosition;

        public int ClubIndex = ClubSet.DriverIndex;

        public GolfPlayer(int index, string name)
        {
            Index = index;
            Name = name;
        }

        public void BeginHole(Vector3 teePosition)
        {
            StrokesThisHole = 0;
            HoledOut = false;
            LastSafePosition = teePosition;
        }

        public void CompleteHole(int holeIndex)
        {
            Strokes[holeIndex] = StrokesThisHole;
        }

        public int TotalStrokes
        {
            get
            {
                int total = 0;
                foreach (int s in Strokes)
                {
                    total += s;
                }

                return total;
            }
        }

        public int HolesPlayed
        {
            get
            {
                int played = 0;
                foreach (int s in Strokes)
                {
                    if (s != NotPlayed)
                    {
                        played++;
                    }
                }

                return played;
            }
        }

        public int RelativeToPar(CourseDefinition course)
        {
            int relative = 0;
            for (int i = 0; i < HoleCount && i < course.Holes.Count; i++)
            {
                if (Strokes[i] != NotPlayed)
                {
                    relative += Strokes[i] - course.Holes[i].Par;
                }
            }

            return relative;
        }

        public static string FormatRelative(int relative) => relative switch
        {
            0 => "E",
            > 0 => $"+{relative}",
            _ => relative.ToString()
        };

        /// <summary>Golf's name for a score, e.g. birdie or bogey.</summary>
        public static string ScoreName(int strokes, int par)
        {
            int diff = strokes - par;
            if (strokes == 1)
            {
                return "Hole in One!";
            }

            return diff switch
            {
                <= -3 => "Albatross!",
                -2 => "Eagle!",
                -1 => "Birdie",
                0 => "Par",
                1 => "Bogey",
                2 => "Double Bogey",
                3 => "Triple Bogey",
                _ => $"+{diff}"
            };
        }
    }
}
