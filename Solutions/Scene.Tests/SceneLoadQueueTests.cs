using System;
using HSGFrame.Scene;
using Xunit;

namespace HSGFrame.Scene.Tests
{
    /// <summary>场景加载队列的排队、推进、进度归一化与完成事件测试。</summary>
    public class SceneLoadQueueTests
    {
        [Fact]
        public void PendingCountReflectsEnqueuedRequests()
        {
            var queue = new SceneLoadQueue();
            queue.Enqueue(new SceneLoadRequest("广场"));
            queue.Enqueue(new SceneLoadRequest("村庄"));

            Assert.Equal(2, queue.PendingCount);
        }

        [Fact]
        public void StartNextReturnsFirstEnqueuedRequest()
        {
            var queue = new SceneLoadQueue();
            var first = new SceneLoadRequest("广场");
            queue.Enqueue(first);
            queue.Enqueue(new SceneLoadRequest("村庄"));

            Assert.Same(first, queue.StartNext());
        }

        [Fact]
        public void CurrentReflectsRequestBeingLoaded()
        {
            var queue = new SceneLoadQueue();
            var request = new SceneLoadRequest("广场");
            queue.Enqueue(request);
            queue.StartNext();

            Assert.Same(request, queue.Current);
        }

        [Fact]
        public void StartNextReturnsNullWhileRequestIsLoading()
        {
            var queue = new SceneLoadQueue();
            queue.Enqueue(new SceneLoadRequest("广场"));
            queue.StartNext();

            Assert.Null(queue.StartNext());
        }

        [Fact]
        public void StartNextReturnsNullWhenQueueIsEmpty()
        {
            var queue = new SceneLoadQueue();

            Assert.Null(queue.StartNext());
        }

        [Fact]
        public void CompleteCurrentClearsCurrentAndFiresCompleted()
        {
            var queue = new SceneLoadQueue();
            var request = new SceneLoadRequest("广场");
            queue.Enqueue(request);
            queue.StartNext();
            SceneLoadRequest completed = null;
            queue.Completed += loaded => completed = loaded;

            queue.CompleteCurrent();

            Assert.Null(queue.Current);
            Assert.Same(request, completed);
        }

        [Fact]
        public void CompleteCurrentIsSilentWhenNoRequestIsLoading()
        {
            var queue = new SceneLoadQueue();

            queue.CompleteCurrent();
        }

        [Fact]
        public void ReportProgressClampsNegativeToZero()
        {
            var queue = new SceneLoadQueue();
            queue.Enqueue(new SceneLoadRequest("广场"));
            queue.StartNext();
            var progress = -1f;
            queue.ProgressChanged += value => progress = value;

            queue.ReportProgress(-1f);

            Assert.Equal(0f, progress, 3);
        }

        [Fact]
        public void ReportProgressClampsAboveOneToOne()
        {
            var queue = new SceneLoadQueue();
            queue.Enqueue(new SceneLoadRequest("广场"));
            queue.StartNext();
            var progress = -1f;
            queue.ProgressChanged += value => progress = value;

            queue.ReportProgress(2f);

            Assert.Equal(1f, progress, 3);
        }

        [Fact]
        public void NonActivatingRequestMapsZeroPointNineToOne()
        {
            var queue = new SceneLoadQueue();
            queue.Enqueue(new SceneLoadRequest("广场", SceneLoadMode.Single, activateOnLoad: false));
            queue.StartNext();
            var progress = -1f;
            queue.ProgressChanged += value => progress = value;

            queue.ReportProgress(0.9f);

            Assert.Equal(1f, progress, 3);
        }

        [Fact]
        public void NonActivatingRequestMapsZeroPointFourFiveToOneHalf()
        {
            var queue = new SceneLoadQueue();
            queue.Enqueue(new SceneLoadRequest("广场", SceneLoadMode.Single, activateOnLoad: false));
            queue.StartNext();
            var progress = -1f;
            queue.ProgressChanged += value => progress = value;

            queue.ReportProgress(0.45f);

            Assert.Equal(0.5f, progress, 3);
        }

        [Fact]
        public void ActivatingRequestKeepsZeroPointNine()
        {
            var queue = new SceneLoadQueue();
            queue.Enqueue(new SceneLoadRequest("广场"));
            queue.StartNext();
            var progress = -1f;
            queue.ProgressChanged += value => progress = value;

            queue.ReportProgress(0.9f);

            Assert.Equal(0.9f, progress, 3);
        }

        [Fact]
        public void ReportProgressFiresProgressChanged()
        {
            var queue = new SceneLoadQueue();
            queue.Enqueue(new SceneLoadRequest("广场"));
            queue.StartNext();
            var firedCount = 0;
            queue.ProgressChanged += _ => firedCount++;

            queue.ReportProgress(0.5f);

            Assert.Equal(1, firedCount);
        }

        [Fact]
        public void NullRequestThrowsArgumentException()
        {
            var queue = new SceneLoadQueue();

            var exception = Assert.Throws<ArgumentException>(() => queue.Enqueue(null));

            Assert.Contains("位置", exception.Message);
            Assert.Contains("原因", exception.Message);
            Assert.Contains("修复", exception.Message);
            Assert.Contains("参考", exception.Message);
        }

        [Fact]
        public void EmptySceneNameThrowsArgumentException()
        {
            var queue = new SceneLoadQueue();

            var exception = Assert.Throws<ArgumentException>(() => queue.Enqueue(new SceneLoadRequest("")));

            Assert.Contains("位置", exception.Message);
            Assert.Contains("原因", exception.Message);
            Assert.Contains("修复", exception.Message);
            Assert.Contains("参考", exception.Message);
        }
    }
}
