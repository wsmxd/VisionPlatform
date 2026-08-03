namespace VisionPlatform.ViewModels;

/// <summary>左侧导航项。</summary>
public class NavItem
{
    public required string Icon { get; init; }
    public required string Title { get; init; }
    public required object ViewModel { get; init; }
}
