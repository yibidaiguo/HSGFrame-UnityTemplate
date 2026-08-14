using UnityEditor;
using UnityEngine;

namespace Template.Toolkit.Editor
{
    /// <summary>batchmode 下的编译校验入口：能被调用起来本身就说明工程编译通过了。</summary>
    public static class CompileCheckEntry
    {
        /// <summary>由 unity-cmd.ps1 通过 -executeMethod 调用，打印一行结论后正常退出。</summary>
        public static void Run()
        {
            Debug.Log($"[compile.check] Unity 侧编译通过，编辑器版本 {Application.unityVersion}");
            EditorApplication.Exit(0);
        }
    }
}
