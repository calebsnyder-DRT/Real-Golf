using System.Collections.Generic;
using NUnit.Framework;
using Puttshack.EditorTools;

namespace Puttshack.Tests
{
    /// <summary>
    /// The baked golfers (Puttshack > Characters > Bake Golfers): Caleb and Maya each carry every cosmetic slot on a valid
    /// Humanoid, every controller state plays a retargeted Mixamo clip, and each club has its impact time and ball spot.
    /// </summary>
    public sealed class GolferCharacterTests
    {
        [Test]
        public void BakedGolfers_PassVerification()
        {
            List<string> problems = GolferCharacterBaker.Verify();
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }
    }
}
