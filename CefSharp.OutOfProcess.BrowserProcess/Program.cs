﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CefSharp.Internals;

namespace CefSharp.OutOfProcess.BrowserProcess
{
    public class Program
    {
        public static int Main(string[] args)
        {
            Cef.EnableHighDPISupport();

            Debugger.Launch();

            var parentProcessId = int.Parse(CommandLineArgsParser.GetArgumentValue(args, "--parentProcessId"));
            var hostHwnd = int.Parse(CommandLineArgsParser.GetArgumentValue(args, "--hostHwnd"));

            var parentProcess = Process.GetProcessById(parentProcessId);

            var settings = new CefSettings()
            {
                //By default CefSharp will use an in-memory cache, you need to specify a Cache Folder to persist data
                CachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CefSharp\\OutOfProcessCache"),
                MultiThreadedMessageLoop = false
            };

            var browserProcessHandler = new BrowserProcessHandler(parentProcessId, new IntPtr(hostHwnd));

            Cef.EnableWaitForBrowsersToClose();

            Cef.Initialize(settings, performDependencyCheck:true, browserProcessHandler: browserProcessHandler);

            Task.Run(() =>
            {
                parentProcess.WaitForExit();

                CefThread.ExecuteOnUiThread(() =>
                {
                    Cef.QuitMessageLoop();

                    return true;
                });
            });

            Cef.RunMessageLoop();            

            Cef.WaitForBrowsersToClose();

            Cef.Shutdown();

            return 0;
        }
    }
}
