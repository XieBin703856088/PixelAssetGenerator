using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace PixelAssetGenerator.Services;

/// <summary>Playback mode for animation sequences.</summary>
public enum AnimationPlayMode
{
    Loop,
    PingPong,
    SingleShot
}

/// <summary>
/// Drives animation playback by advancing a normalized time value [0, 1)
/// at a configurable frame rate. Raises events so the UI layer can request
/// preview refreshes on each frame.
/// </summary>
public sealed class AnimationPlaybackService : INotifyPropertyChanged, IDisposable
{
    private readonly DispatcherTimer _timer;
    private AnimationPlayMode _playMode = AnimationPlayMode.Loop;
    private int _frameCount = 8;
    private double _frameRate = 12; // fps
    private int _currentFrame;
    private bool _isPlaying;
    private int _direction = 1; // for ping-pong

    /// <summary>Fired when the current frame changes and the preview should refresh.</summary>
    public event Action<int, float>? FrameChanged;

    /// <summary>Fired on each timer tick after FrameChanged.</summary>
    public event Action<int, float>? FrameRendered;

    public AnimationPlaybackService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render);
        _timer.Tick += Timer_Tick;
        UpdateInterval();
    }

    // ─── Properties ───

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            _timer.IsEnabled = value;
            if (value && _currentFrame >= _frameCount)
            {
                _currentFrame = 0;
                _direction = 1;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPaused));
        }
    }

    public bool IsPaused => !_isPlaying;

    public int FrameCount
    {
        get => _frameCount;
        set
        {
            if (_frameCount == value) return;
            _frameCount = Math.Clamp(value, 1, 256);
            _currentFrame = Math.Min(_currentFrame, _frameCount - 1);
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentTimeSeconds));
            OnPropertyChanged(nameof(NormalizedTime));
            NotifyFrame();
        }
    }

    public double FrameRate
    {
        get => _frameRate;
        set
        {
            if (Math.Abs(_frameRate - value) < 0.01) return;
            _frameRate = Math.Clamp(value, 1, 60);
            UpdateInterval();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentTimeSeconds));
        }
    }

    public int CurrentFrame
    {
        get => _currentFrame;
        set
        {
            var clamped = Math.Clamp(value, 0, _frameCount - 1);
            if (_currentFrame == clamped) return;
            _currentFrame = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentTimeSeconds));
            OnPropertyChanged(nameof(NormalizedTime));
            NotifyFrame();
        }
    }

    /// <summary>
    /// Loop playback deliberately excludes 1.0 so the first and last frames do not
    /// duplicate the same seamless animation sample.
    /// </summary>
    public float NormalizedTime => _frameCount > 1
        ? _currentFrame / (float)(_playMode == AnimationPlayMode.Loop ? _frameCount : _frameCount - 1)
        : 0f;

    public float CurrentTimeSeconds => (float)(_currentFrame / _frameRate);

    public AnimationPlayMode PlayMode
    {
        get => _playMode;
        set
        {
            if (_playMode == value) return;
            _playMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NormalizedTime));
            NotifyFrame();
        }
    }

    // ─── Playback controls ───

    public void Play()
    {
        if (_currentFrame >= _frameCount - 1)
        {
            _currentFrame = 0;
            _direction = 1;
        }
        IsPlaying = true;
    }

    public void Pause()
    {
        IsPlaying = false;
    }

    public void Stop()
    {
        IsPlaying = false;
        _currentFrame = 0;
        _direction = 1;
        OnPropertyChanged(nameof(CurrentFrame));
        OnPropertyChanged(nameof(NormalizedTime));
        OnPropertyChanged(nameof(CurrentTimeSeconds));
        NotifyFrame();
    }

    public void StepForward()
    {
        var next = _currentFrame + 1;
        if (next >= _frameCount) next = _playMode == AnimationPlayMode.Loop ? 0 : _frameCount - 1;
        CurrentFrame = next;
    }

    public void StepBackward()
    {
        var prev = _currentFrame - 1;
        if (prev < 0) prev = _playMode == AnimationPlayMode.Loop ? _frameCount - 1 : 0;
        CurrentFrame = prev;
    }

    public void GoToFirst() => CurrentFrame = 0;
    public void GoToLast() => CurrentFrame = _frameCount - 1;

    /// <summary>
    /// Moves the playhead to an exact frame.  Scrubbing pauses playback by default
    /// so a dragged timeline does not fight the render timer.
    /// </summary>
    public void SeekFrame(int frame, bool pausePlayback = true)
    {
        if (pausePlayback)
            Pause();
        CurrentFrame = frame;
    }

    /// <summary>Moves the playhead using a normalized [0,1] timeline position.</summary>
    public void SeekNormalized(float normalizedTime, bool pausePlayback = true)
    {
        var t = Math.Clamp(normalizedTime, 0f, 1f);
        var frame = _frameCount <= 1 ? 0 : (int)MathF.Round(t * (_frameCount - 1));
        SeekFrame(frame, pausePlayback);
    }

    // ─── Internals ───

    private void UpdateInterval() => _timer.Interval = TimeSpan.FromSeconds(1.0 / _frameRate);

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_frameCount <= 1)
        {
            _currentFrame = 0;
            if (_playMode == AnimationPlayMode.SingleShot)
                IsPlaying = false;
            NotifyFrame();
            return;
        }

        switch (_playMode)
        {
            case AnimationPlayMode.Loop:
                _currentFrame = (_currentFrame + 1) % _frameCount;
                break;

            case AnimationPlayMode.PingPong:
                _currentFrame += _direction;
                if (_currentFrame >= _frameCount) { _currentFrame = _frameCount - 2; _direction = -1; }
                if (_currentFrame < 0) { _currentFrame = 1; _direction = 1; }
                break;

            case AnimationPlayMode.SingleShot:
                _currentFrame++;
                if (_currentFrame >= _frameCount)
                {
                    _currentFrame = _frameCount - 1;
                    IsPlaying = false;
                }
                break;
        }

        OnPropertyChanged(nameof(CurrentFrame));
        OnPropertyChanged(nameof(NormalizedTime));
        OnPropertyChanged(nameof(CurrentTimeSeconds));
        NotifyFrame();
    }

    private void NotifyFrame()
    {
        var t = NormalizedTime;
        FrameChanged?.Invoke(_currentFrame, t);
        FrameRendered?.Invoke(_currentFrame, t);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
    }

    // ─── INotifyPropertyChanged ───

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new(n));
}
