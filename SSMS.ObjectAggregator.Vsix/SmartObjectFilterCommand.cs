using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SSMS.ObjectAggregator.Infrastructure;
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;

namespace SSMS.ObjectAggregator.Vsix;

internal sealed class SmartObjectFilterCommand
{
    public const int CommandId = 0x0100;
    public static readonly Guid CommandSet = new("F9C8B4AC-9C5A-42A4-8A0E-000000000001");
    private readonly SmartObjectFilterPackage _package;

    private SmartObjectFilterCommand(SmartObjectFilterPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        var cmdId = new CommandID(CommandSet, CommandId);
        var menuItem = new OleMenuCommand((_, _) =>
        {
#pragma warning disable VSSDK007 // FileAndForget is the recommended fire-and-forget pattern
            ThreadHelper.JoinableTaskFactory.RunAsync(ExecuteWithErrorHandlingAsync).FileAndForget("SSMS.ObjectAggregator/SmartObjectFilter");
#pragma warning restore VSSDK007
        }, cmdId);
        commandService.AddCommand(menuItem);
    }

    public static async Task InitializeAsync(SmartObjectFilterPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        AggregatorServiceProvider.InitializeShell(package);
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
                             ?? throw new InvalidOperationException("Unable to acquire OleMenuCommandService.");
        _ = new SmartObjectFilterCommand(package, commandService);
    }

    private async Task ExecuteAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
        await _package.ShowObjectAggregatorWindowAsync();
    }

    private async Task ExecuteWithErrorHandlingAsync()
    {
        try
        {
            await ExecuteAsync();
        }
        catch (Exception ex)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);
            VsShellUtilities.ShowMessageBox(
                _package,
                ex.Message,
                "Object Aggregator",
                OLEMSGICON.OLEMSGICON_CRITICAL,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}