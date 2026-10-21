// Headless System.Windows.Extensions shape stub. Desktop apps (Telerik
// RadMessageBox and friends) touch System.Media for UI sounds, so the
// types must load; the play verbs are silent no-ops because no audio
// device exists in the browser. Same neutralization policy as the fork's
// P/Invoke stubs. A WebAudio bridge can light these up later (SoundPlayer
// keeps its Stream/SoundLocation state so playback can start from it).
// API shape mirrors the real assembly (SystemSound.Play,
// SystemSounds x5, full SoundPlayer); members not listed there do not
// exist here on purpose.

using System.ComponentModel;
using System.IO;

namespace System.Media;

public class SystemSound
{
    internal SystemSound()
    {
    }

    public void Play()
    {
    }
}

public static class SystemSounds
{
    public static SystemSound Asterisk { get; } = new();
    public static SystemSound Beep { get; } = new();
    public static SystemSound Exclamation { get; } = new();
    public static SystemSound Hand { get; } = new();
    public static SystemSound Question { get; } = new();
}

public class SoundPlayer : Component
{
    private int _loadTimeout = 10000;
    private string _soundLocation = string.Empty;
    private Stream? _stream;

    public SoundPlayer()
    {
    }

    public SoundPlayer(Stream stream) => _stream = stream;

    public SoundPlayer(string soundLocation) => _soundLocation = soundLocation ?? string.Empty;

    public bool IsLoadCompleted { get; private set; }

    public int LoadTimeout
    {
        get => _loadTimeout;
        set => _loadTimeout = value;
    }

    public string SoundLocation
    {
        get => _soundLocation;
        set
        {
            if (_soundLocation == value)
                return;
            _soundLocation = value ?? string.Empty;
            _stream = null;
            IsLoadCompleted = false;
            SoundLocationChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Stream? Stream
    {
        get => _stream;
        set
        {
            if (_stream == value)
                return;
            _stream = value;
            _soundLocation = string.Empty;
            IsLoadCompleted = false;
            StreamChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public object? Tag { get; set; }

    public event AsyncCompletedEventHandler? LoadCompleted;

    public event EventHandler? SoundLocationChanged;

    public event EventHandler? StreamChanged;

    public void Load() => IsLoadCompleted = true;

    public void LoadAsync()
    {
        IsLoadCompleted = true;
        LoadCompleted?.Invoke(this, new AsyncCompletedEventArgs(null, false, null));
    }

    public void Play()
    {
    }

    public void PlayLooping()
    {
    }

    public void PlaySync()
    {
    }

    public void Stop()
    {
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}
