namespace ChatTerror.Plugin.Gui.Tabs;

public interface ITab
{
    string Title { get; }

    void Draw();
}
