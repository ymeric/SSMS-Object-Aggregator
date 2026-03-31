using System.Windows.Input;

namespace SSMS.ObjectAggregator.ViewModels;

public class DefinitionActionDescriptor
{
    public DefinitionActionDescriptor(string header, string iconGlyph, ICommand command)
    {
        Header = header;
        IconGlyph = iconGlyph;
        Command = command;
    }

    public string Header { get; }
    public string IconGlyph { get; }
    public ICommand Command { get; }
}