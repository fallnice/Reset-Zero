#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EditorTools
{
    /// <summary>
    /// 编译检查工具（P0：AI 自检闭环 · 第 1 项）
    ///
    /// 背景：AI 盲写代码后，最快发现编译错误的方式是让 Unity 无头编译一次。
    /// 本类负责两件事：
    ///   1. 探测本机 Unity.exe 路径（公司/家中机器安装位置可能不同）
    ///   2. 生成/复制可用的无头编译命令行，供手动或脚本调用
    ///
    /// 编译检查本身不需要在 Unity 里写代码（一条命令行即可），因此本类不负责
    /// 「执行」编译，只解决「在哪找 Unity」和「命令长什么样」两个问题。
    /// 一键执行请用 Tools/run_check.ps1；本类提供编辑器菜单辅助。
    ///
    /// 手动跑编译检查：
    ///   Unity.exe -batchmode -nographics -quit -projectPath <工程> -logFile <日志>
    ///   然后读 <日志> 里的 "error CS" 行。日志末尾出现
    ///   "Exiting batchmode successfully now!" 表示整个导入+编译无致命错误。
    /// </summary>
    public static class CompileCheck
    {
        public const string CompileLogFileName = "unity_compile_check.log";
        public const string SceneAuditExecuteMethod = "EditorTools.SceneAudit.RunFromCommandLine";

        /// <summary>
        /// 探测 Unity.exe 路径，优先级：
        ///   1. 环境变量 UNITY_PATH（最可靠，机器上装好就设）
        ///   2. 项目版本号（ProjectSettings/ProjectVersion.txt）匹配常见安装目录
        ///      （Unity Hub 的 Editor/<版本>/、D:\unity\<版本>/ 等）
        ///   3. Unity Hub 目录下扫描，取版本号最大的一个
        /// 找不到返回 null。
        /// </summary>
        public static string FindUnityExePath()
        {
            string env = Environment.GetEnvironmentVariable("UNITY_PATH");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

            string version = ReadProjectVersion();
            if (!string.IsNullOrEmpty(version))
            {
                string[] roots =
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Unity", "Hub", "Editor"),
                    @"D:\unity",
                    @"D:\Program Files\Unity",
                    @"C:\Program Files\Unity",
                    @"E:\Unity",
                };
                foreach (string root in roots)
                {
                    string exe = Path.Combine(root, version, "Editor", "Unity.exe");
                    if (File.Exists(exe)) return exe;
                }
            }

            string hub = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor");
            return ScanHubForLatest(hub);
        }

        /// <summary>
        /// 组装无头模式命令行参数（-executeMethod 可选）。
        /// 返回的字符串可直接拼在 Unity.exe 后执行。
        /// </summary>
        public static string BuildArguments(string projectPath, string logPath, string executeMethod = null)
        {
            var args = new List<string>
            {
                "-batchmode", "-nographics", "-quit",
                "-projectPath", Quote(projectPath),
                "-logFile", Quote(logPath),
            };
            if (!string.IsNullOrEmpty(executeMethod))
            {
                args.Add("-executeMethod");
                args.Add(executeMethod);
            }
            return string.Join(" ", args);
        }

        // ── 编辑器菜单（辅助）──────────────────────────────────────

        [MenuItem("Tools/自检/复制编译检查命令", false, 2)]
        public static void CopyCompileCommand()
        {
            string exe = FindUnityExePath();
            string project = GetProjectPath();
            if (string.IsNullOrEmpty(exe) || project == null)
            {
                Debug.LogError("[CompileCheck] 未找到 Unity.exe，请设置环境变量 UNITY_PATH（指向 Unity.exe 全路径）后重试");
                return;
            }

            string log = Path.Combine(Path.GetTempPath(), CompileLogFileName);
            string cmd = $"\"{exe}\" {BuildArguments(project, log)}";
            EditorGUIUtility.systemCopyBuffer = cmd;
            Debug.Log($"[CompileCheck] 编译检查命令已复制到剪贴板。\n运行后编译错误见日志：{log}\n命令：{cmd}");
        }

        [MenuItem("Tools/自检/复制场景自检命令", false, 3)]
        public static void CopySceneAuditCommand()
        {
            string exe = FindUnityExePath();
            string project = GetProjectPath();
            if (string.IsNullOrEmpty(exe) || project == null)
            {
                Debug.LogError("[CompileCheck] 未找到 Unity.exe，请设置环境变量 UNITY_PATH（指向 Unity.exe 全路径）后重试");
                return;
            }

            string log = Path.Combine(Path.GetTempPath(), "unity_scene_audit_run.log");
            string cmd = $"\"{exe}\" {BuildArguments(project, log, SceneAuditExecuteMethod)}";
            EditorGUIUtility.systemCopyBuffer = cmd;
            Debug.Log($"[CompileCheck] 场景自检命令已复制到剪贴板。\n检查结果将写入：{SceneAudit.ResultLogPath}\n命令：{cmd}");
        }

        [MenuItem("Tools/自检/探测 Unity 安装路径", false, 4)]
        public static void ProbeUnityPath()
        {
            string exe = FindUnityExePath();
            Debug.Log(exe == null
                ? "[CompileCheck] 未探测到 Unity.exe，请设置环境变量 UNITY_PATH（指向 Unity.exe 全路径）"
                : $"[CompileCheck] 探测到 Unity.exe：{exe}");
        }

        // ── 内部实现 ──────────────────────────────────────────────

        /// <summary> 读取 ProjectSettings/ProjectVersion.txt 里的编辑器版本号 </summary>
        private static string ReadProjectVersion()
        {
            const string file = "ProjectSettings/ProjectVersion.txt";
            if (!File.Exists(file)) return null;

            foreach (string line in File.ReadAllLines(file))
            {
                int idx = line.IndexOf("m_EditorVersion:", StringComparison.Ordinal);
                if (idx >= 0) return line.Substring(idx + "m_EditorVersion:".Length).Trim();
            }
            return null;
        }

        /// <summary> 在 Unity Hub 安装目录中取版本号最大的 Unity.exe </summary>
        private static string ScanHubForLatest(string hubRoot)
        {
            if (!Directory.Exists(hubRoot)) return null;

            Version best = null;
            string bestPath = null;
            foreach (string dir in Directory.GetDirectories(hubRoot))
            {
                if (Version.TryParse(Path.GetFileName(dir), out Version v))
                {
                    string exe = Path.Combine(dir, "Editor", "Unity.exe");
                    if (File.Exists(exe) && (best == null || v > best))
                    {
                        best = v;
                        bestPath = exe;
                    }
                }
            }
            return bestPath;
        }

        private static string GetProjectPath()
        {
            // Application.dataPath = <工程>/Assets
            return Directory.GetParent(Application.dataPath)?.FullName;
        }

        private static string Quote(string s) => $"\"{s}\"";
    }
}
#endif
