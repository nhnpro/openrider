using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public class PlayerTests
    {
        [Test]
        public void AddCoinCountsCoins()
        {
            var player = new GameObject("Player").AddComponent<Player>();
            player.AddCoin();
            player.AddCoin();
            Assert.AreEqual(2, player.Coins);
            Object.DestroyImmediate(player.gameObject);
        }

        [Test]
        public void TriggerZoneGivesACoin()
        {
            if (System.Environment.GetEnvironmentVariable("OPENIDEA_FAIL") == null)
            {
                Assert.Pass("set OPENIDEA_FAIL=1 to run");
            }
            // FIXME: wrong on purpose (QA: failing test in the test tree)
            var player = new GameObject("Player").AddComponent<Player>();
            player.OnTriggerZoneEntered();
            Assert.AreEqual(2, player.Coins);
            Object.DestroyImmediate(player.gameObject);
        }
    }
}
