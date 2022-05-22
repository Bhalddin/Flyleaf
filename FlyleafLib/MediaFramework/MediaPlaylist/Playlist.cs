﻿using FlyleafLib.MediaFramework.MediaContext;
using System;
using System.Collections.ObjectModel;
using System.IO;

using static FlyleafLib.Utils;

namespace FlyleafLib.MediaFramework.MediaPlaylist
{
    public class Playlist : NotifyPropertyChanged
    {
        /// <summary>
        /// Url provided as a demuxer input
        /// </summary>
        public string       Url             { get => _Url;   set => SetUI(ref _Url, value); }
        string _Url;

        /// <summary>
        /// Fallback url provided as a demuxer input
        /// </summary>
        public string       UrlFallback     { get; set; }

        /// <summary>
        /// While the Url can expire or be null DirectUrl can be used as a new input for re-opening
        /// </summary>
        public string       DirectUrl       { get; set; }

        /// <summary>
        /// IOStream provided as a demuxer input
        /// </summary>
        public Stream       IOStream        { get; set; }

        /// <summary>
        /// Playlist's folder base which can be used to save related files
        /// </summary>
        public string       FolderBase      { get; set; }

        /// <summary>
        /// Playlist's title
        /// </summary>
        public string       Title           { get => _Title; set => SetUI(ref _Title, value); }
        string _Title;

        public int          ExpectingItems  { get => _ExpectingItems; set => SetUI(ref _ExpectingItems, value); }
        int _ExpectingItems;

        public bool         Completed       { get; set; }

        /// <summary>
        /// Playlist's opened/selected item
        /// </summary>
        public PlaylistItem Selected        { get => _Selected; internal set => SetUI(ref _Selected, value); }
        PlaylistItem _Selected;

        /// <summary>
        /// Type of the provided input (such as File, UNC, Torrent, Web, etc.)
        /// </summary>
        public InputType    InputType       { get; set; }

        public ObservableCollection<PlaylistItem>
                            Items           { get; set; } = new ObservableCollection<PlaylistItem>();
        object lockItems = new object();

        long openCounter;
        //long openItemCounter;
        internal DecoderContext decoder;
        LogHandler Log;

        public Playlist(int uniqueId)
        {
            Log = new LogHandler($"[#{uniqueId}] [Playlist] ");
            UI(() => System.Windows.Data.BindingOperations.EnableCollectionSynchronization(Items, lockItems));
        }

        public void Reset()
        {
            openCounter = decoder.OpenCounter;

            lock (lockItems)
                Items.Clear();

            bool noupdate = _Url == null && _Title == null && _Selected == null;

            _Url        = null;
            _Title      = null;
            _Selected   = null;
            IOStream    = null;
            FolderBase  = null;
            Completed   = false;
            ExpectingItems = 0;

            InputType   = InputType.Unknown;

            if (!noupdate)
                UI(() =>
                {
                    Raise(nameof(Url));
                    Raise(nameof(Title));
                    Raise(nameof(Selected));
                });
        }

        public void AddItem(PlaylistItem item, string pluginName, object tag = null)
        {
            if (openCounter != decoder.OpenCounter)
            {
                Log.Debug("AddItem Cancelled");
                return;
            }

            lock (lockItems)
            {
                Items.Add(item);
                Items[Items.Count - 1].Index = Items.Count - 1;

                if (tag != null)
                    item.AddTag(tag, pluginName);
            };

            decoder.ScrapeItem(item);

            UI(() => 
            {
                System.Windows.Data.BindingOperations.EnableCollectionSynchronization(item.ExternalAudioStreams, item.lockExternalStreams);
                System.Windows.Data.BindingOperations.EnableCollectionSynchronization(item.ExternalVideoStreams, item.lockExternalStreams);
                System.Windows.Data.BindingOperations.EnableCollectionSynchronization(item.ExternalSubtitlesStreams, item.lockExternalStreams);
            });
        }
    }
}