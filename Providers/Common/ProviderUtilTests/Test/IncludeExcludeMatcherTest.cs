using NUnit.Framework;
using TickZoom.FIX;

namespace Test
{
    [TestFixture]
    public class IncludeExcludeMatcherTest
    {
        [Test]
        public void DefaultTest()
        {
            var matcher = new IncludeExcludeMatcher("*", "");
            Assert.IsTrue(matcher.Compare("Mark"));
            Assert.IsTrue(matcher.Compare("JTSORDS1"));
        }

        [Test]
        public void ExcludeAll()
        {
            var matcher = new IncludeExcludeMatcher("*", "*");
            Assert.IsFalse(matcher.Compare("Mark"));
            Assert.IsFalse(matcher.Compare("JTSORDS1"));

            matcher = new IncludeExcludeMatcher("Mar*,JTS*", "*");
            Assert.IsFalse(matcher.Compare("Mark"));
            Assert.IsFalse(matcher.Compare("JTSORDS1"));
        }

        [Test]
        public void WildCardListInclude()
        {
            var matcher = new IncludeExcludeMatcher("Mar*,JTS*", "");
            Assert.IsTrue(matcher.Compare("Mark"));
            Assert.IsTrue(matcher.Compare("JTSORDS1"));
            Assert.IsFalse(matcher.Compare("Walter"));
            Assert.IsFalse(matcher.Compare("FXORDS1"));
        }

        [Test]
        public void WildCardListExclude()
        {
            var matcher = new IncludeExcludeMatcher("*", "Mar*,JTS*");
            Assert.IsFalse(matcher.Compare("Mark"));
            Assert.IsFalse(matcher.Compare("JTSORDS1"));
            Assert.IsTrue(matcher.Compare("Walter"));
            Assert.IsTrue(matcher.Compare("FXORDS1"));
        }
    }
}