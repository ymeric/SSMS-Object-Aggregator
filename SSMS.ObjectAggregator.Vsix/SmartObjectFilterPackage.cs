#nullable enable
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using SSMS.ObjectAggregator.Vsix.ToolWindows;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SSMS.ObjectAggregator.Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("SSMS Object Aggregator", "Smart Object Filter window", "1.0")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideToolWindow(typeof(SmartObjectFilterToolWindow), Style = VsDockStyle.Tabbed, Orientation = ToolWindowOrientation.Right)]
[Guid(PackageGuidString)]
public sealed class SmartObjectFilterPackage : AsyncPackage
{
    public const string PackageGuidString = "F9C8B4AC-9C5A-42A4-8A0E-099999999999";

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        await SmartObjectFilterCommand.InitializeAsync(this);
    }

    public Task<ToolWindowPane?> ShowObjectAggregatorWindowAsync()
        => ShowToolWindowAsync(typeof(SmartObjectFilterToolWindow), 0, true, DisposalToken);
}