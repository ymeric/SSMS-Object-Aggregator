using Microsoft.VisualStudio.Shell;
using SSMS.ObjectAggregator.Infrastructure;
using SSMS.ObjectAggregator.Views;
using System;
using System.Runtime.InteropServices;

namespace SSMS.ObjectAggregator.Vsix.ToolWindows;

[Guid(ToolWindowGuidString)]
public sealed class SmartObjectFilterToolWindow : ToolWindowPane
{
    public const string ToolWindowGuidString = "B3A8E1C0-4F72-4D6B-9A1E-2C5F8D3B7A0E";

    public SmartObjectFilterToolWindow() : base(null)
    {
        Caption = "Object Aggregator";
        AggregatorServiceProvider.EnsureInitialized(() => new DefaultAggregatorServices());
        Content = new SmartObjectFilterWindow();
    }
}
