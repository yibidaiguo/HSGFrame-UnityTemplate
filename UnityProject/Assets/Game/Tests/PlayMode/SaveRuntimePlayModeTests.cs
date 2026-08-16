using System.Collections;
using NUnit.Framework;
using Template.Presentation.BuildVerification;
using UnityEngine.TestTools;

namespace Template.Tests.PlayMode
{
    /// <summary>
    /// 存档在 Unity 运行期的序列化测试。跑的是出包验收用的同一个探针——
    /// 编辑器这一遍先把逻辑本身钉住，真包那一遍（IL2CPP + 裁剪）才只剩后端差异这一个变量。
    /// </summary>
    public sealed class SaveRuntimePlayModeTests
    {
        /// <summary>存档往返与迁移链在真运行期跑通，落盘原文里的中文没有被转义。</summary>
        [UnityTest]
        public IEnumerator SaveRoundTripAndMigrationPassInPlayMode()
        {
            // 探针自己写盘再读回，隔一帧只是为了确保是在真正的运行期而不是进入运行期的那一刻跑。
            yield return null;

            var conclusion = SaveVerification.ProbeRoundTrip();

            StringAssert.StartsWith("通过：", conclusion, conclusion);
        }
    }
}
