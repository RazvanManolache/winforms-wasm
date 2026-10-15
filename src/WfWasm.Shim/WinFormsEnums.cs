namespace WfWasm.Shim;

/// <summary>How a button-like control paints itself (cf. FlatStyle).</summary>
public enum FlatStyle
{
    Standard,
    Flat,
    Popup,
    System,
}

/// <summary>Check state (cf. CheckState): unchecked, checked, indeterminate.</summary>
public enum CheckState
{
    Unchecked,
    Checked,
    Indeterminate,
}

/// <summary>CheckBox/RadioButton presentation (cf. Appearance).</summary>
public enum Appearance
{
    Normal,
    Button,
}

/// <summary>Control border (cf. BorderStyle): none, 1px, sunken 3D.</summary>
public enum BorderStyle
{
    None,
    FixedSingle,
    Fixed3D,
}

/// <summary>Modal result (cf. DialogResult).</summary>
public enum DialogResult
{
    None,
    OK,
    Cancel,
    Abort,
    Retry,
    Ignore,
    Yes,
    No,
}

/// <summary>Window show state (cf. FormWindowState).</summary>
public enum FormWindowState
{
    Normal,
    Minimized,
    Maximized,
}

/// <summary>Dialog buttons (cf. MessageBoxButtons).</summary>
public enum MessageBoxButtons
{
    OK = 0,
    OKCancel = 1,
    AbortRetryIgnore = 2,
    YesNoCancel = 3,
    YesNo = 4,
    RetryCancel = 5,
    CancelTryContinue = 6,
}

/// <summary>Dialog icon (cf. MessageBoxIcon).</summary>
public enum MessageBoxIcon
{
    None = 0,
    Hand = 16,
    Question = 32,
    Exclamation = 48,
    Asterisk = 64,
    Error = 16,
    Stop = 16,
    Warning = 48,
    Information = 64,
}

/// <summary>Default dialog button (cf. MessageBoxDefaultButton).</summary>
public enum MessageBoxDefaultButton
{
    Button1 = 0,
    Button2 = 256,
    Button3 = 512,
}

/// <summary>Column auto-size rule (cf. ColumnHeaderAutoResizeStyle).</summary>
public enum ColumnHeaderAutoResizeStyle
{
    None = 0,
    HeaderSize = 1,
    ColumnContent = 2,
}

/// <summary>TextBox scrollbars (cf. ScrollBars).</summary>
public enum ScrollBars
{
    None,
    Horizontal,
    Vertical,
    Both,
}

/// <summary>Rich edit scroll bars (cf. RichTextBoxScrollBars).</summary>
public enum RichTextBoxScrollBars
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
    Both = 3,
    ForcedHorizontal = 17,
    ForcedVertical = 18,
    ForcedBoth = 19,
}

/// <summary>Rich edit search flags (cf. RichTextBoxFinds).</summary>
[Flags]
public enum RichTextBoxFinds
{
    None = 0,
    WholeWord = 2,
    MatchCase = 4,
    NoHighlight = 8,
    Reverse = 16,
}

/// <summary>Slider ticks (cf. TickStyle).</summary>
public enum TickStyle
{
    None = 0,
    TopLeft = 1,
    BottomRight = 2,
    Both = 3,
}

/// <summary>Axis orientation (cf. Orientation).</summary>
public enum Orientation
{
    Horizontal = 0,
    Vertical = 1,
}

/// <summary>Drop-down close cause (cf. ToolStripDropDownCloseReason).</summary>
public enum ToolStripDropDownCloseReason
{
    AppFocusChange = 0,
    AppClicked = 1,
    ItemClicked = 2,
    Keyboard = 3,
    CloseCalled = 4,
}

/// <summary>Grid extent rule (cf. SizeType).</summary>
public enum SizeType
{
    AutoSize = 0,
    Absolute = 1,
    Percent = 2,
}

/// <summary>Grid growth (cf. TableLayoutPanelGrowStyle).</summary>
public enum TableLayoutPanelGrowStyle
{
    FixedSize = 0,
    AddRows = 1,
    AddColumns = 2,
}

/// <summary>Grid cell border (cf. TableLayoutPanelCellBorderStyle).</summary>
public enum TableLayoutPanelCellBorderStyle
{
    None = 0,
    Single = 1,
    Inset = 2,
    InsetDouble = 3,
    Outset = 4,
    OutsetDouble = 5,
    OutsetPartial = 6,
}

/// <summary>Picture scaling (cf. PictureBoxSizeMode).</summary>
public enum PictureBoxSizeMode
{
    Normal = 0,
    StretchImage = 1,
    AutoSize = 2,
    CenterImage = 3,
    Zoom = 4,
}

/// <summary>Check-box theme state (cf. VisualStyles.CheckBoxState).</summary>
public enum CheckBoxState
{
    UncheckedNormal = 1,
    UncheckedHot = 2,
    UncheckedPressed = 3,
    UncheckedDisabled = 4,
    CheckedNormal = 5,
    CheckedHot = 6,
    CheckedPressed = 7,
    CheckedDisabled = 8,
    MixedNormal = 9,
    MixedHot = 10,
    MixedPressed = 11,
    MixedDisabled = 12,
}

/// <summary>Push-button theme state (cf. VisualStyles.PushButtonState).</summary>
public enum PushButtonState
{
    Normal = 1,
    Hot = 2,
    Pressed = 3,
    Disabled = 4,
    Default = 5,
}

/// <summary>Radio-button theme state (cf. VisualStyles.RadioButtonState).</summary>
public enum RadioButtonState
{
    UncheckedNormal = 1,
    UncheckedHot = 2,
    UncheckedPressed = 3,
    UncheckedDisabled = 4,
    CheckedNormal = 5,
    CheckedHot = 6,
    CheckedPressed = 7,
    CheckedDisabled = 8,
}

/// <summary>Spin-button side (cf. LeftRightAlignment).</summary>
public enum LeftRightAlignment
{
    Left = 0,
    Right = 1,
}

/// <summary>Strip theme source (cf. ToolStripRenderMode).</summary>
public enum ToolStripRenderMode
{
    Custom = 0,
    System = 1,
    Professional = 2,
    ManagerRenderMode = 3,
}

/// <summary>Top-level window frame (cf. FormBorderStyle).</summary>
public enum FormBorderStyle
{
    None,
    FixedSingle,
    Fixed3D,
    FixedDialog,
    Sizable,
    FixedToolWindow,
    SizableToolWindow,
}

/// <summary>Caption buttons (cf. CaptionButton): close, help, min, max, restore.</summary>
public enum CaptionButton
{
    Close,
    Help,
    Minimize,
    Maximize,
    Restore,
}

/// <summary>Button pressed/hover/disabled state (cf. ButtonState/PushButtonState).</summary>
/// <summary>Push-button appearance flags (cf. ButtonState).</summary>
[Flags]
public enum ButtonState
{
    Normal = 0,
    Inactive = 256,
    Pushed = 512,
    Checked = 1024,
    Flat = 16384,
    All = 18176,
}

/// <summary>ComboBox presentation (cf. ComboBoxStyle).</summary>
public enum ComboBoxStyle
{
    DropDown,
    Simple,
    DropDownList,
}

/// <summary>Scroll arrow direction (cf. ScrollButton).</summary>
public enum ScrollDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>Menu glyphs (cf. MenuGlyph): submenu arrow, checkmark, bullet.</summary>
public enum MenuGlyph
{
    Arrow,
    Checkmark,
    Bullet,
}

/// <summary>Grid cell border mode (cf. DataGridViewCellBorderStyle subset).</summary>
public enum DataGridViewCellBorderStyle
{
    Single,
    Raised,
    Sunken,
    None,
}

/// <summary>Mouse buttons (cf. MouseButtons).</summary>
[Flags]
public enum MouseButtons
{
    None = 0,
    Left = 0x100000,
    Right = 0x200000,
    Middle = 0x400000,
    XButton1 = 0x800000,
    XButton2 = 0x1000000,
}

/// <summary>Drag effects (cf. DragDropEffects).</summary>
[Flags]
public enum DragDropEffects
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
    Scroll = unchecked((int)0x80000000),
    All = unchecked((int)0x80000007),
}

/// <summary>Owner-draw item state (cf. DrawItemState).</summary>
[Flags]
public enum DrawItemState
{
    None = 0,
    Selected = 1,
    Grayed = 2,
    Disabled = 4,
    Checked = 8,
    Focus = 16,
    Default = 32,
    HotLight = 64,
    Inactive = 128,
    NoAccelerator = 256,
    NoFocusRect = 512,
    ComboBoxEdit = 4096,
}

/// <summary>Edge anchoring (cf. AnchorStyles).</summary>
[Flags]
public enum AnchorStyles
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 4,
    Right = 8,
}

/// <summary>Edge docking (cf. DockStyle).</summary>
public enum DockStyle
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 3,
    Right = 4,
    Fill = 5,
}

/// <summary>Which bounds SetBounds sets (cf. BoundsSpecified).</summary>
[Flags]
public enum BoundsSpecified
{
    None = 0,
    X = 1,
    Y = 2,
    Location = 3,
    Width = 4,
    Height = 8,
    Size = 12,
    All = 15,
}

/// <summary>Initial form placement (cf. FormStartPosition).</summary>
public enum FormStartPosition
{
    Manual = 0,
    CenterScreen = 1,
    WindowsDefaultLocation = 2,
    WindowsDefaultBounds = 3,
    CenterParent = 4,
}

/// <summary>Scroll bar orientation (cf. ScrollOrientation).</summary>
public enum ScrollOrientation
{
    HorizontalScroll = 1,
    VerticalScroll = 2,
}

/// <summary>Scroll event kind (cf. ScrollEventType).</summary>
public enum ScrollEventType
{
    SmallDecrement = 0,
    SmallIncrement = 1,
    LargeDecrement = 2,
    LargeIncrement = 3,
    ThumbPosition = 4,
    ThumbTrack = 5,
    First = 6,
    Last = 7,
    EndScroll = 8,
}

/// <summary>Why a form closes (cf. CloseReason).</summary>
public enum CloseReason
{
    None = 0,
    WindowsShutDown = 1,
    MdiFormClosing = 2,
    UserClosing = 3,
    TaskManagerClosing = 4,
    FormOwnerClosing = 5,
    ApplicationExitCall = 6,
}

/// <summary>ComboBox/ListBox painting mode (cf. DrawMode).</summary>
public enum DrawMode
{
    Normal = 0,
    OwnerDrawFixed = 1,
    OwnerDrawVariable = 2,
}

/// <summary>ListBox selection mode (cf. SelectionMode).</summary>
public enum SelectionMode
{
    None = 0,
    One = 1,
    MultiSimple = 2,
    MultiExtended = 3,
}

/// <summary>ListView presentation (cf. View).</summary>
public enum View
{
    LargeIcon = 0,
    Details = 1,
    SmallIcon = 2,
    List = 3,
    Tile = 4,
}

/// <summary>Progress bar fill mode (cf. ProgressBarStyle).</summary>
public enum ProgressBarStyle
{
    Blocks = 0,
    Continuous = 1,
    Marquee = 2,
}

/// <summary>Form size-grip display (cf. SizeGripStyle).</summary>
public enum SizeGripStyle
{
    Auto = 0,
    Show = 1,
    Hide = 2,
}

/// <summary>Automatic scaling basis (cf. AutoScaleMode).</summary>
public enum AutoScaleMode
{
    None = 0,
    Font = 1,
    Dpi = 2,
    Inherit = 3,
}

/// <summary>Control capability flags (cf. ControlStyles).</summary>
[Flags]
public enum ControlStyles
{
    ContainerControl = 0x01,
    UserPaint = 0x02,
    Opaque = 0x04,
    ResizeRedraw = 0x10,
    FixedWidth = 0x20,
    FixedHeight = 0x40,
    StandardClick = 0x100,
    Selectable = 0x200,
    UserMouse = 0x400,
    SupportsTransparentBackColor = 0x800,
    StandardDoubleClick = 0x1000,
    AllPaintingInWmPaint = 0x2000,
    CacheText = 0x4000,
    EnableNotifyMessage = 0x8000,
    DoubleBuffer = 0x10000,
    OptimizedDoubleBuffer = 0x20000,
}

/// <summary>Key codes (cf. Keys): Win32 virtual keys plus modifiers.</summary>
[Flags]
public enum Keys
{
    None = 0,
    LButton = 0x01,
    RButton = 0x02,
    Cancel = 0x03,
    MButton = 0x04,
    XButton1 = 0x05,
    XButton2 = 0x06,
    Back = 0x08,
    Tab = 0x09,
    LineFeed = 0x0A,
    Clear = 0x0C,
    Return = 0x0D,
    Enter = 0x0D,
    ShiftKey = 0x10,
    ControlKey = 0x11,
    Menu = 0x12,
    Pause = 0x13,
    Capital = 0x14,
    CapsLock = 0x14,
    KanaMode = 0x15,
    HanguelMode = 0x15,
    HangulMode = 0x15,
    JunjaMode = 0x17,
    FinalMode = 0x18,
    HanjaMode = 0x19,
    KanjiMode = 0x19,
    Escape = 0x1B,
    IMEConvert = 0x1C,
    IMENonconvert = 0x1D,
    IMEAccept = 0x1E,
    IMEAceept = 0x1E,
    IMEModeChange = 0x1F,
    Space = 0x20,
    Prior = 0x21,
    PageUp = 0x21,
    Next = 0x22,
    PageDown = 0x22,
    End = 0x23,
    Home = 0x24,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    Select = 0x29,
    Print = 0x2A,
    Execute = 0x2B,
    Snapshot = 0x2C,
    PrintScreen = 0x2C,
    Insert = 0x2D,
    Delete = 0x2E,
    Help = 0x2F,
    D0 = 0x30,
    D1 = 0x31,
    D2 = 0x32,
    D3 = 0x33,
    D4 = 0x34,
    D5 = 0x35,
    D6 = 0x36,
    D7 = 0x37,
    D8 = 0x38,
    D9 = 0x39,
    A = 0x41,
    B = 0x42,
    C = 0x43,
    D = 0x44,
    E = 0x45,
    F = 0x46,
    G = 0x47,
    H = 0x48,
    I = 0x49,
    J = 0x4A,
    K = 0x4B,
    L = 0x4C,
    M = 0x4D,
    N = 0x4E,
    O = 0x4F,
    P = 0x50,
    Q = 0x51,
    R = 0x52,
    S = 0x53,
    T = 0x54,
    U = 0x55,
    V = 0x56,
    W = 0x57,
    X = 0x58,
    Y = 0x59,
    Z = 0x5A,
    LWin = 0x5B,
    RWin = 0x5C,
    Apps = 0x5D,
    Sleep = 0x5F,
    NumPad0 = 0x60,
    NumPad1 = 0x61,
    NumPad2 = 0x62,
    NumPad3 = 0x63,
    NumPad4 = 0x64,
    NumPad5 = 0x65,
    NumPad6 = 0x66,
    NumPad7 = 0x67,
    NumPad8 = 0x68,
    NumPad9 = 0x69,
    Multiply = 0x6A,
    Add = 0x6B,
    Separator = 0x6C,
    Subtract = 0x6D,
    Decimal = 0x6E,
    Divide = 0x6F,
    F1 = 0x70,
    F2 = 0x71,
    F3 = 0x72,
    F4 = 0x73,
    F5 = 0x74,
    F6 = 0x75,
    F7 = 0x76,
    F8 = 0x77,
    F9 = 0x78,
    F10 = 0x79,
    F11 = 0x7A,
    F12 = 0x7B,
    F13 = 0x7C,
    F14 = 0x7D,
    F15 = 0x7E,
    F16 = 0x7F,
    F17 = 0x80,
    F18 = 0x81,
    F19 = 0x82,
    F20 = 0x83,
    F21 = 0x84,
    F22 = 0x85,
    F23 = 0x86,
    F24 = 0x87,
    NumLock = 0x90,
    Scroll = 0x91,
    LShiftKey = 0xA0,
    RShiftKey = 0xA1,
    LControlKey = 0xA2,
    RControlKey = 0xA3,
    LMenu = 0xA4,
    RMenu = 0xA5,
    BrowserBack = 0xA6,
    BrowserForward = 0xA7,
    BrowserRefresh = 0xA8,
    BrowserStop = 0xA9,
    BrowserSearch = 0xAA,
    BrowserFavorites = 0xAB,
    BrowserHome = 0xAC,
    VolumeMute = 0xAD,
    VolumeDown = 0xAE,
    VolumeUp = 0xAF,
    MediaNextTrack = 0xB0,
    MediaPreviousTrack = 0xB1,
    MediaStop = 0xB2,
    MediaPlayPause = 0xB3,
    LaunchMail = 0xB4,
    SelectMedia = 0xB5,
    LaunchApplication1 = 0xB6,
    LaunchApplication2 = 0xB7,
    Oem1 = 0xBA,
    OemSemicolon = 0xBA,
    Oemplus = 0xBB,
    Oemcomma = 0xBC,
    OemMinus = 0xBD,
    OemPeriod = 0xBE,
    OemQuestion = 0xBF,
    Oem2 = 0xBF,
    Oemtilde = 0xC0,
    Oem3 = 0xC0,
    OemOpenBrackets = 0xDB,
    Oem4 = 0xDB,
    OemPipe = 0xDC,
    Oem5 = 0xDC,
    OemCloseBrackets = 0xDD,
    Oem6 = 0xDD,
    OemQuotes = 0xDE,
    Oem7 = 0xDE,
    Oem8 = 0xDF,
    OemBackslash = 0xE2,
    Oem102 = 0xE2,
    ProcessKey = 0xE5,
    Packet = 0xE7,
    Attn = 0xF6,
    Crsel = 0xF7,
    Exsel = 0xF8,
    EraseEof = 0xF9,
    Play = 0xFA,
    Zoom = 0xFB,
    NoName = 0xFC,
    Pa1 = 0xFD,
    OemClear = 0xFE,
    Shift = 0x10000,
    Control = 0x20000,
    Alt = 0x40000,
    KeyCode = 0xFFFF,
    Modifiers = unchecked((int)0xFFFF0000),
}

/// <summary>Strip item content (cf. ToolStripItemDisplayStyle).</summary>
public enum ToolStripItemDisplayStyle
{
    None = 0,
    Text = 1,
    Image = 2,
    ImageAndText = 3,
}

/// <summary>Strip text orientation (cf. ToolStripTextDirection).</summary>
public enum ToolStripTextDirection
{
    Inherit = 0,
    Horizontal = 1,
    Vertical90 = 2,
    Vertical270 = 3,
}

/// <summary>Strip item placement (cf. ToolStripItemAlignment).</summary>
public enum ToolStripItemAlignment
{
    Left = 0,
    Right = 1,
}

/// <summary>Strip overflow behavior (cf. ToolStripItemOverflow).</summary>
public enum ToolStripItemOverflow
{
    Never = 0,
    Always = 1,
    AsNeeded = 2,
}

/// <summary>Layout direction (cf. RightToLeft).</summary>
public enum RightToLeft
{
    No = 0,
    Yes = 1,
    Inherit = 2,
}

/// <summary>Image/text arrangement (cf. TextImageRelation).</summary>
public enum TextImageRelation
{
    Overlay = 0,
    ImageAboveText = 1,
    TextAboveImage = 2,
    ImageBeforeText = 4,
    TextBeforeImage = 8,
}

/// <summary>Strip image sizing (cf. ToolStripItemImageScaling).</summary>
public enum ToolStripItemImageScaling
{
    None = 0,
    SizeToFit = 1,
}

/// <summary>Strip item placement (cf. ToolStripItemPlacement).</summary>
public enum ToolStripItemPlacement
{
    None = 0,
    Main = 1,
    Overflow = 2,
}

/// <summary>Strip grip style (cf. ToolStripGripStyle).</summary>
public enum ToolStripGripStyle
{
    Hidden = 0,
    Visible = 1,
}

/// <summary>Strip layout (cf. ToolStripLayoutStyle).</summary>
public enum ToolStripLayoutStyle
{
    StackWithOverflow = 0,
    HorizontalStackWithOverflow = 1,
    VerticalStackWithOverflow = 2,
    Flow = 3,
    Table = 4,
}

/// <summary>Drop-down direction (cf. ToolStripDropDownDirection).</summary>
public enum ToolStripDropDownDirection
{
    AboveLeft = 0,
    AboveRight = 1,
    BelowLeft = 2,
    BelowRight = 3,
    Left = 4,
    Right = 5,
    Default = 7,
}

/// <summary>Drop-down arrow direction (cf. ArrowDirection).</summary>
public enum ArrowDirection
{
    Left = 0,
    Up = 1,
    Right = 2,
    Down = 3,
}

/// <summary>Flow direction (cf. FlowDirection).</summary>
public enum FlowDirection
{
    LeftToRight = 0,
    TopDown = 1,
    RightToLeft = 2,
    BottomUp = 3,
}

/// <summary>Text alignment (cf. HorizontalAlignment).</summary>
public enum HorizontalAlignment
{
    Left = 0,
    Right = 1,
    Center = 2,
}

/// <summary>TextBox completion behavior (cf. AutoCompleteMode).</summary>
public enum AutoCompleteMode
{
    None = 0,
    Suggest = 1,
    Append = 2,
    SuggestAppend = 3,
}

/// <summary>TextBox completion source (cf. AutoCompleteSource, flag values
/// per the underlying SHAutoComplete mapping).</summary>
public enum AutoCompleteSource
{
    None = 0,
    FileSystem = 1,
    HistoryList = 2,
    RecentlyUsedList = 4,
    AllUrl = 8,
    AllSystemSources = 16,
    FileSystemDirectories = 32,
    CustomSource = 64,
    AllSources = 127,
}

/// <summary>Text format flags (cf. TextFormatFlags, common subset).</summary>
[Flags]
public enum TextFormatFlags
{
    Default = 0,
    Left = 0,
    Top = 0,
    HorizontalCenter = 1,
    Right = 2,
    VerticalCenter = 4,
    Bottom = 8,
    WordBreak = 16,
    SingleLine = 32,
    ExpandTabs = 64,
    ExternalLeading = 512,
    NoClipping = 256,
    NoPrefix = 2048,
    TextBoxControl = 8192,
    EndEllipsis = 16384,
    PathEllipsis = 32768,
    ModifyString = 65536,
    HidePrefix = 1048576,
}

/// <summary>Which edge a tab row docks to (cf. TabAlignment).</summary>
public enum TabAlignment
{
    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>UI Automation role of a control (cf. AccessibleRole).</summary>
public enum AccessibleRole
{
    Default = -1,
    None,
    TitleBar,
    MenuBar,
    ScrollBar,
    Grip,
    Sound,
    Cursor,
    Caret,
    Alert,
    Client,
    MenuPopup,
    MenuItem,
    ToolTip,
    Application,
    Document,
    Pane,
    Chart,
    Dialog,
    Border,
    ColumnHeader,
    RowHeader,
    Column,
    Row,
    Cell,
    Link,
    HelpBalloon,
    Character,
    List,
    ListItem,
    Outline,
    OutlineItem,
    PageTab,
    PropertyPage,
    Indicator,
    Graphic,
    StaticText,
    Text,
    PushButton,
    CheckButton,
    RadioButton,
    ComboBox,
    DropList,
    ProgressBar,
    Dial,
    HotkeyField,
    Slider,
    SpinButton,
    Diagram,
    Animation,
    Equation,
    ButtonDropDown,
    ButtonMenu,
    ButtonDropDownGrid,
    WhiteSpace,
    PageTabList,
    Clock,
    SplitButton,
    IpAddress,
    OutlineButton,
}

/// <summary>How a control uses extra layout space (cf. AutoSizeMode).</summary>
public enum AutoSizeMode
{
    GrowAndShrink,
    GrowOnly,
}

/// <summary>3-D border variant for ControlPaint (cf. Border3DStyle).</summary>
public enum Border3DStyle
{
    Adjust = 8192,
    Bump = 9,
    Etched = 6,
    Flat = 16394,
    Raised = 5,
    RaisedInner = 4,
    RaisedOuter = 1,
    Sunken = 10,
    SunkenInner = 8,
    SunkenOuter = 2,
}

/// <summary>Which scrollbar arrow a theme paints (cf. ScrollButton).</summary>
public enum ScrollButton
{
    Min,
    Down = 1,
    Left = 2,
    Right = 3,
    Up = 4,
    Max = 5,
}

/// <summary>Group-box theme state (cf. VisualStyles.GroupBoxState).</summary>
public enum GroupBoxState
{
    Normal = 1,
    Disabled = 2,
}

/// <summary>Combo-box theme state (cf. VisualStyles.ComboBoxState).</summary>
public enum ComboBoxState
{
    Normal = 1,
    Hot = 2,
    Pressed = 3,
    Disabled = 4,
}

/// <summary>Theme color slot selector (cf. VisualStyles.ColorProperty).</summary>
public enum ColorProperty
{
    BorderColor = 3801,
    FillColor = 3802,
    TextColor = 3803,
    EdgeLightColor = 3804,
    EdgeHighlightColor = 3805,
    EdgeShadowColor = 3806,
    EdgeDkShadowColor = 3807,
    EdgeFillColor = 3808,
    TransparentColor = 3809,
    GradientColor1 = 3810,
    GradientColor2 = 3811,
    GradientColor3 = 3812,
    GradientColor4 = 3813,
    GradientColor5 = 3814,
    ShadowColor = 3815,
    GlowColor = 3816,
    TextBorderColor = 3817,
    TextShadowColor = 3818,
    GlyphTextColor = 3819,
    GlyphTransparentColor = 3820,
    FillColorHint = 3821,
    BorderColorHint = 3822,
    AccentColorHint = 3823,
}

/// <summary>3-D border sides (cf. Border3DSide).</summary>
[Flags]
public enum Border3DSide
{
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    Middle = 0x800,
    All = 0x80F,
}

/// <summary>Theme part-size kind (cf. VisualStyles.ThemeSizeType).</summary>
public enum ThemeSizeType
{
    MinimumSize,
    TrueSize,
}

/// <summary>Image-list color depth (cf. ColorDepth).</summary>
public enum ColorDepth
{
    Depth4Bit = 4,
    Depth8Bit = 8,
    Depth16Bit = 16,
    Depth24Bit = 24,
    Depth32Bit = 32,
}

/// <summary>Theme point slot selector (cf. VisualStyles.PointProperty).</summary>
public enum PointProperty
{
    Offset = 3401,
    TextShadowOffset = 3402,
    MinSize = 3403,
    MinSize1 = 3404,
    MinSize2 = 3405,
    MinSize3 = 3406,
    MinSize4 = 3407,
    MinSize5 = 3408,
}
