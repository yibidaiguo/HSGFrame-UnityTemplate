using System.Collections;
using HSGFrame.Scene;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Template.Presentation.Framework
{
    /// <summary>场景加载壳：用引擎的异步加载驱动纯 C# 的加载队列，进度归一化与排队规则都在队列那一侧。</summary>
    public static class SceneLoadDriver
    {
        /// <summary>本驱动使用的加载队列，排队与订阅进度都用它。</summary>
        public static SceneLoadQueue Queue { get; } = new SceneLoadQueue();

        /// <summary>取出队列里的下一个请求并真正加载它，返回可等待的协程枚举器。</summary>
        public static IEnumerator LoadNext()
        {
            var request = Queue.StartNext();
            if (request == null)
            {
                yield break;
            }

            var mode = request.Mode == SceneLoadMode.Additive ? LoadSceneMode.Additive : LoadSceneMode.Single;
            var operation = SceneManager.LoadSceneAsync(request.SceneName, mode);
            if (operation == null)
            {
                // 场景没进 Build Settings 时引擎返回 null。当场把这一单结掉，
                // 否则队列会一直卡在这个请求上，后面排队的永远轮不到。
                Queue.CompleteCurrent();
                yield break;
            }

            operation.allowSceneActivation = request.ActivateOnLoad;

            while (!operation.isDone)
            {
                Queue.ReportProgress(operation.progress);

                // 不允许激活时引擎的进度封顶在 0.9 且 isDone 永远为 false，
                // 这里到顶就交给调用方决定何时激活，避免死等。
                if (!request.ActivateOnLoad && operation.progress >= 0.9f)
                {
                    yield break;
                }

                yield return null;
            }

            Queue.ReportProgress(1f);
            Queue.CompleteCurrent();
        }
    }
}
