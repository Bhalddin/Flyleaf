﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

using FlyleafLib.Controls;
using FlyleafLib.MediaFramework.MediaContext;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaDemuxer;

using static FlyleafLib.Utils;
using static FlyleafLib.Logger;

namespace FlyleafLib.MediaPlayer;

public unsafe partial class Player : NotifyPropertyChanged, IDisposable
{
    #region Properties
    public bool                 IsDisposed          { get; private set; }

    /// <summary>
    /// FlyleafHost (WinForms, WPF or WinUI)
    /// </summary>
    public IHostPlayer          Host                { get => _Host; set => Set(ref _Host, value); }
    IHostPlayer _Host;

    /// <summary>
    /// Player's Activity (Idle/Active/FullActive)
    /// </summary>
    public Activity             Activity            { get; private set; }

    /// <summary>
    /// Helper ICommands for WPF MVVM
    /// </summary>
    public Commands             Commands            { get; private set; }

    public Playlist             Playlist            => decoder.Playlist;

    /// <summary>
    /// Player's Audio (In/Out)
    /// </summary>
    public Audio                Audio               { get; private set; }

    /// <summary>
    /// Player's Video
    /// </summary>
    public Video                Video               { get; private set; }

    /// <summary>
    /// Player's Subtitles
    /// </summary>
    public Subtitles            Subtitles           { get; private set; }

    /// <summary>
    /// Player's Renderer
    /// (Normally you should not access this directly)
    /// </summary>
    public Renderer             renderer            => decoder.VideoDecoder.Renderer;

    /// <summary>
    /// Player's Decoder Context
    /// (Normally you should not access this directly)
    /// </summary>
    public DecoderContext       decoder             { get; private set; }

    /// <summary>
    /// Audio Decoder
    /// (Normally you should not access this directly)
    /// </summary>
    public AudioDecoder         AudioDecoder        => decoder.AudioDecoder;

    /// <summary>
    /// Video Decoder
    /// (Normally you should not access this directly)
    /// </summary>
    public VideoDecoder         VideoDecoder        => decoder.VideoDecoder;

    /// <summary>
    /// Subtitles Decoder
    /// (Normally you should not access this directly)
    /// </summary>
    public SubtitlesDecoder     SubtitlesDecoder    => decoder.SubtitlesDecoder;

    /// <summary>
    /// Main Demuxer (if video disabled or audio only can be AudioDemuxer instead of VideoDemuxer)
    /// (Normally you should not access this directly)
    /// </summary>
    public Demuxer              MainDemuxer         => decoder.MainDemuxer;

    /// <summary>
    /// Audio Demuxer
    /// (Normally you should not access this directly)
    /// </summary>
    public Demuxer              AudioDemuxer        => decoder.AudioDemuxer;

    /// <summary>
    /// Video Demuxer
    /// (Normally you should not access this directly)
    /// </summary>
    public Demuxer              VideoDemuxer        => decoder.VideoDemuxer;

    /// <summary>
    /// Subtitles Demuxer
    /// (Normally you should not access this directly)
    /// </summary>
    public Demuxer              SubtitlesDemuxer    => decoder.SubtitlesDemuxer;


    /// <summary>
    /// Player's incremental unique id
    /// </summary>
    public int          PlayerId            { get; private set; }

    /// <summary>
    /// Player's configuration (set once in the constructor)
    /// </summary>
    public Config       Config              { get; protected set; }

    /// <summary>
    /// Player's Status
    /// </summary>
    public Status       Status              { get => status;            private set => Set(ref _Status, value); }
    Status _Status = Status.Stopped, status = Status.Stopped;
    public bool         IsPlaying           => status == Status.Playing;

    /// <summary>
    /// Whether the player's status is capable of accepting playback commands
    /// </summary>
    public bool         CanPlay             { get => canPlay;           internal set => Set(ref _CanPlay, value); }
    internal bool _CanPlay, canPlay;

    /// <summary>
    /// The list of chapters
    /// </summary>
    public List<Demuxer.Chapter> 
                        Chapters            => VideoDemuxer?.Chapters;

    /// <summary>
    /// Player's current time or user's current seek time (uses backward/forward direction based on previous time or accurate seek based on Config.Player.SeekAccurate)
    /// </summary>
    public long         CurTime             { get => curTime;           set { if (Config.Player.SeekAccurate) SeekAccurate((int) (value/10000)); else Seek((int) (value/10000), value > curTime); } }
    long _CurTime, curTime;
    internal void UpdateCurTime()
    {
        if (MainDemuxer == null || !seeks.IsEmpty)
            return;

        if (MainDemuxer.IsHLSLive)
        {
            curTime  = MainDemuxer.CurTime;
            duration = MainDemuxer.Duration;
            Duration = Duration;
        }

        Set(ref _CurTime, curTime, true, nameof(CurTime));

        UpdateBufferedDuration();
    }
    internal void UpdateBufferedDuration()
    {
        if (_BufferedDuration != MainDemuxer.BufferedDuration)
        {
            _BufferedDuration = MainDemuxer.BufferedDuration;
            Raise(nameof(BufferedDuration));
        }
    }

    /// <summary>
    /// Input's duration
    /// </summary>
    public long         Duration            { get => duration;          private set => Set(ref _Duration, value); }
    long _Duration, duration;

    /// <summary>
    /// The current buffered duration in the demuxer
    /// </summary>
    public long         BufferedDuration    { get => MainDemuxer == null ? 0 : MainDemuxer.BufferedDuration;
                                                                        internal set => Set(ref _BufferedDuration, value); }
    long _BufferedDuration;

    /// <summary>
    /// Whether the input is live (duration might not be 0 on live sessions to allow live seek, eg. hls)
    /// </summary>
    public bool         IsLive              { get => isLive;            private set => Set(ref _IsLive, value); }
    bool _IsLive, isLive;

    ///// <summary>
    ///// Total bitrate (Kbps)
    ///// </summary>
    public double       BitRate             { get => bitRate;           internal set => Set(ref _BitRate, value); }
    internal double _BitRate, bitRate;

    /// <summary>
    /// Whether the player is recording
    /// </summary>
    public bool         IsRecording
    {
        get => decoder != null && decoder.IsRecording;
        private set { if (_IsRecording == value) return; _IsRecording = value; UI(() => Set(ref _IsRecording, value, false)); }
    }
    bool _IsRecording;

    /// <summary>
    /// Pan X Offset to change the X location
    /// </summary>
    public int          PanXOffset          { get => renderer.PanXOffset; set { renderer.PanXOffset = value; Raise(nameof(PanXOffset)); } }

    /// <summary>
    /// Pan Y Offset to change the Y location
    /// </summary>
    public int          PanYOffset          { get => renderer.PanYOffset; set { renderer.PanYOffset = value; Raise(nameof(PanYOffset)); } }

    /// <summary>
    /// Playback's speed (x1 - x4)
    /// </summary>
    public double       Speed {
        get => _Speed; 
        set
        {
            double newValue = value;
            if (value < 0.5)
                newValue = 0.5;
            else if (value > 16)
                newValue = 16;

            if (newValue == _Speed || (newValue > 1 && ReversePlayback))
                return;
            
            AudioDecoder.Speed      = newValue;
            VideoDecoder.Speed      = newValue;
            _Speed                  = newValue;
            decoder.RequiresResync  = true;
            requiresBuffering       = true;
            Subtitles.subsText      = "";
            //AudioDecoder.DisposeFrames();

            UI(() =>
            {
                Subtitles.SubsText = Subtitles.SubsText;
                Raise(nameof(Speed));
            });
        }
    }
    double _Speed = 1;

    /// <summary>
    /// Custom pan zoom percentage (Note: Prefer ZoomIn/ZoomOut and Config.Player.ZoomOffset instead of this)
    /// </summary>
    public int          Zoom
    {
        get => renderer.Zoom;
        set { _ZoomTimes = 0; if (renderer.Zoom == value) return; renderer.Zoom = value; UI(() => Set(ref _Zoom, renderer.Zoom, false)); }
    }
    int _Zoom = 100;

    public int ZoomTimes { get => _ZoomTimes; private set => SetZoom(value); }
    int _ZoomTimes = 0;

    private void SetZoom(int value, bool refresh = true)
    {
        _ZoomTimes = value;
        int zoom = 100 + (Config.Player.ZoomOffset * _ZoomTimes);

        if (_Zoom == zoom)
            return;

        if (zoom < 1)
        {
            _ZoomTimes++;
            zoom = 1;
        }

        _Zoom = zoom;
        renderer.SetZoom(zoom, refresh);

        UIInvokeIfRequired(() =>
        {
            Raise(nameof(Zoom));
            Raise(nameof(_Zoom));
        });
    }

    /// <summary>
    /// Pan rotation angle (for D3D11 VP allowed values are 0, 90, 180, 270 only)
    /// </summary>
    public int     Rotation            { get => _Rotation; 
        set
        {
            renderer.Rotation = value;
            Set(ref _Rotation, renderer.Rotation);
        }
    }
    int _Rotation;

    /// <summary>
    /// Whether to use reverse playback mode
    /// </summary>
    public bool         ReversePlayback
    {
        get => _ReversePlayback;

        set
        {
            if (_ReversePlayback == value)
                return;

            _ReversePlayback = value;
            UI(() => Set(ref _ReversePlayback, value, false));

            if (!Video.IsOpened || !CanPlay | IsLive)
                return;

            lock (lockActions)
            {
                bool shouldPlay = IsPlaying;
                Pause();
                sFrame = null;
                Subtitles.subsText = "";
                if (Subtitles._SubsText != "")
                    UI(() => Subtitles.SubsText = Subtitles.SubsText);
                decoder.StopThreads();
                decoder.Flush();

                if (value)
                {
                    Speed = 1;
                    VideoDemuxer.EnableReversePlayback(CurTime);
                }
                else
                {
                    VideoDemuxer.DisableReversePlayback();

                    if (Status == Status.Ended)
                    {
                        status = Status.Paused;
                        UI(() => Status = Status);
                    }

                    var vFrame = VideoDecoder.GetFrame(VideoDecoder.GetFrameNumber(CurTime));
                    VideoDecoder.DisposeFrame(vFrame);
                    vFrame = null;
                    decoder.RequiresResync = true;
                }

                reversePlaybackResync = false;
                if (shouldPlay) Play();
            }
        }
    }
    bool _ReversePlayback;

    public object       Tag                 { get => tag; set => Set(ref  tag, value); }
    object tag;

    public string       LastError           { get => lastError; set => Set(ref _LastError, value); } 
    string _LastError, lastError;
    bool decoderHasEnded => decoder != null && (VideoDecoder.Status == MediaFramework.Status.Ended || (VideoDecoder.Disposed && AudioDecoder.Status == MediaFramework.Status.Ended));
    #endregion

    #region Properties Internal
    readonly object lockActions  = new();
    readonly object lockSubtitles= new();

    bool taskSeekRuns;
    bool taskPlayRuns;
    bool taskOpenAsyncRuns;

    readonly ConcurrentStack<SeekData>   seeks      = new();
    readonly ConcurrentQueue<Action>     UIActions  = new();

    internal AudioFrame     aFrame;
    internal VideoFrame     vFrame;
    internal SubtitlesFrame sFrame, sFramePrev;
    internal PlayerStats    stats = new();
    internal LogHandler     Log;

    internal bool requiresBuffering;
    bool reversePlaybackResync;

    bool isVideoSwitch;
    bool isAudioSwitch;
    bool isSubsSwitch;
    #endregion

	public Player(Config config = null)
    {
        if (config != null)
        {
            if (config.Player.player != null)
                throw new Exception("Player's configuration is already assigned to another player");

            Config = config;
        }
        else
            Config = new Config();

        PlayerId = GetUniqueId();
        Log = new LogHandler(("[#" + PlayerId + "]").PadRight(8, ' ') + " [Player        ] ");
        Log.Debug($"Creating Player (Usage = {Config.Player.Usage})");

        Activity    = new Activity(this);
        Audio       = new Audio(this);
        Video       = new Video(this);
        Subtitles   = new Subtitles(this);
        Commands    = new Commands(this);

        Config.SetPlayer(this);
        
        if (Config.Player.Usage == Usage.Audio)
        {
            Config.Video.Enabled = false;
            Config.Subtitles.Enabled = false;
        }

        Engine.AddPlayer(this);
        decoder = new DecoderContext(Config, PlayerId) { Tag = this };

        //decoder.OpenPlaylistItemCompleted              += Decoder_OnOpenExternalSubtitlesStreamCompleted;
        
        decoder.OpenAudioStreamCompleted               += Decoder_OpenAudioStreamCompleted;
        decoder.OpenVideoStreamCompleted               += Decoder_OpenVideoStreamCompleted;
        decoder.OpenSubtitlesStreamCompleted           += Decoder_OpenSubtitlesStreamCompleted;

        decoder.OpenExternalAudioStreamCompleted       += Decoder_OpenExternalAudioStreamCompleted;
        decoder.OpenExternalVideoStreamCompleted       += Decoder_OpenExternalVideoStreamCompleted;
        decoder.OpenExternalSubtitlesStreamCompleted   += Decoder_OpenExternalSubtitlesStreamCompleted;

        AudioDecoder.CodecChanged = Decoder_AudioCodecChanged;
        VideoDecoder.CodecChanged = Decoder_VideoCodecChanged;
        decoder.RecordingCompleted += (o, e) => { IsRecording = false; };

        status = Status.Stopped;
        Reset();
        Log.Debug("Created");
    }

    /// <summary>
    /// Disposes the Player and de-assigns it from FlyleafHost
    /// </summary>
    public void Dispose() => Engine.DisposePlayer(this);
    internal void DisposeInternal()
    {
        lock (lockActions)
        {
            if (IsDisposed)
                return;

            try
            {
                Initialize();
                Audio.Dispose(); 
                decoder.Dispose();
                Host?.Player_Disposed();
                Log.Info("Disposed");
            } catch (Exception e) { Log.Warn($"Disposed ({e.Message})"); }

            IsDisposed = true;
        }
    }
    internal void RefreshMaxVideoFrames()
    {
        lock (lockActions)
        {
            if (!Video.isOpened)
                return;

            bool wasPlaying = IsPlaying;
            Pause();
            VideoDecoder.RefreshMaxVideoFrames();
            ReSync(decoder.VideoStream, (int) (CurTime / 10000), true);

            if (wasPlaying)
                Play();
        }
    }

    private void ResetMe()
    {
        canPlay     = false;
        bitRate     = 0;
        curTime     = 0;
        duration    = 0;
        isLive      = false;
        lastError   = null;

        UIAdd(() =>
        {
            BitRate     = BitRate;
            Duration    = Duration;
            IsLive      = IsLive;
            Status      = Status;
            CanPlay     = CanPlay;
            LastError   = LastError;
            BufferedDuration = 0;
            Set(ref _CurTime, curTime, true, nameof(CurTime));
        });
    }
    private void Reset()
    {
        ResetMe();
        Video.Reset();
        Audio.Reset();
        Subtitles.Reset();
        UIAll();
    }
    private void Initialize(Status status = Status.Stopped, bool andDecoder = true, bool isSwitch = false)
    {
        if (CanDebug) Log.Debug($"Initializing");

        lock (lockActions) // Required in case of OpenAsync and Stop requests
        {
            try
            {
                Engine.TimeBeginPeriod1();

                this.status = status;
                canPlay = false;
                isVideoSwitch = false;
                seeks.Clear();

                while (taskPlayRuns || taskSeekRuns) Thread.Sleep(5);

                if (andDecoder)
                {
                    if (isSwitch)
                        decoder.InitializeSwitch();
                    else
                        decoder.Initialize();
                }

                Reset();
                VideoDemuxer.DisableReversePlayback();
                ReversePlayback = false;

                if (CanDebug) Log.Debug($"Initialized");

            } catch (Exception e)
            {
                Log.Error($"Initialize() Error: {e.Message}");

            } finally
            {
                Engine.TimeEndPeriod1();
            }
        }
    }

    internal void UIAdd(Action action) => UIActions.Enqueue(action);
    internal void UIAll()
    {
        while (!UIActions.IsEmpty)
            if (UIActions.TryDequeue(out var action))
                UI(action);
    }

    public override bool Equals(object obj)
        => obj == null || !(obj is Player) ? false : ((Player)obj).PlayerId == PlayerId;
    public override int GetHashCode() => PlayerId.GetHashCode();
}

public enum Status
{
    Opening,
    Failed,
    Stopped,
    Paused,
    Playing,
    Ended
}
public enum Usage
{
    AVS,
    Audio
}