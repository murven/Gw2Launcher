﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Gw2Launcher.Client
{
    static partial class Launcher
    {
        private class LinkedProcess : IDisposable
        {
            public static event EventHandler<Account> ProcessExited;
            public static event EventHandler<Account> ProcessActive;

            public event EventHandler<Account> Exited;
            public event EventHandler<Process> Changed;

            private static Dictionary<int, LinkedProcess> activeProcesses = new Dictionary<int, LinkedProcess>();

            private class ProcessWatcher : IDisposable
            {
                public event EventHandler Exited;

                private Process process;
                private Task waiter;

                public ProcessWatcher(Process p)
                {
                    this.process = p;
                    this.waiter = new Task(WaitForExit, TaskCreationOptions.LongRunning);
                    this.waiter.Start();
                }

                private void WaitForExit()
                {
                    this.process.WaitForExit();

                    if (Exited != null)
                        Exited(this.process, null);
                }

                public void Dispose()
                {
                    if (waiter != null)
                    {
                        if (waiter.IsCompleted)
                            waiter.Dispose();
                        waiter = null;
                    }
                }
            }

            private Account account;
            private ProcessWatcher watcher;

            public static LinkedProcess[] GetActive()
            {
                lock (activeProcesses)
                {
                    LinkedProcess[] active = new LinkedProcess[activeProcesses.Count];
                    int i = 0;

                    foreach (LinkedProcess p in activeProcesses.Values)
                    {
                        active[i++] = p;
                    }

                    return active;
                }
            }

            public static int GetActiveCount()
            {
                lock (activeProcesses)
                {
                    return activeProcesses.Count;
                }
            }

            public static Account GetAccount(Process p)
            {
                LinkedProcess l;
                if (activeProcesses.TryGetValue(p.Id, out l))
                {
                    return l.account;
                }
                return null;
            }

            public static bool Contains(Process p)
            {
                return activeProcesses.ContainsKey(p.Id);
            }

            public LinkedProcess(Account account)
            {
                this.account = account;
            }

            public Process Launch(ProcessStartInfo startInfo)
            {
                Process p = new Process();
                p.StartInfo = startInfo;

                Attach(p);

                if (p.Start())
                {
                    lock (activeProcesses)
                    {
                        if (!p.HasExited)
                        {
                            activeProcesses.Add(p.Id, this);

                            if (ProcessActive != null)
                                ProcessActive(p, this.account);
                        }
                    }
                }

                return p;
            }

            public void Attach(Process p)
            {
                lock (this)
                {
                    _Attach(p);
                }
            }

            private void _Attach(Process p)
            {
                if (this.Process != null)
                {
                    try
                    {
                        int pid = this.Process.Id;
                        lock (activeProcesses)
                        {
                            activeProcesses.Remove(pid);
                        }
                    }
                    catch (Exception e)
                    {
                        Util.Logging.Log(e);
                    }

                    if (watcher != null)
                    {
                        watcher.Dispose();
                        watcher = null;
                    }
                    else
                    {
                        this.Process.Exited -= p_Exited;
                    }
                }

                bool hasExited = false;
                bool hasStarted = false;

                try
                {
                    hasStarted = p.Id > 0;
                    hasExited = p.HasExited;
                }
                catch (Exception ex)
                {
                    Util.Logging.Log(ex);
                }

                this.Process = p;

                if (!hasExited)
                {
                    bool supportsEvents = false;

                    if (!p.EnableRaisingEvents)
                    {
                        //try setting the value as the current user, then the user running the process
                        //the current user will be able to set another user's process if this process is still the owner (ownership is lost when this process is closed)
                        try
                        {
                            p.EnableRaisingEvents = true;
                            supportsEvents = true;
                        }
                        catch  (Exception e)
                        {
                            Util.Logging.Log(e);

                            if (!Util.Users.IsCurrentUser(account.Settings.WindowsAccount))
                            {
                                string username = Util.Users.GetUserName(account.Settings.WindowsAccount);

                                try
                                {
                                    using (Security.Impersonation.Impersonate(username, Security.Credentials.GetPassword(username)))
                                    {
                                        p.EnableRaisingEvents = true;
                                        supportsEvents = true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Util.Logging.Log(ex);
                                }
                            }
                        }
                    }
                    else
                        supportsEvents = true;

                    if (supportsEvents)
                    {
                        p.Exited += p_Exited;
                    }
                    else
                    {
                        watcher = new ProcessWatcher(p);
                        watcher.Exited += p_Exited;
                    }

                    if (hasStarted && p.HasExited)
                    {
                        this.HasMutex = false;
                    }
                    else
                    {
                        this.HasMutex = true;
                    }
                }
                else
                {
                    this.HasMutex = false;

                    p_Exited(p, null);
                }

                if (hasStarted)
                {
                    lock (activeProcesses)
                    {
                        if (!p.HasExited)
                        {
                            activeProcesses.Add(p.Id, this);

                            if (ProcessActive != null)
                                ProcessActive(p, this.account);
                        }
                    }
                }
            }

            void p_Exited(object sender, EventArgs e)
            {
                Process p = sender as Process;

                var pid = p.Id;

                lock (activeProcesses)
                {
                    activeProcesses.Remove(pid);
                }

                bool b;

                lock (this)
                {
                    if (b = p == this.Process)
                        this.Process = null;
                }

                if (b)
                {
                    //using (var pi = new Windows.ProcessInfo())
                    //{
                    //    foreach (var process in Process.GetProcesses())
                    //    {
                    //        var dispose = true;
                    //        if (b)
                    //        {
                    //            try
                    //            {
                    //                if (pi.Open(process.Id))
                    //                {
                    //                    if (pi.GetParent() == pid)
                    //                    {
                    //                        b = false;
                    //                        dispose = false;

                    //                        Attach(process);
                    //                    }
                    //                }
                    //            }
                    //            catch (Exception ex)
                    //            {
                    //                Util.Logging.Log(ex);
                    //            }
                    //        }
                    //        if (dispose)
                    //            process.Dispose();
                    //    }
                    //}

                    if (b)
                    {
                        if (Exited != null)
                            Exited(this, this.account);

                        if (ProcessExited != null)
                            ProcessExited(p, this.account);
                    }
                }
            }

            private Process _process;
            public Process Process
            {
                get
                {
                    return _process;
                }
                private set
                {
                    if (_process != value)
                    {
                        _process = value;
                        if (Changed != null)
                            Changed(this, value);
                    }
                }
            }

            public bool HasMutex
            {
                get;
                private set;
            }

            public Account Account
            {
                get
                {
                    return account;
                }
            }

            public bool KillMutex()
            {
                if (!Util.Users.IsCurrentUser(account.Settings.WindowsAccount))
                {
                    string username = Util.Users.GetUserName(account.Settings.WindowsAccount);
                    System.Security.SecureString password = Security.Credentials.GetPassword(username);
                    try
                    {
                        using (Security.Impersonation.Impersonate(username, password))
                        {
                            if (!_KillMutex())
                                return false;
                        }
                    }
                    catch (Exception e)
                    {
                        Util.Logging.Log(e);
                        Util.ProcessUtil.KillMutexWindow(this.Process.Id, username, password);
                    }
                }
                else if (!_KillMutex())
                {
                    return false;
                }

                this.HasMutex = false;

                return true;
            }

            private bool _KillMutex()
            {
                var p = this.Process;
                var hasWindow = false;

                do
                {
                    var handle = Windows.Win32Handles.GetHandle(p.Id, "AN-Mutex-Window-Guild Wars 2", false);

                    if (handle != null)
                    {
                        handle.Kill();
                        return true;
                    }
                    else if (hasWindow)
                    {
                        return true;
                    }

                    try
                    {
                        var limit = DateTime.UtcNow.AddSeconds(30);

                        do
                        {
                            var h = p.MainWindowHandle;
                            if (h != IntPtr.Zero)
                            {
                                hasWindow = true;
                                break;
                            }
                            else if (DateTime.UtcNow > limit)
                            {
                                return false;
                            }
                        }
                        while (!p.WaitForExit(500));
                    }
                    catch { }
                }
                while (true);
            }

            public void Dispose()
            {
                if (watcher != null)
                {
                    watcher.Dispose();
                    watcher = null;
                }
            }
        }
    }
}
