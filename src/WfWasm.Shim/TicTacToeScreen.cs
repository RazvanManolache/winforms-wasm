namespace WfWasm.Shim;

/// <summary>
/// Tic-tac-toe mirror (cf. zakaria-sahki/tic-tac-toe-game Form1): 9-cell
/// board with X/O turns, win/draw detection, 10-round match with per-player
/// statistics, Reset Round and Play Again. Deviations: cell marks are big
/// button text instead of X/O images (no image support in the shim);
/// winning cells are not tinted green (no per-button BackColor); the
/// painted board lines are implied by the button grid; the group disable
/// is applied per cell button (no enable cascade in the shim).
/// </summary>
public sealed class TicTacToeScreen
{
    private enum Player
    {
        Player1,
        Player2,
    }

    private enum Winner
    {
        Player1,
        Player2,
        Draw,
        InProgress,
    }

    private readonly Button[] _cells = new Button[9];
    private readonly string[] _marks = new string[9];
    private readonly Action<string, string> _showMessage;

    private Player _turn = Player.Player1;
    private Winner _winner = Winner.InProgress;
    private int _playCount;
    private int _playedRounds;
    private int _player1WonRounds;
    private int _player2WonRounds;

    public Form Window { get; }

    public Label TurnLabel { get; }

    public Label WinnerLabel { get; }

    public Label Player1Stats { get; }

    public Label Player2Stats { get; }

    private readonly Button _resetRound;

    public TicTacToeScreen(int x, int y, Action<string, string>? showMessage = null)
    {
        _showMessage = showMessage ?? ((_, _) => { });
        Window = new Form("tictactoe") { X = x, Y = y, Width = 560, Height = 560, Text = "Tic Tac Toe" };

        Window.AddChild(new Label("title")
        {
            X = 12, Y = 8, Width = 300, Height = 30, Text = "Tic Tac Toe",
        });
        Window.AddChild(new Label("turnCap") { X = 12, Y = 42, Width = 60, Height = 22, Text = "Turn:" });
        TurnLabel = new Label("turn") { X = 76, Y = 42, Width = 120, Height = 22, Text = "Player 1" };
        Window.AddChild(TurnLabel);
        Window.AddChild(new Label("winCap") { X = 220, Y = 42, Width = 70, Height = 22, Text = "Winner:" });
        WinnerLabel = new Label("winner") { X = 294, Y = 42, Width = 150, Height = 22, Text = "In Progress" };
        Window.AddChild(WinnerLabel);

        for (int i = 0; i < 9; i++)
        {
            int row = i / 3;
            int col = i % 3;
            var cell = new Button("cell" + i)
            {
                X = 100 + (col * 100), Y = 72 + (row * 100),
                Width = 92, Height = 92, Text = "", FontCss = "40px sans-serif",
            };
            int index = i;
            cell.Click += (_, _) => Play(index);
            _cells[i] = cell;
            _marks[i] = "?";
            Window.AddChild(cell);
        }

        _resetRound = new Button("reset") { X = 100, Y = 384, Width = 130, Height = 30, Text = "Reset Round" };
        _resetRound.Click += (_, _) => ResetGame();
        Window.AddChild(_resetRound);
        var again = new Button("again") { X = 238, Y = 384, Width = 130, Height = 30, Text = "Play Again" };
        again.Click += (_, _) => PlayAgain();
        Window.AddChild(again);

        Window.AddChild(new Label("statX") { X = 100, Y = 424, Width = 90, Height = 22, Text = "Player X =" });
        Player1Stats = new Label("statXVal") { X = 194, Y = 424, Width = 120, Height = 22, Text = "0 time(s)" };
        Window.AddChild(Player1Stats);
        Window.AddChild(new Label("statO") { X = 100, Y = 448, Width = 90, Height = 22, Text = "Player O =" });
        Player2Stats = new Label("statOVal") { X = 194, Y = 448, Width = 120, Height = 22, Text = "0 time(s)" };
        Window.AddChild(Player2Stats);
        Window.AddChild(new Label("note")
        {
            X = 100, Y = 476, Width = 440, Height = 22,
            Text = "Important Note: You can play 10 Rounds Only.",
        });
        Window.AddChild(new Label("madeby") { X = 100, Y = 500, Width = 250, Height = 22, Text = "Made By Zakaria Sahki" });
        ResetGame();
    }

    private void Play(int index)
    {
        if (!_cells[index].Enabled)
            return;
        if (_marks[index] == "?")
        {
            if (_turn == Player.Player1)
            {
                _cells[index].Text = "X";
                _marks[index] = "X";
                _playCount++;
                _turn = Player.Player2;
                TurnLabel.Text = "Player 2";
                CheckWinner();
            }
            else
            {
                _cells[index].Text = "O";
                _marks[index] = "O";
                _playCount++;
                _turn = Player.Player1;
                TurnLabel.Text = "Player 1";
                CheckWinner();
            }
        }
        else
        {
            _showMessage("Wrong Choice", "Wrong");
        }

        if (_playCount == 9)
        {
            _winner = Winner.Draw;
            EndGame();
        }
    }

    private bool CheckLine(int a, int b, int c)
    {
        if (_marks[a] != "?" && _marks[a] == _marks[b] && _marks[a] == _marks[c])
        {
            if (_marks[a] == "X")
            {
                _winner = Winner.Player1;
                _player1WonRounds++;
                EndGame();
                return true;
            }

            _winner = Winner.Player2;
            _player2WonRounds++;
            EndGame();
            return true;
        }

        return false;
    }

    private void CheckWinner()
    {
        if (CheckLine(0, 1, 2)) return;
        if (CheckLine(3, 4, 5)) return;
        if (CheckLine(6, 7, 8)) return;
        if (CheckLine(0, 3, 6)) return;
        if (CheckLine(1, 4, 7)) return;
        if (CheckLine(2, 5, 8)) return;
        if (CheckLine(0, 4, 8)) return;
        if (CheckLine(2, 4, 6)) return;
    }

    private void EndGame()
    {
        TurnLabel.Text = "Game Over";
        WinnerLabel.Text = _winner switch
        {
            Winner.Player1 => "Player 1",
            Winner.Player2 => "Player 2",
            _ => "Draw",
        };
        _showMessage("Game Over", "Game Over");
        foreach (Button cell in _cells)
            cell.Enabled = false;
        _playedRounds++;
        Player1Stats.Text = _player1WonRounds + " time(s)";
        Player2Stats.Text = _player2WonRounds + " time(s)";
        EndWholeGame();
    }

    private void EndWholeGame()
    {
        if (_playedRounds != 10)
            return;
        string message = "The Winner of Game is: " + GameWinner() + ".";
        _showMessage(message, "End Of Game");
        _resetRound.Enabled = false;
    }

    private string GameWinner()
    {
        if (_player1WonRounds > _player2WonRounds)
            return "Player 1";
        if (_player2WonRounds > _player1WonRounds)
            return "Player 2";
        return "Draw";
    }

    private void ResetGame()
    {
        foreach (Button cell in _cells)
        {
            cell.Text = "";
            cell.Enabled = true;
        }

        Array.Fill(_marks, "?");
        _winner = Winner.InProgress;
        WinnerLabel.Text = "In Progress";
        _turn = Player.Player1;
        TurnLabel.Text = "Player 1";
        _playCount = 0;
    }

    private void PlayAgain()
    {
        _playedRounds = 0;
        _player1WonRounds = 0;
        _player2WonRounds = 0;
        Player1Stats.Text = "0 time(s)";
        Player2Stats.Text = "0 time(s)";
        _resetRound.Enabled = true;
        ResetGame();
    }
}
