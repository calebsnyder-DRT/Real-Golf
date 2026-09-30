using System.Collections.Generic;
using Puttshack.Domain;

namespace Puttshack.Content
{
    /// <summary>Content-build checks from spec section 25. Failures block HOLE_LOAD.</summary>
    public static class CourseValidator
    {
        public static List<string> Validate(CourseDefinition course)
        {
            var errors = new List<string>();
            if (course.Holes.Count == 0) errors.Add("Course has no holes.");

            var ids = new HashSet<string>();
            var numbers = new HashSet<int>();
            foreach (HoleDefinition hole in course.Holes)
            {
                if (!ids.Add(hole.Id)) errors.Add($"Duplicate hole id '{hole.Id}'.");
                if (!numbers.Add(hole.Number)) errors.Add($"Duplicate hole number {hole.Number}.");
                errors.AddRange(ValidateHole(hole));
            }
            return errors;
        }

        public static List<string> ValidateHole(HoleDefinition hole)
        {
            var errors = new List<string>();
            string tag = $"Hole {hole.Number} ({hole.Id})";

            if (hole.Par < 3 || hole.Par > 5) errors.Add($"{tag}: impossible par {hole.Par}.");
            if (!hole.TeeM.IsFinite || !hole.CupM.IsFinite) errors.Add($"{tag}: non-finite tee or cup.");
            if (hole.PlayableBoundary.Count < 3) errors.Add($"{tag}: missing playable boundary.");
            if (hole.Surfaces.Count == 0) errors.Add($"{tag}: no surface regions.");

            if (hole.PlayableBoundary.Count >= 3)
            {
                if (!Shapes.Contains(hole.PlayableBoundary, hole.TeeM.x, hole.TeeM.z)) errors.Add($"{tag}: tee outside playable boundary.");
                if (!Shapes.Contains(hole.PlayableBoundary, hole.CupM.x, hole.CupM.z)) errors.Add($"{tag}: cup outside playable boundary.");
            }

            foreach (HazardVolume hazard in hole.Hazards)
            {
                if (Shapes.Contains(hazard.Polygon, hole.TeeM.x, hole.TeeM.z)) errors.Add($"{tag}: tee inside hazard '{hazard.Id}'.");
                if (Shapes.Contains(hazard.Polygon, hole.CupM.x, hole.CupM.z)) errors.Add($"{tag}: cup inside hazard '{hazard.Id}'.");

                DropPoint drop = hole.Drops.Find(d => d.Id == hazard.DropPointId);
                if (drop == null)
                {
                    errors.Add($"{tag}: hazard '{hazard.Id}' has no relief drop.");
                    continue;
                }
                foreach (HazardVolume other in hole.Hazards)
                {
                    if (Shapes.Contains(other.Polygon, drop.Position.x, drop.Position.z))
                        errors.Add($"{tag}: drop '{drop.Id}' lies inside hazard '{other.Id}'.");
                }
                if (hole.PlayableBoundary.Count >= 3 && !Shapes.Contains(hole.PlayableBoundary, drop.Position.x, drop.Position.z))
                    errors.Add($"{tag}: drop '{drop.Id}' outside playable boundary.");
            }

            bool cupOnGreen = false;
            foreach (SurfaceRegion region in hole.Surfaces)
            {
                if (region.Polygon.Count < 3) errors.Add($"{tag}: region '{region.Id}' is degenerate.");
                if (region.Surface == SurfaceId.Green && Shapes.Contains(region.Polygon, hole.CupM.x, hole.CupM.z)) cupOnGreen = true;
            }
            if (!cupOnGreen) errors.Add($"{tag}: cup is not on a green region.");

            return errors;
        }
    }
}
