using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine.LowLevel;
using Yoji.EditorCore;

namespace Yoji.EditorDebug.Ui
{
    // Screen and GraphicRaycaster must see the same GameView context as the player.
    internal static class UiFrameDispatcher
    {
        private sealed class Request
        {
            public Func<object> Work;
            public readonly TaskCompletionSource<object> Completion = new TaskCompletionSource<object>();
        }

        private static readonly Queue<Request> s_Pending = new Queue<Request>();

        public static object Run(Func<object> work, out long elapsedMs)
        {
            var timer = Stopwatch.StartNew();
            var request = new Request { Work = work };
            MainThreadDispatcher.Run(() =>
            {
                if (!EditorApplication.isPlaying)
                    Complete(request);
                else if (EditorApplication.isPaused)
                    request.Completion.TrySetException(new InvalidOperationException("PlayMode UI observation and act require an advancing frame; resume the Editor"));
                else
                {
                    Install();
                    s_Pending.Enqueue(request);
                    EditorApplication.QueuePlayerLoopUpdate();
                }
                return null;
            }, out _);
            if (!request.Completion.Task.Wait(TimeSpan.FromSeconds(10)))
            {
                request.Completion.TrySetCanceled();
                throw new TimeoutException("PlayMode did not produce a UI frame within 10 seconds");
            }
            elapsedMs = timer.ElapsedMilliseconds;
            return request.Completion.Task.GetAwaiter().GetResult();
        }

        private static void Install()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var children = new List<PlayerLoopSystem>(loop.subSystemList);
            children.RemoveAll(system => system.type == typeof(UiFrameDispatcher));
            children.Add(new PlayerLoopSystem { type = typeof(UiFrameDispatcher), updateDelegate = Pump });
            loop.subSystemList = children.ToArray();
            PlayerLoop.SetPlayerLoop(loop);
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
        }

        private static void Pump()
        {
            while (s_Pending.Count != 0)
                Complete(s_Pending.Dequeue());
        }

        private static void Complete(Request request)
        {
            if (request.Completion.Task.IsCompleted) return;
            try { request.Completion.TrySetResult(request.Work()); }
            catch (Exception error) { request.Completion.TrySetException(error); }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) Cancel();
        }

        private static void Cancel()
        {
            while (s_Pending.Count != 0)
                s_Pending.Dequeue().Completion.TrySetCanceled();
        }
    }
}
