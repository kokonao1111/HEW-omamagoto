using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;

namespace Omamagoto.Tests
{
    /// <summary>テストで「〜になるまで待つ」ための道具</summary>
    public static class Wait
    {
        /// <summary>
        /// condition が true になるまで、1 フレームずつ待つ。
        /// timeoutSeconds（実際の秒）を過ぎたら、what を出してテストを失敗にする。
        /// </summary>
        public static IEnumerator Until(Func<bool> condition, float timeoutSeconds, string what)
        {
            float giveUpTime = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > giveUpTime)
                {
                    Assert.Fail($"{timeoutSeconds} 秒待っても、{what}");
                }
                yield return null;
            }
        }

        /// <summary>実際の秒で待つ（Time.timeScale に関係なく）</summary>
        public static IEnumerator RealSeconds(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
        }
    }
}
