namespace SSMS.ObjectAggregator.ViewModels;

public class PlaceholderNodeViewModel
{
    public PlaceholderNodeViewModel(GroupViewModel parent, string message)
    {
        ParentGroup = parent;
        Message = message;
    }

    public GroupViewModel ParentGroup { get; }

    public string Message { get; }
}