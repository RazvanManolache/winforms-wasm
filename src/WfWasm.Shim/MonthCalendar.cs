using System.Globalization;
using System.Text.Json;

namespace WfWasm.Shim;

/// <summary>
/// Event store mirroring the calendar app's events.json: date (day precision)
/// to text, JSON round-trippable.
/// </summary>
public sealed class CalendarEvents
{
    private readonly Dictionary<DateTime, string> _entries = new();

    public int Count => _entries.Count;

    public void Set(DateTime date, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        _entries[date.Date] = text;
    }

    public bool Remove(DateTime date) => _entries.Remove(date.Date);

    public bool TryGet(DateTime date, out string? text) => _entries.TryGetValue(date.Date, out text);

    public string ToJson() =>
        JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });

    public void LoadJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        _entries.Clear();
        var loaded = JsonSerializer.Deserialize<Dictionary<DateTime, string>>(json);
        if (loaded is null)
            return;
        foreach (var kv in loaded)
            _entries[kv.Key.Date] = kv.Value;
    }
}

/// <summary>
/// Month grid mirroring grp2Calendar's Form1 + UserControlDays: Sunday-first
/// 7x6 cells, weekend/today/event styling, rounded day cells (radius 15),
/// day click raising <see cref="DayClicked"/>, month navigation with the
/// app's wrap/clamp/name-parsing semantics.
/// </summary>
public class MonthCalendar : Control
{
    public const int CellWidth = 100;
    public const int CellHeight = 80;
    public const int HeaderHeight = 24;
    public const int Columns = 7;
    public const int Rows = 6;
    public const float CornerRadius = 15;

    private static readonly string[] DayNames = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];

    private const uint WeekdayFill = 0xFFFFFFFF;
    private const uint SundayFill = 0xFFD2DCFF;
    private const uint SaturdayFill = 0xC8E1FFFF;
    private const uint TodayFill = 0xD3D3D3FF;
    private const uint NormalText = 0x000000FF;
    private const uint SundayText = 0xFF8080FF;
    private const uint SaturdayText = 0x1E90FFFF;
    private const uint EventText = 0x006400FF;
    private const string DayFont = "13px sans-serif";
    private const string BoldDayFont = "bold 13px sans-serif";

    private int _year = DateTime.Today.Year;
    private int _month = DateTime.Today.Month;

    public MonthCalendar(string name = "")
        : base(name)
    {
        Width = Columns * CellWidth;
        Height = HeaderHeight + Rows * CellHeight;
    }

    public event EventHandler<DateTime>? DayClicked;

    public int Year => _year;
    public int Month => _month;

    public DateTime Today { get; set; } = DateTime.Today;

    public new CalendarEvents Events { get; } = new();

    /// <summary>Next month with year wrap (cf. btnNext_Click).</summary>
    public void MoveNext()
    {
        _month++;
        if (_month > 12)
        {
            _month = 1;
            _year++;
        }
    }

    /// <summary>Previous month with year wrap (cf. btnPrevious_Click).</summary>
    public void MovePrevious()
    {
        _month--;
        if (_month < 1)
        {
            _month = 12;
            _year--;
        }
    }

    /// <summary>
    /// Jump to a month/year (cf. btnGo_Click): month accepts 1-12 (clamped)
    /// or a full month name; invalid input returns false (the app's warning
    /// path). Month names parse with the invariant culture; the app uses
    /// the current culture.
    /// </summary>
    public bool GoTo(string monthText, string yearText)
    {
        ArgumentNullException.ThrowIfNull(monthText);
        ArgumentNullException.ThrowIfNull(yearText);

        int inputMonth;
        string m = monthText.Trim();
        if (int.TryParse(m, out int n))
        {
            inputMonth = Math.Max(1, Math.Min(12, n));
        }
        else
        {
            try
            {
                inputMonth = DateTime.ParseExact(m, "MMMM", CultureInfo.InvariantCulture).Month;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        if (!int.TryParse(yearText.Trim(), out int inputYear))
            return false;

        _month = inputMonth;
        _year = inputYear;
        return true;
    }

    /// <summary>Date at a parent-space point, or null for blanks/outside.</summary>
    public DateTime? DateAt(int x, int y)
    {
        int lx = x - X;
        int ly = y - Y - HeaderHeight;
        if (lx < 0 || ly < 0)
            return null;
        int col = lx / CellWidth;
        int row = ly / CellHeight;
        if (col < 0 || col >= Columns || row < 0 || row >= Rows)
            return null;
        int dayNumber = row * Columns + col - FirstDayOffset() + 1;
        if (dayNumber < 1 || dayNumber > DateTime.DaysInMonth(_year, _month))
            return null;
        return new DateTime(_year, _month, dayNumber);
    }

    /// <summary>Click a day cell: raises <see cref="DayClicked"/>. False when no date there.</summary>
    public bool ClickDayAt(int x, int y)
    {
        DateTime? date = DateAt(x, y);
        if (date is null)
            return false;
        DayClicked?.Invoke(this, date.Value);
        return true;
    }

    protected override void PaintSelf(ICanvas canvas, int ax, int ay)
    {
        for (int c = 0; c < Columns; c++)
        {
            uint color = c == 0 ? SundayText : c == 6 ? SaturdayText : NormalText;
            canvas.DrawText(DayNames[c], ax + c * CellWidth + 8, ay + 17, DayFont, color);
        }

        int offset = FirstDayOffset();
        int days = DateTime.DaysInMonth(_year, _month);
        for (int day = 1; day <= days; day++)
        {
            var date = new DateTime(_year, _month, day);
            int index = offset + day - 1;
            int col = index % Columns;
            int row = index / Columns;
            float cx = ax + col * CellWidth;
            float cy = ay + HeaderHeight + row * CellHeight;

            uint fill = date.DayOfWeek == DayOfWeek.Sunday ? SundayFill
                : date.DayOfWeek == DayOfWeek.Saturday ? SaturdayFill
                : WeekdayFill;
            if (date == Today.Date)
                fill = TodayFill;
            canvas.FillRoundedRectangle(cx, cy, CellWidth, CellHeight, CornerRadius, fill);

            uint textColor = Events.TryGet(date, out _) ? EventText : NormalText;
            string font = date == Today.Date ? BoldDayFont : DayFont;
            canvas.DrawText(day.ToString(CultureInfo.InvariantCulture), cx + 8, cy + 22, font, textColor);
        }
    }

    private int FirstDayOffset() => (int)new DateTime(_year, _month, 1).DayOfWeek;
}
