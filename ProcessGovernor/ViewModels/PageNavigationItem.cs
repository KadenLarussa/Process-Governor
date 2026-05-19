namespace ProcessGovernor.ViewModels;

public sealed class PageNavigationItem
{
    public PageNavigationItem(string title, object viewModel, string glyph)
    {
        Title = title;
        ViewModel = viewModel;
        Glyph = glyph;
    }

    public string Title { get; }

    public object ViewModel { get; }

    public string Glyph { get; }
}
