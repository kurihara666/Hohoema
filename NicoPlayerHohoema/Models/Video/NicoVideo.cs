﻿using FFmpegInterop;
using Mntone.Nico2;
using Mntone.Nico2.Videos.Comment;
using Mntone.Nico2.Videos.Thumbnail;
using Mntone.Nico2.Videos.WatchAPI;
using NicoPlayerHohoema.Models.Db;
using NicoPlayerHohoema.Util;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace NicoPlayerHohoema.Models
{
	public class NicoVideo : BindableBase
	{
		private CommentResponse _CachedCommentResponse;
		private NicoVideoQuality _VisitedPageType = NicoVideoQuality.Low;

		internal WatchApiResponse CachedWatchApiResponse { get; private set; }

        AsyncLock _InitializeLock = new AsyncLock();
		bool _IsInitialized = false;
		bool _thumbnailInitialized = false;

		public NicoVideo(HohoemaApp app, string rawVideoid, NiconicoMediaManager manager)
		{
			HohoemaApp = app;
			RawVideoId = rawVideoid;
			_NiconicoMediaManager = manager;

			_InterfaceByQuality = new Dictionary<NicoVideoQuality, DividedQualityNicoVideo>();
            QualityDividedVideos = new ReadOnlyObservableCollection<DividedQualityNicoVideo>(_QualityDividedVideos);
        }

        private async void _NiconicoMediaManager_VideoCacheStateChanged(object sender, NicoVideoCacheRequest request, NicoVideoCacheState state)
        {
            if (this.RawVideoId == request.RawVideoId)
            {
                var divided = GetDividedQualityNicoVideo(request.Quality);

//                divided.CacheState = state;

                Debug.WriteLine($"{request.RawVideoId}<{request.Quality}>: {state.ToString()}");

                // update Cached time
                await divided.GetCacheFile();

                if (state != NicoVideoCacheState.NotCacheRequested)
                {
                    var requestAt = request.RequestAt;
                    foreach (var div in GetAllQuality())
                    {
                        if (div.VideoFileCreatedAt > requestAt)
                        {
                            requestAt = div.VideoFileCreatedAt;
                        }
                    }

                    CachedAt = requestAt;
                }
            }
        }

        public async Task Initialize()
		{
            using (var releaser = await _InitializeLock.LockAsync())
            {
                if (_IsInitialized) { return; }

                if (HohoemaApp.ServiceStatus >= HohoemaAppServiceLevel.OnlineWithoutLoggedIn)
                {
                    Debug.WriteLine("start initialize : " + RawVideoId);

                    await UpdateWithThumbnail();

                    _IsInitialized = true;
                }
                else if(!_thumbnailInitialized && HohoemaApp.ServiceStatus == HohoemaAppServiceLevel.Offline)
                {
                    await FillVideoInfoFromDb();
                }
            }
        }


        public DividedQualityNicoVideo GetDividedQualityNicoVideo(NicoVideoQuality quality)
        {
            DividedQualityNicoVideo qualityDividedVideo = null;

            if (_InterfaceByQuality.ContainsKey(quality))
            {
                qualityDividedVideo = _InterfaceByQuality[quality];
            }
            else 
            {
                switch (quality)
                {
                    case NicoVideoQuality.Original:
                        qualityDividedVideo = new OriginalQualityNicoVideo(this, _NiconicoMediaManager);
                        break;
                    case NicoVideoQuality.Low:
                        qualityDividedVideo = new LowQualityNicoVideo(this, _NiconicoMediaManager);
                        break;
                    default:
                        throw new NotSupportedException(quality.ToString());
                }

                _InterfaceByQuality.Add(quality, qualityDividedVideo);
                _QualityDividedVideos.Add(qualityDividedVideo);
            }

            return qualityDividedVideo;
        }


        public IEnumerable<DividedQualityNicoVideo> GetAllQuality()
        {
            return _InterfaceByQuality.Values;
        }



        // コメントのキャッシュまたはオンラインからの取得と更新
        public async Task<List<Chat>> GetComments(bool requierLatest = false)
		{
            CommentResponse commentRes = null;
            if (requierLatest || _CachedCommentResponse == null)
            {
                if (HohoemaApp.ServiceStatus == HohoemaAppServiceLevel.LoggedIn)
                {
                    try
                    {
                        commentRes = await ConnectionRetryUtil.TaskWithRetry(async () =>
                        {
                            return await this.HohoemaApp.NiconicoContext.Video
                                .GetCommentAsync(CachedWatchApiResponse);
                        });

                        if (commentRes != null)
                        {
                            _CachedCommentResponse = commentRes;
                        }
                    }
                    catch { }
                }
            }

            if (commentRes == null && _CachedCommentResponse != null)
            {
                commentRes = _CachedCommentResponse;
            }

            if (commentRes == null)
			{
                var j = CommentDb.Get(RawVideoId);
                return j.GetComments();
			}
            else
            {
                return commentRes?.Chat;
            }
        }

		

		private async Task UpdateWithThumbnail()
		{
			if (_thumbnailInitialized) { return; }

			// 動画のサムネイル情報にアクセスさせて、アプリ内部DBを更新
			try
			{
				var res = await HohoemaApp.ContentFinder.GetThumbnailResponse(RawVideoId);

				this.VideoId = res.Id;
				this.VideoLength = res.Length;
				this.SizeLow = (uint)res.SizeLow;
				this.SizeHigh = (uint)res.SizeHigh;
				this.Title = res.Title;
				this.VideoOwnerId = res.UserId;
				this.ContentType = res.MovieType;
				this.PostedAt = res.PostedAt.DateTime;
				this.Tags = res.Tags.Value.ToList();
				this.ViewCount = res.ViewCount;
				this.MylistCount = res.MylistCount;
				this.CommentCount = res.CommentCount;
				this.ThumbnailUrl = res.ThumbnailUrl.AbsoluteUri;
			}
			catch (Exception e) when (e.Message.Contains("delete"))
			{
				await DeletedTeardown();
			}
			catch (Exception e) when (e.Message.Contains("community"))
			{
				this.IsCommunity = true;
			}

            _thumbnailInitialized = true;

            if (!this.IsDeleted && GetAllQuality().Any(x => x.IsCacheRequested))
			{
				// キャッシュ情報を最新の状態に更新
				await OnCacheRequested();
			}
		}



		// 動画情報のキャッシュまたはオンラインからの取得と更新

		public Task VisitWatchPage(bool forceLowQuality = false)
		{
			return GetWatchApiResponse(forceLowQuality);
		}

		private async Task<WatchApiResponse> GetWatchApiResponse(bool forceLoqQuality = false)
		{
            if (!HohoemaApp.IsLoggedIn) { return null; }


			WatchApiResponse watchApiRes = null;

			try
			{
				watchApiRes = await HohoemaApp.ContentFinder.GetWatchApiResponse(RawVideoId, forceLoqQuality, HarmfulContentReactionType);
			}
			catch (AggregateException ea) when (ea.Flatten().InnerExceptions.Any(e => e is ContentZoningException))
			{
				IsBlockedHarmfulVideo = true;
			}
			catch (ContentZoningException)
			{
				IsBlockedHarmfulVideo = true;
			}
			catch { }

			if (!forceLoqQuality && watchApiRes != null)
			{
				NowLowQualityOnly = watchApiRes.VideoUrl.AbsoluteUri.EndsWith("low");
			}

			_VisitedPageType = watchApiRes.VideoUrl.AbsoluteUri.EndsWith("low") ? NicoVideoQuality.Low : NicoVideoQuality.Original;
			
			if (watchApiRes != null)
			{
				CachedWatchApiResponse = watchApiRes;

				VideoUrl = watchApiRes.VideoUrl;

				ProtocolType = MediaProtocolTypeHelper.ParseMediaProtocolType(watchApiRes.VideoUrl);

				DescriptionWithHtml = watchApiRes.videoDetail.description;
				ThreadId = watchApiRes.ThreadId.ToString();
				PrivateReasonType = watchApiRes.PrivateReason;
                VideoLength = watchApiRes.Length;

				if (!_thumbnailInitialized)
				{
					RawVideoId = watchApiRes.videoDetail.id;
					await UpdateWithThumbnail();
				}
				IsCommunity = watchApiRes.flashvars.is_community_video == "1";
				

				// TODO: 
//				Tags = watchApiRes.videoDetail.tagList.Select(x => new Tag()
//				{
					
//				}).ToList();

				if (watchApiRes.UploaderInfo != null)
				{
					VideoOwnerId = uint.Parse(watchApiRes.UploaderInfo.id);
				}


				this.IsDeleted = watchApiRes.IsDeleted;
				if (IsDeleted)
				{
					await DeletedTeardown();
				}
			}

			return watchApiRes;
		}




		public bool CanGetVideoStream()
		{
			return ProtocolType == MediaProtocolType.RTSPoverHTTP;
		}

		/// <summary>
		/// 動画ストリームの取得します
		/// 他にダウンロードされているアイテムは強制的に一時停止し、再生終了後に再開されます
		/// </summary>
		public async Task StartPlay(NicoVideoQuality quality, TimeSpan? initialPosition = null)
		{
			IfVideoDeletedThrowException();

            // キャッシュ済みの場合は
            var divided = GetDividedQualityNicoVideo(quality);
			if (divided.HasCache)
			{
				var file = await divided.GetCacheFile();
				NicoVideoCachedStream = await file.OpenReadAsync();

                await OnCacheRequested();
            }
			else if (ProtocolType == MediaProtocolType.RTSPoverHTTP)
			{				
				if (Util.InternetConnection.IsInternet())
				{
					var size = divided.VideoSize;
					NicoVideoCachedStream = await HttpSequencialAccessStream.CreateAsync(
						HohoemaApp.NiconicoContext.HttpClient
						, VideoUrl
						);					
				}
			}

			if (NicoVideoCachedStream == null)
			{
				throw new NotSupportedException();
			}

            if (initialPosition == null)
            {
                initialPosition = TimeSpan.Zero;
            }

            MediaSource mediaSource = null;
            if (ContentType == MovieType.Mp4)
            {
                string contentType = null;

                if (NicoVideoCachedStream is Util.HttpRandomAccessStream)
                {
                    contentType = (NicoVideoCachedStream as Util.HttpRandomAccessStream).ContentType;
                }
                else if (NicoVideoCachedStream is Util.HttpSequencialAccessStream)
                {
                    contentType = (NicoVideoCachedStream as Util.HttpSequencialAccessStream).ContentType;
                }
                else if (NicoVideoCachedStream is FileRandomAccessStream)
                {
                    contentType = "video/mp4";
                }

                if (contentType == null) { throw new Exception("unknown movie content type"); }

                mediaSource = MediaSource.CreateFromStream(NicoVideoCachedStream, contentType);
            }
            else
            {
                var mss = FFmpegInteropMSS.CreateFFmpegInteropMSSFromStream(NicoVideoCachedStream, false, true);

                if (mss != null)
                {
                    var realMss = mss.GetMediaStreamSource();
                    realMss.SetBufferedRange(TimeSpan.Zero, TimeSpan.Zero);
                    mediaSource = MediaSource.CreateFromMediaStreamSource(realMss);
                }
                else
                {
                    throw new NotSupportedException();
                }
            }

            if (mediaSource != null)
            {
                HohoemaApp.MediaPlayer.Source = mediaSource;
                HohoemaApp.MediaPlayer.PlaybackSession.Position = initialPosition.Value;

                var smtc = HohoemaApp.MediaPlayer.SystemMediaTransportControls;
                
                smtc.DisplayUpdater.Type = Windows.Media.MediaPlaybackType.Video;
                smtc.DisplayUpdater.VideoProperties.Title = Title ?? "Hohoema";
                if (ThumbnailUrl != null)
                {
                    smtc.DisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(this.ThumbnailUrl));
                }
                smtc.IsPlayEnabled = true;
                smtc.IsPauseEnabled = true;
                smtc.DisplayUpdater.Update();
                
                divided.OnPlayStarted();
            }

        }


        internal async Task<Uri> GetVideoUrl(NicoVideoQuality quality)
        {
            var divided = GetDividedQualityNicoVideo(quality);

            if (divided.IsAvailable)
            {
                await SetupWatchPageVisit(quality);

                if (quality == NicoVideoQuality.Original && NowLowQualityOnly)
                {
                    return null;
                }

                return CachedWatchApiResponse.VideoUrl;
            }
            else
            {
                return null;
            }
        }

        internal async Task SetupWatchPageVisit(NicoVideoQuality quality)
		{
			WatchApiResponse res;
			if (quality == NicoVideoQuality.Original)
			{
				if (OriginalQuality.IsCached)
				{
					res = await GetWatchApiResponse();
				}
				// オリジナル画質の視聴ページにアクセスしたWacthApiResponseを取得する
				else if (CachedWatchApiResponse != null
					&& _VisitedPageType == NicoVideoQuality.Original
					)
				{
					// すでに
					res = CachedWatchApiResponse;
				}
				else
				{
					res = await GetWatchApiResponse();
				}

				// ないです				
				if (_VisitedPageType == NicoVideoQuality.Low)
				{
					throw new Exception("cant download original quality video.");
				}
			}
			else
			{
				// 低画質動画キャッシュ済みの場合
				if (LowQuality.IsCached)
				{
					res = await GetWatchApiResponse();
				}
				// 低画質の視聴ページへアクセスしたWatchApiResponseを取得する
				else if (CachedWatchApiResponse != null
					&& _VisitedPageType == NicoVideoQuality.Low
					)
				{
					// すでに
					res = CachedWatchApiResponse;
				}
				else
				{
					// まだなので、低画質を指定してアクセスする
					res = await GetWatchApiResponse( forceLoqQuality:true );
				}
			}

			if (res == null)
			{
				throw new Exception("");
			}


            // キャッシュリクエストされている場合このタイミングでコメントを取得
            var divided = GetDividedQualityNicoVideo(quality);
			if (divided.IsCacheRequested)
			{
				await OnCacheRequested();
//				var commentRes = await GetCommentResponse();
//				CommentDb.AddOrUpdate(RawVideoId, commentRes);
			}
		}
		
		public void StopPlay()
		{
            HohoemaApp.MediaPlayer.Pause();
            HohoemaApp.MediaPlayer.Source = null;

			NicoVideoCachedStream?.Dispose();
			NicoVideoCachedStream = null;

            foreach (var div in GetAllQuality())
            {
                if (div.NowPlaying)
                {
                    div.OnPlayDone();
                }
            }

            var smtc = HohoemaApp.MediaPlayer.SystemMediaTransportControls;
            smtc.DisplayUpdater.ClearAll();
            smtc.IsEnabled = false;
            smtc.DisplayUpdater.Update();
        }

        public async Task RestoreCache(NicoVideoQuality quality, string filepath)
        {
            var divided = GetDividedQualityNicoVideo(quality);

            await divided.RestoreCache(filepath);

            await divided.RequestCache();

            if (divided.VideoFileCreatedAt > this.CachedAt)
            {
                this.CachedAt = divided.VideoFileCreatedAt;
            }
        }

        public Task RequestCache(NicoVideoQuality? quality = null)
		{
            if (!quality.HasValue)
            {
                if (HohoemaApp.UserSettings.PlayerSettings.IsLowQualityDeafult)
                {
                    quality = NicoVideoQuality.Low;
                }
                else
                {
                    if (IsOriginalQualityOnly)
                    {
                        quality = NicoVideoQuality.Low;
                    }
                    else
                    {
                        quality = NicoVideoQuality.Original;
                    }
                }
            }


            var divided = GetDividedQualityNicoVideo(quality.Value);

            _NiconicoMediaManager.VideoCacheStateChanged -= _NiconicoMediaManager_VideoCacheStateChanged;
            _NiconicoMediaManager.VideoCacheStateChanged += _NiconicoMediaManager_VideoCacheStateChanged;

            return divided.RequestCache();
		}


		public async Task CancelCacheRequest(NicoVideoQuality? quality = null)
		{
            if (quality.HasValue)
            {
                var divided = GetDividedQualityNicoVideo(quality.Value);
                await divided.DeleteCache();
            }
            else
            {
                foreach (var divided in GetAllQuality())
                {
                    await divided.DeleteCache();
                }
            }

            // 全てのキャッシュリクエストが取り消されていた場合
            // 動画情報とコメント情報をDBから削除する
			if (GetAllQuality().All(x => !x.IsCacheRequested))
			{
				var info = await VideoInfoDb.GetEnsureNicoVideoInfoAsync(RawVideoId);
				await VideoInfoDb.RemoveAsync(info);

				CommentDb.Remove(RawVideoId);

                _NiconicoMediaManager.VideoCacheStateChanged -= _NiconicoMediaManager_VideoCacheStateChanged;
            }
        }





		public async Task<PostCommentResponse> SubmitComment(string comment, TimeSpan position, string commands)
		{
			var commentRes = _CachedCommentResponse;
			var watchApiRes = CachedWatchApiResponse;

			if (commentRes == null && watchApiRes == null)
			{
				throw new Exception("コメント投稿には事前に動画ページへのアクセスとコメント情報取得が必要です");
			}

			try
			{
				return await HohoemaApp.NiconicoContext.Video.PostCommentAsync(watchApiRes, commentRes.Thread, comment, position, commands);
			}
			catch
			{
				// コメントデータを再取得してもう一度？
				return null;
			}
		}


		/// <summary>
		/// キャッシュ要求の基本処理を実行します
		/// DividedQualityNicoVideoから呼び出されます
		/// </summary>
		/// <returns></returns>
		internal async Task OnCacheRequested()
		{
			IfVideoDeletedThrowException();

			if (CachedWatchApiResponse != null)
			{
                if (_CachedCommentResponse == null)
                {
                    await GetComments(true);
                }

                if (_CachedCommentResponse != null)
                {
                    CommentDb.AddOrUpdate(RawVideoId, _CachedCommentResponse);
                }
            }

            var info = await VideoInfoDb.GetEnsureNicoVideoInfoAsync(RawVideoId);
            
            info.VideoId = this.VideoId;
			info.Length = this.VideoLength;
			info.LowSize = (uint)this.SizeLow;
			info.HighSize = (uint)this.SizeHigh;
			info.Title = this.Title;
			info.UserId = this.VideoOwnerId;
			info.MovieType = this.ContentType;
			info.PostedAt = this.PostedAt;
			info.SetTags(this.Tags);
			info.ViewCount = this.ViewCount;
			info.MylistCount = this.MylistCount;
			info.CommentCount = this.CommentCount;
			info.ThumbnailUrl = this.ThumbnailUrl;
            
            await VideoInfoDb.UpdateAsync(info);
		}



		/// <summary>
		/// 動画削除済みの場合の処理
		/// </summary>
		private async Task DeletedTeardown()
		{
            this.IsDeleted = true;

            await FillVideoInfoFromDb();

            // キャッシュした動画データと動画コメントの削除
            await CancelCacheRequest();

            await _NiconicoMediaManager.CacheForceDeleted(this);
        }


        private async Task FillVideoInfoFromDb()
        {
            var videoInfo = await VideoInfoDb.GetAsync(RawVideoId);

            if (videoInfo == null) { return; }

            //this.RawVideoId = videoInfo.RawVideoId;
            this.VideoId = videoInfo.VideoId;
            this.VideoLength = videoInfo.Length;
            this.SizeLow = (uint)videoInfo.LowSize;
            this.SizeHigh = (uint)videoInfo.HighSize;
            this.Title = videoInfo.Title;
            this.VideoOwnerId = videoInfo.UserId;
            this.ContentType = videoInfo.MovieType;
            this.PostedAt = videoInfo.PostedAt;
            this.Tags = videoInfo.GetTags();
            this.ViewCount = videoInfo.ViewCount;
            this.MylistCount = videoInfo.MylistCount;
            this.CommentCount = videoInfo.CommentCount;
            this.ThumbnailUrl = videoInfo.ThumbnailUrl;
            this.DescriptionWithHtml = videoInfo.DescriptionWithHtml;

        }







        private void IfVideoDeletedThrowException()
		{
			if (IsDeleted) { throw new Exception("video is deleted"); }
		}


		public NGResult CheckUserNGVideo()
		{
			return HohoemaApp.UserSettings?.NGSettings.IsNgVideo(this);
		}


		// Initializeが呼ばれるまで有効
		public void PreSetTitle(string title)
		{
			if (_IsInitialized) { return; }

			Title = title;
		}

		public void PreSetPostAt(DateTime dateTime)
		{
			if (_IsInitialized) { return; }

			PostedAt = dateTime;
		}

		public void PreSetVideoLength(TimeSpan length)
		{
			if (_IsInitialized) { return; }

			VideoLength = length;
		}

		public void PreSetCommentCount(uint count)
		{
			if (_IsInitialized) { return; }

			CommentCount = count;
		}
		public void PreSetViewCount(uint count)
		{
			if (_IsInitialized) { return; }

			ViewCount = count;
		}
		public void PreSetMylistCount(uint count)
		{
			if (_IsInitialized) { return; }

			MylistCount = count;
		}

		public void PreSetThumbnailUrl(string thumbnailUrl)
		{
			if (_IsInitialized) { return; }

			ThumbnailUrl = thumbnailUrl;
		}


		public string RawVideoId { get; private set; }
		public string VideoId { get; private set; }

		public bool IsDeleted { get; private set; }
		public bool IsCommunity { get; private set; }

		public MovieType ContentType { get; private set; }
		public string Title { get; private set; }
		public TimeSpan VideoLength { get; private set; }
		public DateTime PostedAt { get; private set; }
		public uint VideoOwnerId { get; private set; }
		public bool IsOriginalQualityOnly => SizeLow == 0 || ContentType != MovieType.Mp4;
		public List<Tag> Tags { get; private set; }
		public uint SizeLow { get; private set; }
		public uint SizeHigh { get; private set; }
		public uint ViewCount { get; internal set; }
		public uint CommentCount { get; internal set; }
		public uint MylistCount { get; internal set; }
		public string ThumbnailUrl { get; internal set; }

		public Uri VideoUrl { get; private set; }
		public string ThreadId { get; private set; }
		public string DescriptionWithHtml { get; private set; }
		public bool NowLowQualityOnly { get; private set; }
		public MediaProtocolType ProtocolType { get; private set; }
		public PrivateReasonType PrivateReasonType { get; private set; }

        private DateTime _CachedAt;
        public DateTime CachedAt
        {
            get { return _CachedAt; }
            set { SetProperty(ref _CachedAt, value); }
        }


		public bool IsNeedPayment { get; private set; }


		public bool IsRequireConfirmDelete { get; private set; }

		public HohoemaApp HohoemaApp { get; private set; }
        NiconicoMediaManager _NiconicoMediaManager;

		public bool LastAccessIsLowQuality { get; private set; }


		// 有害動画への対応
		public bool IsBlockedHarmfulVideo { get; private set; }

		public HarmfulContentReactionType HarmfulContentReactionType { get; set; }
		
		public IRandomAccessStream NicoVideoCachedStream { get; private set; }


		private Dictionary<NicoVideoQuality, DividedQualityNicoVideo> _InterfaceByQuality;


        private ObservableCollection<DividedQualityNicoVideo> _QualityDividedVideos = new ObservableCollection<DividedQualityNicoVideo>();
        public ReadOnlyObservableCollection<DividedQualityNicoVideo> QualityDividedVideos { get; private set; }


        private DividedQualityNicoVideo _OriginalQuality;
		public DividedQualityNicoVideo OriginalQuality
		{
			get
			{
				return _OriginalQuality ??
					(_OriginalQuality = GetDividedQualityNicoVideo(NicoVideoQuality.Original));
			}
		}


		private DividedQualityNicoVideo _LowQuality;
		public DividedQualityNicoVideo LowQuality
		{
			get
			{
				return _LowQuality ??
					(_LowQuality = GetDividedQualityNicoVideo(NicoVideoQuality.Low));
			}
		}


		

	

		public static string MakeVideoFileName(string title, string videoid)
		{
			return $"{title.ToSafeFilePath()} - [{videoid}]";
		}



		public static string GetProgressFileName(string rawVideoId)
		{
			return $"{rawVideoId}_progress";
		}


	}
}
