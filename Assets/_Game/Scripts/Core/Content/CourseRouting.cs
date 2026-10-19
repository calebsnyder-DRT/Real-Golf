using System;
using System.Collections.Generic;
using Puttshack.Storage;

namespace Puttshack.Content
{
    /// <summary>
    /// The round's routing: which authored layout every hole number plays (course.json "Routing"). Only some Pebble Beach
    /// holes are authored (<see cref="GrayboxCourse"/>), so every other number replays one of them as a stand-in, picked so
    /// consecutive holes follow the cart path network and the between-hole drive always has a route. Authoring a real hole
    /// means adding its file to course.json "Holes" (so it bakes) and naming it in "Routing" at its number.
    /// </summary>
    public static class CourseRouting
    {
        public const int FullRound = 18;

        /// <summary>Layout id played at each hole number (index = number - 1).</summary>
        public static IReadOnlyList<string> Layouts => CourseData.Load().Routing;

        /// <summary>
        /// The first <paramref name="holes"/> numbers of the routing as a playable course. A number whose layout is the
        /// authored hole of that number plays it unchanged; any other plays a copy renumbered and re-identified (validation
        /// needs unique ids and numbers) that keeps the layout's collision and points <see cref="HoleDefinition.LayoutId"/>
        /// at it for the baked visuals.
        /// </summary>
        public static CourseDefinition CreateRound(CourseDefinition authored, int holes = FullRound)
        {
            if (authored == null) throw new ArgumentNullException(nameof(authored));
            IReadOnlyList<string> layouts = Layouts;
            int count = Math.Max(1, Math.Min(holes, layouts.Count));
            var round = new CourseDefinition
            {
                Id = $"{authored.Id}-{count}",
                Version = $"{authored.Version}+routing-1",
                Name = $"{authored.Name} - {count} holes",
            };
            for (int i = 0; i < count; i++)
            {
                int number = i + 1;
                HoleDefinition layout = authored.Holes.Find(h => h.Id == layouts[i]);
                if (layout == null) throw new InvalidOperationException($"Routing hole {number} names unknown layout '{layouts[i]}'.");
                round.Holes.Add(layout.Number == number ? layout : StandIn(layout, number));
            }
            ContentHasher.Seal(round);
            return round;
        }

        /// <summary>True when the hole replays another hole's layout rather than its own.</summary>
        public static bool IsStandIn(HoleDefinition hole) => hole?.LayoutId != null;

        private static HoleDefinition StandIn(HoleDefinition layout, int number)
        {
            HoleDefinition copy = JournalSerializer.Clone(layout);
            copy.Id = $"h{number:00}~{layout.Id}";
            copy.Number = number;
            copy.LayoutId = layout.Id;
            return copy;
        }
    }
}
