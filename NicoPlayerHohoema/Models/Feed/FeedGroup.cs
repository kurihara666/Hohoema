﻿using NicoPlayerHohoema.Util;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Foundation;
using Windows.UI.Core;

namespace NicoPlayerHohoema.Models
{
	// ユーザーが指定したFavItemを束ねて、動画Feedを生成する
	[DataContract]
	[KnownType(typeof(TagFeedSource))]
	[KnownType(typeof(MylistFeedSource))]
	[KnownType(typeof(UserFeedSource))]
	public class FeedGroup2 : BindableBase, IFeedGroup, IBackgroundUpdateable
	{
		public const int MaxFeedItemsCount = 100;

		#region Properties

		public HohoemaApp HohoemaApp { get; internal set; }
		public FeedManager FeedManager { get; internal set; }

		[DataMember(Name = "id")]
		private Guid _Id;
		public Guid Id
		{
			get { return _Id; }
			private set { SetProperty(ref _Id, value); }
		}

		[DataMember(Name = "label")]
		private string _Label;
		public string Label
		{
			get { return _Label; }
			internal set { SetProperty(ref _Label, value); }
		}


		[DataMember(Name = "feed_source_list")]
		private List<IFeedSource> _FeedSourceList;


		public IReadOnlyList<IFeedSource> FeedSourceList
		{
			get
			{
				return _FeedSourceList;
			}
		}



		public List<FeedItem> FeedItems { get; private set; }

		[DataMember(Name = "update_time")]
		private DateTime _UpdateTime;
		public DateTime UpdateTime
		{
			get { return _UpdateTime; }
			private set
            {
                SetProperty(ref _UpdateTime, value);
            }
		}

		[DataMember(Name = "is_need_refresh")]
		private bool _IsNeedRefresh;
		public bool IsNeedRefresh
		{
			get { return _IsNeedRefresh; }
			internal set { SetProperty(ref _IsNeedRefresh, value); }
		}


		#endregion


		private SemaphoreSlim _RefreshingLock;

        public event BackgroundUpdateCompletedEventHandler Completed;

        public FeedGroup2()
		{
			Id = Guid.NewGuid();
			Label = "";
			_FeedSourceList = new List<IFeedSource>();
			FeedItems = new List<FeedItem>();
			IsNeedRefresh = true;
			_RefreshingLock = new SemaphoreSlim(1, 1);
		}

		public FeedGroup2(string label)
		{
			Id = Guid.NewGuid();
			Label = label;
			_FeedSourceList = new List<IFeedSource>();
			FeedItems = new List<FeedItem>();
			IsNeedRefresh = true;
			_RefreshingLock = new SemaphoreSlim(1, 1);
		}

		public FeedGroup2(FeedGroup legacy)
		{
			Id = legacy.Id;
			Label = legacy.Label;
			_FeedSourceList = legacy.FeedSourceList.ToList();
			FeedItems = new List<FeedItem>();
			IsNeedRefresh = legacy.IsNeedRefresh;
			_UpdateTime = legacy.UpdateTime;
			_RefreshingLock = new SemaphoreSlim(1, 1);
		}


		public IAsyncAction BackgroundUpdate(CoreDispatcher uiDispatcher)
		{
			IsNeedRefresh = true;
			return RefreshAction(() => _Refresh()).AsAsyncAction();
		}



		public IFeedSource AddTagFeedSource(string tag)
		{			
			if (ExistFeedSource(FollowItemType.Tag, tag)) { return null; }

			var feedSource = new TagFeedSource(tag);
			_FeedSourceList.Add(feedSource);

			IsNeedRefresh = true;

			return feedSource;
		}

		public IFeedSource AddMylistFeedSource(string name, string mylistGroupId)
		{
			if (ExistFeedSource(FollowItemType.Mylist, mylistGroupId)) { return null; }

			var feedSource = new MylistFeedSource(name, mylistGroupId);
			_FeedSourceList.Add(feedSource);

			IsNeedRefresh = true;

			return feedSource;
		}

		public IFeedSource AddUserFeedSource(string name, string userId)
		{
			if (ExistFeedSource(FollowItemType.User, userId)) { return null; }

			var feedSource = new UserFeedSource(name, userId);
			_FeedSourceList.Add(feedSource);

			IsNeedRefresh = true;

			return feedSource;
		}

		public void RemoveUserFeedSource(IFeedSource feedSource)
		{
			if (_FeedSourceList.Remove(feedSource))
			{
				IsNeedRefresh = true;
			}
		}


		public bool ExistFeedSource(FollowItemType itemType, string id)
		{
			return _FeedSourceList.Any(x => x.FollowItemType == itemType && x.Id == id);
		}



		public async Task Refresh()
		{
			IsNeedRefresh = true;
			await FeedManager.RefreshOneAsync(this);
		}

		private async Task _Refresh()
		{
			if (!IsNeedRefresh) { return; }

			Debug.WriteLine($"{Label} starting update feed.");

			var updateTime = DateTime.Now;
			var latestItems = new List<FeedItem>();
			foreach (var feedSource in _FeedSourceList)
			{
				var items = await feedSource.GetLatestItems(HohoemaApp);
				foreach (var item in items)
				{
					latestItems.Add(item);
				}
			}


			var latestOrderedItems = latestItems
				.OrderByDescending(x => x.SubmitDate)
				.Take(MaxFeedItemsCount)
				.ToList();

			foreach (var item in latestOrderedItems)
			{
				item.CheckedTime = updateTime;
			}

			var exceptItems = latestOrderedItems.Except(FeedItems, FeedItemComparer.Default).ToList();

			var addedItems = exceptItems.Where(x => x.CheckedTime == updateTime);

			var removedItems = FeedItems
				.Except(latestOrderedItems, FeedItemComparer.Default)
				.Where(x => x.CheckedTime != updateTime)
				.ToList();


			foreach (var addItem in addedItems)
			{
				addItem.IsUnread = true;

				// 投稿日時が初期化されていない場合はThumbnailInfoから拾ってくる

				// ユーザー動画取得の場合に投稿時刻が取れないことが原因
				// 追加されたアイテムだけのThumbnailを取得することで無駄な処理を減らす
				if (addItem.SubmitDate == default(DateTime))
				{
					try
					{
						var nicoVideo = await HohoemaApp.MediaManager.GetNicoVideoAsync(addItem.VideoId);
						
						addItem.SubmitDate = nicoVideo.PostedAt;
					}
					catch (Exception ex)
					{
						Debug.Fail("UserFeedItem 更新中、NicoVideoオブジェクトの取得に失敗しました。", ex.Message);
					}
				}
				

				FeedItems.Add(addItem);
			}

			foreach (var removedItem in removedItems)
			{
				var item = FeedItems.SingleOrDefault(x => x.VideoId == removedItem.VideoId);
				if (item != null)
				{
					item.IsDeleted = true;
					FeedItems.Remove(item);
				}

			}

			FeedItems.Sort(FeedItemComparer.Default);

			UpdateTime = updateTime;

			await FeedManager.SaveOne(this);

            Completed?.Invoke(this);

            IsNeedRefresh = false;

			Debug.WriteLine($"{Label} update feed done.");
		}


		
		private async Task RefreshAction(Func<Task> action)
		{
			try
			{
				await _RefreshingLock.WaitAsync();

				await action();
			}
			finally
			{
				_RefreshingLock.Release();
			}
		}

		public bool MarkAsRead(string videoId)
		{
			bool isChanged = false;
			foreach (var item in FeedItems)
			{
				if (item.IsUnread && item.VideoId == videoId)
				{
					item.IsUnread = false;
					isChanged = true;
				}
			}

			return isChanged;
		}

		public void ForceMarkAsRead()
		{
			foreach (var item in FeedItems)
			{
				item.IsUnread = false;
			}
		}

		public int GetUnreadItemCount()
		{
			return FeedItems.Count(x => x.IsUnread);
		}


		public async Task<bool> Rename(string newLabel)
		{
			if (await FeedManager.RenameFeedGroup(this, newLabel))
			{
				this.Label = newLabel;

				await FeedManager.SaveOne(this);

				return true;
			}
			else
			{
				return false;
			}
		}



		public async Task<bool> LoadFeedStream(FileAccessor<List<FeedItem>> fileAccessor)
		{
			try
			{
				var items = await fileAccessor.Load();
				this.FeedItems = items ?? new List<FeedItem>();

				return true;
			}
			catch
			{
				return false;
			}
		}

		
	}

	[DataContract]
	[KnownType(typeof(TagFeedSource))]
	[KnownType(typeof(MylistFeedSource))]
	[KnownType(typeof(UserFeedSource))]
	public class FeedGroup : BindableBase
	{
		public const int MaxFeedItemsCount = 50;

		#region Properties

		public HohoemaApp HohoemaApp { get; internal set; }
		public FeedManager FeedManager { get; internal set; }

		[DataMember(Name = "id")]
		private Guid _Id;
		public Guid Id
		{
			get { return _Id; }
			private set { SetProperty(ref _Id, value); }
		}

		[DataMember(Name = "label")]
		private string _Label;
		public string Label
		{
			get { return _Label; }
			internal set { SetProperty(ref _Label, value); }
		}


		[DataMember(Name = "feed_source_list")]
		private List<IFeedSource> _FeedSourceList = new List<IFeedSource>();


		public IReadOnlyList<IFeedSource> FeedSourceList
		{
			get
			{
				return _FeedSourceList;
			}
		}



		[DataMember(Name = "feed_items")]
		public List<FeedItem> FeedItems { get; private set; }

		[DataMember(Name = "update_time")]
		private DateTime _UpdateTime;
		public DateTime UpdateTime
		{
			get { return _UpdateTime; }
			private set { SetProperty(ref _UpdateTime, value); }
		}

		[DataMember(Name = "is_need_refresh")]
		private bool _IsNeedRefresh;
		public bool IsNeedRefresh
		{
			get { return _IsNeedRefresh; }
			internal set { SetProperty(ref _IsNeedRefresh, value); }
		}


		#endregion


		

	}
}
