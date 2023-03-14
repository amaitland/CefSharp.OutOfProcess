﻿using CefSharp.OutOfProcess.Interface;
using CefSharp.OutOfProcess.Internal;
using PInvoke;
using StreamJsonRpc;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CefSharp.OutOfProcess
{
    public class OutOfProcessHost : IOutOfProcessHostRpc, IDisposable
    {
        /// <summary>
        /// The CefSharp.OutOfProcess.BrowserProcess.exe name
        /// </summary>
        public const string HostExeName = "CefSharp.OutOfProcess.BrowserProcess.exe";

        private Process _browserProcess;
        private JsonRpc _jsonRpc;
        private IOutOfProcessClientRpc _outOfProcessClient;
        private string _cefSharpVersion;
        private string _cefVersion;
        private string _chromiumVersion;
        private int _uiThreadId;
        private int _remoteuiThreadId;
        private int _browserIdentifier = 1;
        private string _outofProcessHostExePath;
        private Settings _settings;
        private ConcurrentDictionary<int, IChromiumWebBrowserInternal> _browsers = new ConcurrentDictionary<int, IChromiumWebBrowserInternal>();
        private TaskCompletionSource<OutOfProcessHost> _processInitialized = new TaskCompletionSource<OutOfProcessHost>(TaskCreationOptions.RunContinuationsAsynchronously);

        private OutOfProcessHost(string outOfProcessHostExePath, Settings settings = null)
        {
            _outofProcessHostExePath = outOfProcessHostExePath;
            _settings = settings;
        }

        /// <summary>
        /// UI Thread assocuated with this <see cref="OutOfProcessHost"/>
        /// </summary>
        public int UiThreadId
        {
            get { return _uiThreadId; }
        }

        /// <summary>
        /// Thread Id of the UI Thread running in the Browser Process
        /// </summary>
        public int RemoteUiThreadId
        {
            get { return _remoteuiThreadId; }
        }

        /// <summary>
        /// CefSharp Version
        /// </summary>
        public string CefSharpVersion
        {
            get { return _cefSharpVersion; }
        }

        /// <summary>
        /// Cef Version
        /// </summary>
        public string CefVersion
        {
            get { return _cefVersion; }
        }

        /// <summary>
        /// Chromium Version
        /// </summary>
        public string ChromiumVersion
        {
            get { return _chromiumVersion; }
        }

        /// <summary>
        /// Sends an IPC message to the Browser Process instructing it
        /// to create a new Out of process browser
        /// </summary>
        /// <param name="browser">The <see cref="IChromiumWebBrowserInternal"/> that will host the browser</param>
        /// <param name="handle">handle used to host the control</param>
        /// <param name="url"></param>
        /// <param name="id"></param>
        /// <param name="requestContextPreferences">request context preference.</param>
        /// <returns></returns>
        public bool CreateBrowser(IChromiumWebBrowserInternal browser, IntPtr handle, string url, out int id, IDictionary<string, object> requestContextPreferences = null)
        {
            id = _browserIdentifier++;
            _ = _outOfProcessClient.CreateBrowser(handle, url, id, requestContextPreferences);

            return _browsers.TryAdd(id, browser);
        }

        internal Task SendDevToolsMessageAsync(int browserId, string message)
        {
            return _outOfProcessClient.SendDevToolsMessage(browserId, message);
        }

        private Task<OutOfProcessHost> InitializedTask
        {
            get { return _processInitialized.Task; }
        }

        private void Init()
        {
            var currentProcess = Process.GetCurrentProcess();

            var args = $"--parentProcessId={currentProcess.Id}";

            if (_settings != null)
            {
                if (!string.IsNullOrEmpty(_settings.CachePath))
                {
                    args += $" --cachePath={_settings.CachePath}";
                }

                if (!string.IsNullOrEmpty(_settings.RootCachePath))
                {
                    args += $" --rootCachePath={_settings.RootCachePath}";
                }

                args = _settings.AdditionalCommandLineArgs.Aggregate(args, (current, next) => $"{current} {next}");
            }

            _browserProcess = Process.Start(new ProcessStartInfo(_outofProcessHostExePath, args)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            });

            _browserProcess.Exited += OnBrowserProcessExited;

            _jsonRpc = JsonRpc.Attach(_browserProcess.StandardInput.BaseStream, _browserProcess.StandardOutput.BaseStream);

            _outOfProcessClient = _jsonRpc.Attach<IOutOfProcessClientRpc>();
            _jsonRpc.AllowModificationWhileListening = true;
            _jsonRpc.AddLocalRpcTarget<IOutOfProcessHostRpc>(this, null);
            _jsonRpc.AllowModificationWhileListening = false;

            _uiThreadId = Kernel32.GetCurrentThreadId();
        }

        private void OnBrowserProcessExited(object sender, EventArgs e)
        {
            var exitCode = _browserProcess.ExitCode;
        }

        void IOutOfProcessHostRpc.NotifyAddressChanged(int browserId, string address)
        {
            if (_browsers.TryGetValue(browserId, out var chromiumWebBrowser))
            {
                chromiumWebBrowser.SetAddress(address);
            }
        }

        void IOutOfProcessHostRpc.NotifyBrowserCreated(int browserId, IntPtr browserHwnd)
        {
            if (_browsers.TryGetValue(browserId, out var chromiumWebBrowser))
            {
                chromiumWebBrowser.OnAfterBrowserCreated(browserHwnd);
            }
        }

        void IOutOfProcessHostRpc.NotifyContextInitialized(int threadId, string cefSharpVersion, string cefVersion, string chromiumVersion)
        {
            _remoteuiThreadId = threadId;
            _cefSharpVersion = cefSharpVersion;
            _cefVersion = cefVersion;
            _chromiumVersion = chromiumVersion;

            _processInitialized.TrySetResult(this);
        }

        void IOutOfProcessHostRpc.NotifyDevToolsAgentDetached(int browserId)
        {
            if (_browsers.TryGetValue(browserId, out var chromiumWebBrowser))
            {

            }
        }

        void IOutOfProcessHostRpc.NotifyDevToolsMessage(int browserId, string devToolsMessage)
        {
            if (_browsers.TryGetValue(browserId, out var chromiumWebBrowser))
            {
                chromiumWebBrowser.OnDevToolsMessage(devToolsMessage);
            }
        }

        void IOutOfProcessHostRpc.NotifyDevToolsReady(int browserId)
        {
            if (_browsers.TryGetValue(browserId, out var chromiumWebBrowser))
            {
                chromiumWebBrowser.OnDevToolsReady();
            }
        }

        void IOutOfProcessHostRpc.NotifyLoadingStateChange(int browserId, bool canGoBack, bool canGoForward, bool isLoading)
        {
            if (_browsers.TryGetValue(browserId, out var chromiumWebBrowser))
            {
                chromiumWebBrowser.SetLoadingStateChange(canGoBack, canGoForward, isLoading);
            }
        }

        void IOutOfProcessHostRpc.NotifyStatusMessage(int browserId, string statusMessage)
        {
            if (_browsers.TryGetValue(browserId, out var chromiumWebBrowser))
            {
                chromiumWebBrowser.SetStatusMessage(statusMessage);
            }
        }

        void IOutOfProcessHostRpc.NotifyTitleChanged(int browserId, string title)
        {
            if (_browsers.TryGetValue(browserId, out var chromiumWebBrowser))
            {
                chromiumWebBrowser.SetTitle(title);
            }
        }

        public void NotifyMoveOrResizeStarted(int id)
        {
            _outOfProcessClient.NotifyMoveOrResizeStarted(id);
        }

        /// <summary>
        /// Set whether the browser is focused. (Used for Normal Rendering e.g. WinForms)
        /// </summary>
        /// <param name="id">browser id</param>
        /// <param name="focus">set focus</param>
        public void SetFocus(int id, bool focus)
        {
            _outOfProcessClient.SetFocus(id, focus);
        }

        public void CloseBrowser(int id)
        {
            _ = _outOfProcessClient.CloseBrowser(id);
        }

        public void Dispose()
        {
            _ = _outOfProcessClient.CloseHost();
            _jsonRpc?.Dispose();
            _jsonRpc = null;
        }

        public static Task<OutOfProcessHost> CreateAsync(string path = HostExeName, Settings settings = null)
        {
            if(string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path));
            }

            var fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Unable to find Host executable.", path);
            }

            var host = new OutOfProcessHost(fullPath, settings);

            host.Init();            

            return host.InitializedTask;
        }

        /// <summary>
        /// Set Request Context Preferences of the browser.
        /// </summary>
        /// <param name="browserId">The browser id.</param>
        /// <param name="preferences">The preferences.</param>
        public void SetRequestContextPreferences(int browserId, IDictionary<string, object> preferences)
        {
            _outOfProcessClient.SetRequestContextPreferences(browserId, preferences);
        }

        /// <summary>
        /// Set Global Request Context Preferences for all browsers.
        /// </summary>
        /// <param name="preferences">The preferences.</param>
        public void SetGlobalRequestContextPreferences(IDictionary<string, object> preferences)
        {
            _outOfProcessClient.SetGlobalRequestContextPreferences(preferences);
        }
    }
}
