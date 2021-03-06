﻿using NicoPlayerHohoema.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Prism.Windows.Navigation;
using NicoPlayerHohoema.Util;
using Mntone.Nico2.Searches.Live;
using Prism.Commands;
using System.Windows.Input;

namespace NicoPlayerHohoema.ViewModels
{
	public class SearchResultLivePageViewModel : HohoemaListingPageViewModelBase<LiveInfoViewModel>
	{
		public LiveSearchPagePayloadContent SearchOption { get; private set; }


		public SearchResultLivePageViewModel(
			HohoemaApp app,
			PageManager pageManager
			) 
			: base(app, pageManager)
		{
            ChangeRequireServiceLevel(HohoemaAppServiceLevel.OnlineWithoutLoggedIn);
		}

		#region Commands


		private DelegateCommand _ShowSearchHistoryCommand;
		public DelegateCommand ShowSearchHistoryCommand
		{
			get
			{
				return _ShowSearchHistoryCommand
					?? (_ShowSearchHistoryCommand = new DelegateCommand(() =>
					{
						PageManager.OpenPage(HohoemaPageType.Search);
					}));
			}
		}

		#endregion


		public override void OnNavigatedTo(NavigatedToEventArgs e, Dictionary<string, object> viewModelState)
		{
            if (e.Parameter is string)
            {
                SearchOption = PagePayloadBase.FromParameterString<LiveSearchPagePayloadContent>(e.Parameter as string);
            }

            if (SearchOption == null)
            {
                throw new Exception();
            }

            Models.Db.SearchHistoryDb.Searched(SearchOption.Keyword, SearchOption.SearchTarget);

            var target = "生放送";
			var optionText = Util.SortHelper.ToCulturizedText(SearchOption.Sort, SearchOption.Order);

			string mode = "";
			if (SearchOption.Mode.HasValue)
			{
				switch (SearchOption.Mode)
				{
					case Mntone.Nico2.Searches.Live.NicoliveSearchMode.OnAir:
						mode = "放送中";
						break;
					case Mntone.Nico2.Searches.Live.NicoliveSearchMode.Reserved:
						mode = "放送予定";
						break;
					case Mntone.Nico2.Searches.Live.NicoliveSearchMode.Closed:
						mode = "放送終了";
						break;
					default:
						break;
				}
			}
			else
			{
				mode = "すべて";
			}
			
			UpdateTitle($"{SearchOption.Keyword} - {target}/{optionText}({mode})");

			base.OnNavigatedTo(e, viewModelState);
		}

		protected override IIncrementalSource<LiveInfoViewModel> GenerateIncrementalSource()
		{
			return new LiveSearchSource(SearchOption, HohoemaApp, PageManager);
		}
	}



	public class LiveSearchSource : IIncrementalSource<LiveInfoViewModel>
	{

		public HohoemaApp HohoemaApp { get; private set; }
		public PageManager PageManager { get; private set; }

		public LiveSearchPagePayloadContent SearchOption { get; private set; }

		public List<Tag> Tags { get; private set; }

		public uint OneTimeLoadCount => 10;

		public LiveSearchSource(
			LiveSearchPagePayloadContent searchOption,
			HohoemaApp app,
			PageManager pageManager
			)
		{
			HohoemaApp = app;
			PageManager = pageManager;
			SearchOption = searchOption;
		}

		private Task<NicoliveVideoResponse> GetLiveSearchResponseOnCurrentOption(uint from, uint length)
		{
			return HohoemaApp.ContentFinder.LiveSearchAsync(
					SearchOption.Keyword,
					SearchOption.IsTagSearch,
					from: from,
					length: length,
					provider: SearchOption.Provider,
					sort: SearchOption.Sort,
					order: SearchOption.Order,
					mode: SearchOption.Mode
					);
		}

		public async Task<int> ResetSource()
		{
			int totalCount = 0;
			try
			{
				var res = await GetLiveSearchResponseOnCurrentOption(0, 1);

				totalCount = res.TotalCount.FilteredCount;
			}
			catch { }

			return totalCount;
		}

		public async Task<IEnumerable<LiveInfoViewModel>> GetPagedItems(int head, int count)
		{
			var res = await GetLiveSearchResponseOnCurrentOption((uint)head, (uint)count);

			if (res == null || res.VideoInfo == null)
			{
				return Enumerable.Empty<LiveInfoViewModel>();
			}

			Tags = res.Tags?.Tag.ToList();

			if (res.VideoInfo == null)
			{
				return Enumerable.Empty<LiveInfoViewModel>();
			}

			return res.VideoInfo.Select(x =>
			{
				return new LiveInfoViewModel(x, HohoemaApp.Playlist, PageManager);
			});
		}
	}


	public class LiveInfoViewModel : HohoemaListingPageItemBase
	{
        public HohoemaPlaylist Playlist { get; private set; }
		public VideoInfo LiveVideoInfo { get; private set; }
		public PageManager PageManager { get; private set; }


		public string LiveId { get; private set; }

		public string CommunityName { get; private set; }
		public string CommunityThumbnail { get; private set; }
		public string CommunityGlobalId { get; private set; }
		public Mntone.Nico2.Live.CommunityType CommunityType { get; private set; }

		public string LiveTitle { get; private set; }
		public int ViewCounter { get; private set; }
		public int CommentCount { get; private set; }
		public DateTime OpenTime { get; private set; }
		public DateTime StartTime { get; private set; }
		public bool HasEndTime { get; private set; }
		public DateTime EndTime { get; private set; }
		public string DurationText { get; private set; }
		public bool IsTimeshiftEnabled { get; private set; }
		public bool IsCommunityMemberOnly { get; private set; }



		public LiveInfoViewModel(VideoInfo liveVideoInfo, HohoemaPlaylist playlist, PageManager pageManager)
		{
			LiveVideoInfo = liveVideoInfo;
			PageManager = pageManager;
            Playlist = playlist;

            LiveId = liveVideoInfo.Video.Id;
			CommunityName = LiveVideoInfo.Community?.Name;
			CommunityThumbnail = LiveVideoInfo.Community?.Thumbnail;
			CommunityGlobalId = LiveVideoInfo.Community?.GlobalId;
			CommunityType = LiveVideoInfo.Video.ProviderType;

			LiveTitle = LiveVideoInfo.Video.Title;
			ViewCounter = int.Parse(LiveVideoInfo.Video.ViewCounter);
			CommentCount = int.Parse(LiveVideoInfo.Video.CommentCount);
			OpenTime = LiveVideoInfo.Video.OpenTime;
			StartTime = LiveVideoInfo.Video.StartTime;
			EndTime = LiveVideoInfo.Video.EndTime;
			IsTimeshiftEnabled = LiveVideoInfo.Video.TimeshiftEnabled;
			IsCommunityMemberOnly = LiveVideoInfo.Video.CommunityOnly;

			var duration = EndTime - StartTime;
			if (LiveVideoInfo.Video.StartTime < DateTime.Now)
			{
				// 予約
				DurationText = "";
			}
			else if (LiveVideoInfo.Video.EndTime < DateTime.Now)
			{
				// 終了
				if (duration.Hours > 0)
				{
					DurationText = $"（{duration.Hours}時間 {duration.Minutes}分）";
				}
				else
				{
					DurationText = $"（{duration.Minutes}分）";
				}
			}
			else
			{
				// 放送中
				// 終了
				if (duration.Hours > 0)
				{
					DurationText = $"{duration.Hours}時間 {duration.Minutes}分 経過";
				}
				else
				{
					DurationText = $"{duration.Minutes}分 経過";
				}
			}
		}


		private DelegateCommand _OpenLiveVideoPageCommand;
		public override ICommand PrimaryCommand
		{
			get
			{
				return _OpenLiveVideoPageCommand
					?? (_OpenLiveVideoPageCommand = new DelegateCommand(() =>
					{
                        Playlist.PlayLiveVideo(LiveId, LiveTitle);
					}));
			}
		}

	}
}
