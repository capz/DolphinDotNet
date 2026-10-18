using System;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json;

namespace DolphinDotNet.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid("27F8D063-82A9-49D1-AA4E-7FCA219BA121")]
public sealed class AttachPackage : AsyncPackage
{
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var service = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
            ?? throw new InvalidOperationException("Visual Studio's menu service is unavailable.");
        var command = new OleMenuCommand((_, __) => Attach(), new CommandID(new Guid("8DC7D557-5BBE-4D77-BF25-122185E14DD8"), 0x0100));
        command.BeforeQueryStatus += (_, __) => {
            ThreadHelper.ThrowIfNotOnUIThread();
            var service = GetService(typeof(SDTE));
            if (service is DTE2 dte)
                command.Enabled = dte.Solution.IsOpen && dte.Debugger.CurrentMode == dbgDebugMode.dbgDesignMode;
            else command.Enabled = false;
        };
        service.AddCommand(command);
    }
    private void Attach()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var dte = GetService(typeof(SDTE)) as DTE2
                ?? throw new InvalidOperationException("Visual Studio's automation service is unavailable.");
            if (string.IsNullOrEmpty(dte.Solution.FullName)) throw new InvalidOperationException("Open and save a solution before attaching.");
            var folder = Path.Combine(Path.GetDirectoryName(dte.Solution.FullName)!, ".vs", "DolphinDotNet");
            var settingsPath = Path.Combine(folder, "attach.json");
            var adapter = Path.Combine(Path.GetDirectoryName(typeof(AttachPackage).Assembly.Location)!, "adapter", "DolphinDotNet.DebugAdapter.exe");
            var settings = AttachOptions.Load(settingsPath);
            using (var dialog = new AttachDialog(settings, adapter))
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                settings = dialog.Options;
            }
            var launch = settings.CreateLaunch(adapter);
            settings.Save(settingsPath);
            // The documented host command accepts one configuration object,
            // including request=attach. This is not a .NET project launch profile.
            var launchPath = Path.Combine(folder, "launch.json");
            File.WriteAllText(launchPath, launch.ToString(Formatting.Indented));
            dte.ExecuteCommand("DebugAdapterHost.Launch", "/LaunchJson:\"" + launchPath + "\" /EngineGuid:\"{CD5691AC-E370-44AB-94CC-5940CD02A820}\"");
        }
        catch (Exception error)
        {
            VsShellUtilities.ShowMessageBox(this, error.Message, "DolphinDotNet attach", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
