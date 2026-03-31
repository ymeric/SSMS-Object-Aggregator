namespace SSMS.ObjectAggregator.ViewModels;

public class GroupDefinitionRequestEventArgs : EventArgs
{
    public GroupDefinitionRequestEventArgs(GroupViewModel group, GroupDefinitionLaunchMode mode)
    {
        Group = group;
        Mode = mode;
    }

    public GroupViewModel Group { get; }
    public GroupDefinitionLaunchMode Mode { get; }
}

public enum GroupDefinitionLaunchMode
{
    Default,
    AddInstance
}