using System.Windows.Forms;
using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

// The host paints ListView rows itself; overflow used to clip silently at
// the visible capacity (a 116px list showed 4 rows, the 5th+ item existed in
// Items but was unreachable: no scrollbar, wheel did nothing). The host now
// owns a per-ListView top index, paints windowed rows + scrollbar, and
// scrolls on wheel/track clicks. These pin the scroll math; the browser
// gallery case (case_todo_scroll) proves end-to-end reveal.
public class ListViewScrollTests
{
    [Fact]
    public void CapacityMatchesLegacyClip()
    {
        // 116px list showed exactly 4 rows before scrolling existed.
        Assert.Equal(4, WinFormsAppHost.ListViewCapacity(116));
        Assert.Equal(4, WinFormsAppHost.ListViewCapacity(127));
        Assert.Equal(1, WinFormsAppHost.ListViewCapacity(24));
        Assert.Equal(1, WinFormsAppHost.ListViewCapacity(0));
    }

    [Fact]
    public void ClampTopKeepsWindowInsideItems()
    {
        Assert.Equal(0, WinFormsAppHost.ListViewClampTop(0, 6, 4));
        Assert.Equal(2, WinFormsAppHost.ListViewClampTop(9, 6, 4));
        Assert.Equal(0, WinFormsAppHost.ListViewClampTop(-3, 6, 4));
        Assert.Equal(0, WinFormsAppHost.ListViewClampTop(0, 3, 4));
        Assert.Equal(0, WinFormsAppHost.ListViewClampTop(5, 0, 4));
    }

    private static ListView SixRows()
    {
        var list = new ListView { Height = 116 };
        for (int i = 0; i < 6; i++)
            list.Items.Add($"row-{i}");
        return list;
    }

    [Fact]
    public void WheelScrollPersistsAcrossResolves()
    {
        var host = new WinFormsAppHost();
        using ListView list = SixRows();
        Assert.True(host.ScrollListView(list, 2));
        // Repaints resolve the stored top; wheel position must not snap back.
        Assert.Equal(2, host.ResolveListViewTop(list));
        Assert.Equal(2, host.ResolveListViewTop(list));
    }

    [Fact]
    public void ScrollStopsAtEdges()
    {
        var host = new WinFormsAppHost();
        using ListView list = SixRows();
        Assert.False(host.ScrollListView(list, -1));
        Assert.True(host.ScrollListView(list, 99));
        Assert.Equal(2, host.ResolveListViewTop(list));
        Assert.False(host.ScrollListView(list, 1));
        Assert.False(host.ScrollListView(list, 0));
    }

    [Fact]
    public void NoScrollWithoutOverflow()
    {
        var host = new WinFormsAppHost();
        using var list = new ListView { Height = 116 };
        list.Items.Add("a");
        Assert.False(host.ScrollListView(list, 3));
        Assert.Equal(0, host.ResolveListViewTop(list));
    }

    [Fact]
    public void NewSelectionSnapsIntoView()
    {
        var host = new WinFormsAppHost();
        using ListView list = SixRows();
        _ = list.Handle; // selection reads go native, like the selection shims
        list.Items[5].Selected = true;
        Assert.Equal(2, host.ResolveListViewTop(list));
        list.Items[0].Selected = true;
        list.Items[5].Selected = false;
        Assert.Equal(0, host.ResolveListViewTop(list));
    }
}
