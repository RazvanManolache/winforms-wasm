namespace WfWasm.Shim;

/// <summary>
/// Calculator mirror (cf. albisnesstz/csharp-win-calculator Form1):
/// display plus %/DEL/00/dot/digit/operator/=/C grid with the original's
/// state machine (num1/num2/operation/isNewEntry) and percent rules.
/// Deviations: display is left-aligned (no textbox alignment in the shim),
/// divide-by-zero shows a canvas modal instead of a Win32 MessageBox.
/// </summary>
public sealed class CalcScreen
{
    private double _num1;
    private double _num2;
    private string _operation = "";
    private bool _isNewEntry = true;
    private readonly Action<string, string> _showMessage;

    public Form Window { get; }

    public TextBox Display { get; }

    public CalcScreen(int x, int y, Action<string, string>? showMessage = null)
    {
        _showMessage = showMessage ?? ((_, _) => { });
        Window = new Form("calc") { X = x, Y = y, Width = 300, Height = 330, Text = "Calculator" };
        Display = new TextBox("txtDisplay")
        {
            X = 12, Y = 12, Width = 276, Height = 34, Text = "0", ReadOnly = true,
        };
        Window.AddChild(Display);

        string[,] keys =
        {
            { "%", "DEL", "C", "/" },
            { "7", "8", "9", "*" },
            { "4", "5", "6", "-" },
            { "1", "2", "3", "+" },
            { "00", "0", ".", "=" },
        };
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                string key = keys[row, col];
                var button = new Button("btn" + key)
                {
                    X = 12 + (col * 68), Y = 56 + (row * 52),
                    Width = 62, Height = 46, Text = key,
                };
                button.Click += (_, _) => Press(key);
                Window.AddChild(button);
            }
        }
    }

    private void Press(string key)
    {
        if ((key.Length <= 2 && key.All(char.IsDigit)) || key == "00")
            NumberClick(key);
        else if (key == ".")
            DotClick();
        else if (key is "+" or "-" or "*" or "/")
            OperatorClick(key);
        else if (key == "%")
            PercentClick();
        else if (key == "=")
            EqualsClick();
        else if (key == "C")
            ClearClick();
        else if (key == "DEL")
            DeleteClick();
    }

    private void NumberClick(string digit)
    {
        if (Display.Text == "0" || _isNewEntry)
        {
            Display.Text = digit;
            _isNewEntry = false;
        }
        else
        {
            Display.Text += digit;
        }
    }

    private void DotClick()
    {
        if (_isNewEntry)
        {
            Display.Text = "0.";
            _isNewEntry = false;
        }
        else if (!Display.Text.Contains('.'))
        {
            Display.Text += ".";
        }
    }

    private void OperatorClick(string operation)
    {
        _num1 = double.Parse(Display.Text);
        _operation = operation;
        _isNewEntry = true;
    }

    private void PercentClick()
    {
        double value = double.Parse(Display.Text);
        if (_operation is "+" or "-")
            value = (_num1 * value) / 100;
        else
            value /= 100;
        Display.Text = value.ToString();
        _isNewEntry = true;
    }

    private void EqualsClick()
    {
        _num2 = double.Parse(Display.Text);
        switch (_operation)
        {
            case "+":
                Display.Text = (_num1 + _num2).ToString();
                break;
            case "-":
                Display.Text = (_num1 - _num2).ToString();
                break;
            case "*":
                Display.Text = (_num1 * _num2).ToString();
                break;
            case "/":
                if (_num2 != 0)
                    Display.Text = (_num1 / _num2).ToString();
                else
                {
                    _showMessage("Cannot divide by zero", "");
                    Display.Text = "0";
                }

                break;
            default:
                break;
        }

        _isNewEntry = true;
        _operation = "";
    }

    private void ClearClick()
    {
        Display.Text = "0";
        _num1 = 0;
        _num2 = 0;
        _operation = "";
        _isNewEntry = true;
    }

    private void DeleteClick()
    {
        if (!_isNewEntry && Display.Text.Length > 1)
            Display.Text = Display.Text[..^1];
        else
        {
            Display.Text = "0";
            _isNewEntry = true;
        }
    }
}
