﻿using Mntone.Nico2.Videos.Ranking;
using Mntone.Nico2.Videos.Thumbnail;
using NicoPlayerHohoema.Models;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NicoPlayerHohoema.ViewModels
{
	public class VideoInfoControlViewModel : BindableBase
	{

		public VideoInfoControlViewModel(string title, string videoUrl, HohoemaApp app)
		{
			App = app;

			Title = title;
			VideoUrl = videoUrl;

			ShowDetailCommand = new DelegateCommand(() =>
			{
			});

			PlayCommand = new DelegateCommand(() =>
			{
				App.PlayVideo(this.VideoUrl);
			});
		}



		public async void LoadThumbnail()
		{
			var videoId = Util.NicoVideoExtention.UrlToVideoId(VideoUrl);
			var thumbnail = await App.NiconicoContext.Video.GetThumbnailAsync(videoId);

			IsNotGoodVideo = App.IsNgVideo(thumbnail);

			Title = thumbnail.Title;
			ViewCount = thumbnail.ViewCount;
			CommentCount = thumbnail.CommentCount;
			MylistCount = thumbnail.MylistCount;
			OwnerComment = thumbnail.Description;
			PostAt = thumbnail.PostedAt.LocalDateTime;
			ThumbnailImageUrl = thumbnail.ThumbnailUrl;
			MovieLength = thumbnail.Length;

		}

		private string _Title;
		public string Title
		{
			get { return _Title; }
			set { SetProperty(ref _Title, value); }
		}

		private uint _ViewCount;
		public uint ViewCount
		{
			get { return _ViewCount; }
			set { SetProperty(ref _ViewCount, value); }
		}


		private uint _CommentCount;
		public uint CommentCount
		{
			get { return _CommentCount; }
			set { SetProperty(ref _CommentCount, value); }
		}

		private uint _MylistCount;
		public uint MylistCount
		{
			get { return _MylistCount; }
			set { SetProperty(ref _MylistCount, value); }
		}

		private string _OwnerComment;
		public string OwnerComment
		{
			get { return _OwnerComment; }
			set { SetProperty(ref _OwnerComment, value); }
		}

		private TimeSpan _MovieLength;
		public TimeSpan MovieLength
		{
			get { return _MovieLength; }
			set { SetProperty(ref _MovieLength, value); }
		}

		private Uri _ThumbnailImageUrl;
		public Uri ThumbnailImageUrl
		{
			get { return _ThumbnailImageUrl; }
			set { SetProperty(ref _ThumbnailImageUrl, value); }
		}

		private bool _IsNotGoodVideo;
		public bool IsNotGoodVideo
		{
			get { return _IsNotGoodVideo; }
			set { SetProperty(ref _IsNotGoodVideo, value); }
		}

		private DateTime _PostAt;
		public DateTime PostAt
		{
			get { return _PostAt; }
			set { SetProperty(ref _PostAt, value); }
		}


		


		public string VideoUrl { get; private set; }

		public DelegateCommand ShowDetailCommand { get; private set; }
		public DelegateCommand PlayCommand { get; private set; }


		public HohoemaApp App { get; private set; }
	}
}
