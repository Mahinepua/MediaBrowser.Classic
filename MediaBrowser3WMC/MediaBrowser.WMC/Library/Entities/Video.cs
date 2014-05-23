﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.Persistance;
using MediaBrowser.Library.Filesystem;
using MediaBrowser.Library.Entities.Attributes;
using MediaBrowser.Library.Streaming;
using MediaBrowser.LibraryManagement;
using MediaBrowser.Library.Extensions;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Library.Entities {
    public class Video : Media {

        [Persist]
        public string VideoFormat { get; set; }

        public override bool AssignFromItem(BaseItem item) {
            bool changed = this.MediaType != ((Video)item).MediaType;
            this.MediaType = ((Video)item).MediaType;
            return changed | base.AssignFromItem(item);
        }

        private List<string> _fileCache; 
        public override IEnumerable<string> Files
        {
            get { return _fileCache ?? (_fileCache = VideoFiles.ToList()); }
        }
        
        public override PlaybackStatus PlaybackStatus {
            get {

                if (playbackStatus != null) return playbackStatus;

                //playbackStatus = Kernel.Instance.ItemRepository.RetrievePlayState(this.Id);
                if (playbackStatus == null) {
                    playbackStatus = PlaybackStatusFactory.Instance.Create(Id); // initialise an empty version that items can bind to
                    if (DateCreated <= Config.Instance.AssumeWatchedBefore || !IsPlayable)
                        playbackStatus.PlayCount = 1;
                    //Kernel.Instance.SavePlayState(this, playbackStatus);  //removed this so we don't create files until we actually play something -ebr
                }
                return playbackStatus;
            }

            set { base.PlaybackStatus = value; }
        }

        public override bool PassesFilter(Query.FilterProperties filters)
        {
            return (playbackStatus.WasPlayed == filters.IsUnWatched) && base.PassesFilter(filters);
        }

        public override int RunTime
        {
            get
            {
                return RunningTime ?? 0;
            }
        }

        public virtual IEnumerable<string> VideoFiles {
            get
            {
                if (Path == System.IO.Path.GetPathRoot(Path) || Directory.Exists(System.IO.Path.GetDirectoryName(Path ?? "") ?? ""))
                {
                    yield return Path;
                }
                else
                {
                    Logger.ReportInfo("Unable to access {0}.  Will try to stream.", Path);
                    // build based on WMC profile
                    var info = MediaSources != null && MediaSources.Any() ? new StreamBuilder().BuildVideoItem(new VideoOptions {DeviceId = Kernel.ApiClient.DeviceId, ItemId = ApiId, MediaSources = MediaSources, MaxBitrate = 10000000, Profile = new WindowsMediaCenterProfile()}) : null;
                    yield return info != null ? info.ToUrl(Kernel.ApiClient.ApiUrl) : Kernel.ApiClient.GetVideoStreamUrl(new VideoStreamOptions
                                                                                              {
                                                                                                  ItemId = ApiId,
                                                                                                  OutputFileExtension = ".wmv",
                                                                                                  MaxWidth = 1280,
                                                                                                  VideoBitRate = 5000000,
                                                                                                  AudioBitRate = 128000,
                                                                                                  MaxAudioChannels = 2,
                                                                                                  AudioStreamIndex = FindAudioStream(Kernel.CurrentUser.Dto.Configuration.AudioLanguagePreference)
                                                                                              });
                }
            }
        }

        protected static List<string> StreamableCodecs = new List<string> {"DTS", "DTS-HD MA", "DTS Express", "AC3", "MP3"}; 

        /// <summary>
        /// Find the first streamable audio stream for the specified language
        /// </summary>
        /// <returns></returns>
        protected int FindAudioStream(string lang = "")
        {
            if (string.IsNullOrEmpty(lang)) lang = "eng";
            if (MediaSources == null || !MediaSources.Any()) return 0;

            Logging.Logger.ReportVerbose("Looking for audio stream in {0}", lang);
            MediaStream stream = null;
            foreach (var codec in StreamableCodecs)
            {
                stream = MediaSources.First().MediaStreams.OrderBy(s => s.Index).FirstOrDefault(s => s.Type == MediaStreamType.Audio && (s.Language == null || s.Language.Equals(lang, StringComparison.OrdinalIgnoreCase)) 
                    && s.Codec.Equals(codec,StringComparison.OrdinalIgnoreCase));
                if (stream != null) break;
                
            }
            Logging.Logger.ReportVerbose("Requesting audio stream #{0}", stream != null ? stream.Index : 0);
            return stream != null ? stream.Index : 0;
        }

        /// <summary>
        /// Returns true if the Video is from ripped media (DVD , BluRay , HDDVD or ISO)
        /// </summary>
        public bool ContainsRippedMedia {
            get {
                return IsRippedMedia(MediaType);
            }
        }

        public IEnumerable<string> IsoFiles
        {
            get
            {
                if (MediaLocation is IFolderMediaLocation)
                {
                    return Helper.GetIsoFiles(Path);
                }

                return new string[] { Path };
            }
        }

        public bool ContainsTrailers { get; set; }
        private IEnumerable<string> _trailerFiles;

        public IEnumerable<string> TrailerFiles
        {
            get { return _trailerFiles ?? (_trailerFiles = Kernel.ApiClient.GetLocalTrailers(Kernel.CurrentUser.Id, this.Id.ToString()).Select(t => t.Path)); }
            set { _trailerFiles = value; }
        }

        public static bool IsRippedMedia(MediaType type)
        {
            return type == MediaType.BluRay ||
               type == MediaType.DVD ||
               type == MediaType.ISO ||
               type == MediaType.HDDVD;
        }

    }
}
