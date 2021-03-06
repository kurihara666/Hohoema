﻿using Mntone.Nico2.Mylist;
using NicoPlayerHohoema.Models;
using NicoPlayerHohoema.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using NicoPlayerHohoema.Views.Service;
using Windows.UI.Xaml.Navigation;
using Mntone.Nico2.Searches.Mylist;
using Prism.Commands;
using Mntone.Nico2;
using Prism.Windows.Navigation;

namespace NicoPlayerHohoema.ViewModels
{
	public class SearchResultMylistPageViewModel : HohoemaListingPageViewModelBase<MylistSearchListingItem>
	{
		public MylistSearchPagePayloadContent SearchOption { get; private set; }

		public SearchResultMylistPageViewModel(
			HohoemaApp hohoemaApp
			, PageManager pageManager
			) 
			: base(hohoemaApp, pageManager)
		{
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
                SearchOption = PagePayloadBase.FromParameterString<MylistSearchPagePayloadContent>(e.Parameter as string);
            }

            if (SearchOption == null)
            {
                throw new Exception();
            }


            Models.Db.SearchHistoryDb.Searched(SearchOption.Keyword, SearchOption.SearchTarget);


            var target = "マイリスト";
			var optionText = Util.SortHelper.ToCulturizedText(SearchOption.Sort, SearchOption.Order);
			UpdateTitle($"{SearchOption.Keyword} - {target}/{optionText}");

			base.OnNavigatedTo(e, viewModelState);
		}

		#region Implement HohoemaVideListViewModelBase


		protected override IIncrementalSource<MylistSearchListingItem> GenerateIncrementalSource()
		{
			return new MylistSearchSource(SearchOption, HohoemaApp, PageManager);
		}

		protected override bool CheckNeedUpdateOnNavigateTo(NavigationMode mode)
		{
			var source = IncrementalLoadingItems?.Source as MylistSearchSource;
			if (source == null) { return true; }

			if (SearchOption != null)
			{
				return !SearchOption.Equals(source.SearchOption);
			}
			else
			{
				return true;
			}
		}

		#endregion
	}

	public class MylistSearchListingItem : HohoemaListingPageItemBase
	{
		PageManager _PageManager;


		public MylistSearchListingItem(MylistGroup mylistgroup, PageManager pageManager)
		{
			_PageManager = pageManager;

			Name = mylistgroup.Name;
			Description = mylistgroup.Description;
			ItemCount = mylistgroup.ItemCount;
			GroupId = mylistgroup.Id;
			UpdateTime = mylistgroup.UpdateTime;

			SampleVideos = mylistgroup.VideoInfoItems?.Select(x => x.Video).ToList() ?? new List<Mntone.Nico2.Searches.Video.Video>();
		}


		public string Name { get; private set; }
		public string Description { get; private set; }
		public uint ItemCount { get; private set; }
		public string GroupId { get; private set; }
		public DateTime UpdateTime { get; private set; }
		public List<Mntone.Nico2.Searches.Video.Video> SampleVideos { get; private set; }


		private DelegateCommand _OpenMylistCommand;
		public DelegateCommand OpenMylistCommand
		{
			get
			{
				return _OpenMylistCommand
					?? (_OpenMylistCommand = new DelegateCommand(() => 
					{
						_PageManager.OpenPage(HohoemaPageType.Mylist, GroupId);
					}));
			}
		}

        public override ICommand PrimaryCommand
		{
			get
			{
				return OpenMylistCommand;
			}
		}
	}


	public class MylistSearchSource : IIncrementalSource<MylistSearchListingItem>
	{
		public int MaxPageCount { get; private set; }

		HohoemaApp _HohoemaApp;
		PageManager _PageManager;
		public MylistSearchPagePayloadContent SearchOption { get; private set; }
		
		private MylistSearchResponse _MylistGroupResponse;



		public MylistSearchSource(MylistSearchPagePayloadContent searchOption, HohoemaApp hohoemaApp, PageManager pageManager)
		{
			_HohoemaApp = hohoemaApp;
			_PageManager = pageManager;
			SearchOption = searchOption;
		}





		public uint OneTimeLoadCount
		{
			get
			{
				return 10;
			}
		}


		public async Task<int> ResetSource()
		{
			// Note: 件数が1だとJsonのParseがエラーになる
			_MylistGroupResponse = await _HohoemaApp.NiconicoContext.Search.MylistSearchAsync(
				SearchOption.Keyword,
				0,
				2,
				SearchOption.Sort, 
				SearchOption.Order
				);

			return (int)_MylistGroupResponse.GetTotalCount();
		}


		

		public async Task<IEnumerable<MylistSearchListingItem>> GetPagedItems(int head, int count)
		{
			var items = new List<MylistSearchListingItem>();


			var response = await _HohoemaApp.NiconicoContext.Search.MylistSearchAsync(
				SearchOption.Keyword
				, (uint)head
				, (uint)count
				, SearchOption.Sort
				, SearchOption.Order
			);


			foreach (var item in response.MylistGroupItems)
			{
				// TODO: mylistGroupをリスト表示
				items.Add(new MylistSearchListingItem(item, _PageManager));
			}

			return items;
		}
	}
}
