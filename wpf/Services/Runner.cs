using System;
using System.Diagnostics;
using System.Text;

namespace WinTune.Wpf
{
    /// <summary>外部进程运行器：同步捕获输出 / 异步逐行回调</summary>
    public static class Runner
    {
        public static string Run(string file, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.Default,
                    StandardErrorEncoding = Encoding.Default,
                };
                using (var p = Process.Start(psi))
                {
                    string so = p.StandardOutput.ReadToEnd();
                    string se = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return "执行超时被终止"; }
                    string all = (so + "\n" + se).Trim();
                    if (p.ExitCode == 0) return string.IsNullOrEmpty(all) ? null : all;
                    return "退出码 " + p.ExitCode + "\n" + all;
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>异步运行，stdout/stderr 逐行回调 onLine(string text, bool isError)；返回进程对象</summary>
        public static Process RunAsync(string file, string args, Action<string, bool> onLine, Action<int> onExit)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default,
            };
            try
            {
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    try { if (!string.IsNullOrEmpty(e.Data) && onLine != null) onLine(e.Data, false); }
                    catch { }
                };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    try { if (!string.IsNullOrEmpty(e.Data) && onLine != null) onLine(e.Data, true); }
                    catch { }
                };
                p.Exited += delegate(object s, EventArgs e)
                {
                    int code = -1;
                    try { code = p.ExitCode; } catch { }
                    try { if (onExit != null) onExit(code); } catch { }
                    try { p.Dispose(); } catch { }
                };
                if (!p.Start()) return null;
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                return p;
            }
            catch
            {
                return null;
            }
        }
    }
}
