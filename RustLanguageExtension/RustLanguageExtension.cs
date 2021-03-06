﻿using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;

namespace RustLanguageExtension
{
    [Export(typeof(ILanguageClient))]
    [ContentType("rust")]
    public class RustLanguageExtension : ILanguageClient
    {
        public string Name => "Rust Language Extension";

        public IEnumerable<string> ConfigurationSections => null;

        public object InitializationOptions => null;

        public IEnumerable<string> FilesToWatch => null;

        public event AsyncEventHandler<EventArgs> StartAsync;
        public event AsyncEventHandler<EventArgs> StopAsync;

        public async Task<Connection> ActivateAsync(CancellationToken token)
        {
            var toolchain = OptionsModel.Toolchain;
            var env = await MakeEnvironment(toolchain);
            var startInfo = new ProcessStartInfo()
            {
                FileName = "rustup",
                Arguments = $"run {toolchain} rls",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var pair in env)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }

            var p = Process.Start(startInfo);
            return new Connection(p.StandardOutput.BaseStream, p.StandardInput.BaseStream);
        }

        public async System.Threading.Tasks.Task OnLoadedAsync()
        {
            var toolchain = OptionsModel.Toolchain;
            if (!await Rustup.HasToolchain(toolchain))
            {
                var infoBar = new VsUtilities.InfoBar($"configured toolchain {toolchain} is not installed", new VsUtilities.InfoBarButton("Install"));
                if (await Utilities.WaitForSingleButtonInfoBarAsync(infoBar))
                {
                    var task = Rustup.InstallToolchain(toolchain).ContinueWith(t => t.Result == 0);
                    await VsUtilities.CreateTask($"Installing {toolchain}", task);
                    if (!await task)
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            if (!await Rustup.HasComponent("rls-preview", toolchain))
            {
                if (!await InstallComponent("rls-preview", toolchain))
                {
                    return;
                }
            }

            if (!await Rustup.HasComponent("rust-analysis", toolchain))
            {
                if (!await InstallComponent("rust-analysis", toolchain))
                {
                    return;
                }
            }

            if (!await Rustup.HasComponent("rust-src", toolchain))
            {
                if (!await InstallComponent("rust-src", toolchain))
                {
                    return;
                }
            }

            if (StartAsync != null)
            {
                await StartAsync.InvokeAsync(this, EventArgs.Empty);
            }
        }

        private async Task<bool> InstallComponent(string component, string toolchain)
        {
            var infoBar = new VsUtilities.InfoBar($"component '{component}' is not installed", new VsUtilities.InfoBarButton("Install"));
            if (await Utilities.WaitForSingleButtonInfoBarAsync(infoBar))
            {
                var task = Rustup.InstallComponent(component, toolchain).ContinueWith(t => t.Result == 0);
                await VsUtilities.CreateTask($"Installing {component}", task);
                return await task;
            }
            else
            {
                return false;
            }
        }

        private async Task<IDictionary<string, string>> MakeEnvironment(string toolchain)
        {
            var newEnv = new Dictionary<string, string>();
            if (Environment.GetEnvironmentVariable("RUST_SRC_PATH") == null)
            {
                newEnv["RUST_SRC_PATH"] = await GetSysRoot(toolchain);
            }

            return newEnv;
        }

        private async Task<string> GetSysRoot(string toolchain)
        {
            var (sysRoot, _) = await Rustup.Run("rustc --print sysroot", toolchain);
            return sysRoot.Replace("\n", "").Replace("\r", "");
        }
    }
}
