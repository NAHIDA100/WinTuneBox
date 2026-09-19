using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace WinTune.Wpf
{
    /// <summary>单实例 / 免责声明 / 提权重启 —— 偏好统一走 settings.json</summary>
    public static class AppLifecycle
    {
        const string MutexName = @"Local\WinTuneBox_SingleInstance";
        static Mutex _mutex;

        /// <summary>
        /// 获取单实例锁。失败时必须释放 Mutex 句柄，否则命名对象会一直被引用，
        /// 导致后续所有实例都误判为“已在运行”（并让上一个崩溃实例留下的锁无法自动恢复）。
        /// </summary>
        public static bool AcquireSingleInstance()
        {
            if (_mutex != null) return true;
            try
            {
                _mutex = new Mutex(false, MutexName);
                try
                {
                    if (!_mutex.WaitOne(0, false))
                    {
                        _mutex.Dispose();
                        _mutex = null;
                        return false;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // 上一个实例异常退出：内核已把所有权交给本线程，直接接管
                    Logger.Log("Instance", "接管上一个未正常退出的实例锁");
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Instance", "单实例检测失败，按独立运行处理: " + ex.Message);
                try { if (_mutex != null) _mutex.Dispose(); } catch { }
                _mutex = null;
                return true;
            }
        }

        public static void Release()
        {
            try
            {
                if (_mutex == null) return;
                try { _mutex.ReleaseMutex(); } catch { }
                _mutex.Dispose();
            }
            catch { }
            finally { _mutex = null; }
        }

        public static bool Reacquire()
        {
            return AcquireSingleInstance();
        }

        public static bool DisclaimerAccepted()
        {
            return Settings.GetBool(Settings.KeyDisclaimer, false);
        }

        public static void SetDisclaimerAccepted()
        {
            Settings.SetBool(Settings.KeyDisclaimer, true);
        }

        public static void SetPref(string name, int value) { Settings.SetInt(name, value); }
        public static int? GetPref(string name)
        {
            string v = Settings.Get(name, null);
            if (v == null) return null;
            int x;
            return int.TryParse(v, out x) ? (int?)x : null;
        }

        /// <summary>以管理员身份重启（先释放单实例锁；用户取消 UAC 时恢复锁）</summary>
        public static bool RestartElevated()
        {
            try
            {
                Release();
                var psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = "--elevated",
                };
                Process.Start(psi);
                if (Application.Current != null) Application.Current.Shutdown();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Elevate", "提权重启取消或失败: " + ex.Message);
                Reacquire();
                return false;
            }
        }

        /// <summary>重启自身（非提权，例如切换主题后需要重启时使用）</summary>
        public static void RestartSelf(string args)
        {
            try
            {
                Release();
                Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName)
                {
                    UseShellExecute = true,
                    Arguments = args ?? "",
                });
                if (Application.Current != null) Application.Current.Shutdown();
            }
            catch (Exception ex) { Logger.Log("Restart", "重启失败: " + ex.Message); }
        }
    }
}