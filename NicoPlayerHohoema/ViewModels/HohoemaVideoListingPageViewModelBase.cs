﻿using NicoPlayerHohoema.Models;
using NicoPlayerHohoema.Util;
using Prism.Commands;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Prism.Windows.Navigation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using System.Threading;
using Windows.UI.Xaml;
using NicoPlayerHohoema.Views.Service;
using Microsoft.Practices.Unity;
using System.Runtime.InteropServices.WindowsRuntime;

namespace NicoPlayerHohoema.ViewModels
{
	public abstract class HohoemaVideoListingPageViewModelBase<VIDEO_INFO_VM> : HohoemaListingPageViewModelBase<VIDEO_INFO_VM>
		where VIDEO_INFO_VM : VideoInfoControlViewModel
	{
		public HohoemaVideoListingPageViewModelBase(HohoemaApp app, PageManager pageManager, MylistRegistrationDialogService mylistDialogService, bool isRequireSignIn = true)
			: base(app, pageManager)
		{
			MylistDialogService = mylistDialogService;


			var SelectionItemsChanged = SelectedItems.ToCollectionChanged().ToUnit();

#if DEBUG
			SelectedItems.CollectionChangedAsObservable()
				.Subscribe(x =>
				{
					Debug.WriteLine("Selected Count: " + SelectedItems.Count);
				});
#endif


			PlayAllCommand = SelectionItemsChanged
				.Select(_ => SelectedItems.Count > 0)
				.ToReactiveCommand(false)
				.AddTo(_CompositeDisposable);

			PlayAllCommand
				.SubscribeOnUIDispatcher()
				.Subscribe(_ =>
				{

				// TODO: プレイリストに登録
				// プレイリストを空にしてから選択動画を登録

				//				SelectedVideoInfoItems.First()?.PlayCommand.Execute();
			    })
			    .AddTo(_CompositeDisposable);

			CancelCacheDownloadRequest = SelectionItemsChanged
				.Select(_ => SelectedItems.Count > 0)
				.ToReactiveCommand(false)
				.AddTo(_CompositeDisposable);

			CancelCacheDownloadRequest
				.SubscribeOnUIDispatcher()
				.Subscribe(async _ =>
				{
					var items = EnumerateCacheRequestedVideoItems().ToList();
					var action = AsyncInfo.Run<uint>(async (cancelToken, progress) => 
					{
						uint count = 0;
						foreach (var item in items)
						{
        					await item.NicoVideo.CancelCacheRequest();

							++count;
							progress.Report(count);
						}

						ClearSelection();
					});

					await PageManager.StartNoUIWork("キャッシュリクエストをキャンセル中", items.Count, () => action);
				}
			)
			.AddTo(_CompositeDisposable);


            // クオリティ指定無しのキャッシュDLリクエスト
            RequestCacheDownload = SelectionItemsChanged
                .Select(_ => SelectedItems.Count > 0 && CanDownload)
				.ToReactiveCommand(false)
				.AddTo(_CompositeDisposable);

			RequestCacheDownload
				.SubscribeOnUIDispatcher()
				.Subscribe(async _ =>
				{
					// 低画質限定を指定されている場合はそれに従う
					if (HohoemaApp.UserSettings.PlayerSettings.IsLowQualityDeafult)
					{
						foreach (var item in EnumerateCanDownloadVideoItem())
						{
							if (item.NicoVideo.IsOriginalQualityOnly)
							{
								await item.NicoVideo.RequestCache(NicoVideoQuality.Original);
							}
							else
							{
								await item.NicoVideo.RequestCache(NicoVideoQuality.Low);
							}
						}
					}

					// そうでない場合は、オリジナル画質を優先して現在ダウンロード可能な画質でキャッシュリクエスト
					else
					{
						foreach (var item in EnumerateCanDownloadVideoItem(/*画質指定なし*/))
						{
							if (item.NicoVideo.OriginalQuality.CanRequestCache)
							{
								await item.NicoVideo.RequestCache(NicoVideoQuality.Original);
							}
							else if (item.NicoVideo.LowQuality.CanRequestCache)
							{
								await item.NicoVideo.RequestCache(NicoVideoQuality.Low);
							}
						}
					}

					ClearSelection();
					await UpdateList();
				})
			.AddTo(_CompositeDisposable);



			RegistratioMylistCommand = SelectionItemsChanged
				.Select(x => SelectedItems.Count > 0)
				.ToReactiveCommand(false)
				.AddTo(_CompositeDisposable);
			RegistratioMylistCommand
				.SubscribeOnUIDispatcher()
				.Subscribe(async _ =>
				{
					var result = await MylistDialogService.ShowDialog(SelectedItems.Count);

					if (result == null) { return; }

					var mylistGroup = result.Item1;
					var mylistComment = result.Item2;
					var items = EnumerateCacheRequestedVideoItems().ToList();
					var action = AsyncInfo.Run<uint>(async (cancelToken, progress) =>
					{
						uint progressCount = 0;


						Debug.WriteLine($"一括マイリスト登録を開始...");
						int successCount = 0;
						int existCount = 0;
						int failedCount = 0;
						foreach (var video in SelectedItems)
						{
							var registrationResult = await mylistGroup.Registration(
								video.RawVideoId
								, mylistComment
								, withRefresh: false /* あとで一括でリフレッシュ */
								);

							switch (registrationResult)
							{
								case Mntone.Nico2.ContentManageResult.Success: successCount++; break;
								case Mntone.Nico2.ContentManageResult.Exist: existCount++; break;
								case Mntone.Nico2.ContentManageResult.Failed: failedCount++; break;
								default:
									break;
							}

							progressCount++;
							progress.Report(progressCount);

							Debug.WriteLine($"{video.Title}[{video.RawVideoId}]:{registrationResult.ToString()}");
						}

						// リフレッシュ
						await mylistGroup.Refresh();


						// ユーザーに結果を通知

						var titleText = $"「{mylistGroup.Name}」に {successCount}件 の動画を登録しました";
						var toastService = App.Current.Container.Resolve<ToastNotificationService>();
						var resultText = $"";
						if (existCount > 0)
						{
							resultText += $"重複：{existCount} 件";
						}
						if (failedCount > 0)
						{
							resultText += $"\n登録に失敗した {failedCount}件 は選択されたままです";
						}

						toastService.ShowText(titleText, resultText);



						// マイリスト登録に失敗したものを残すように
						// 登録済みのアイテムを選択アイテムリストから削除
						foreach (var item in SelectedItems.ToArray())
						{
							if (mylistGroup.CheckRegistratedVideoId(item.RawVideoId))
							{
								SelectedItems.Remove(item);
							}
						}

						//					ResetList();

						Debug.WriteLine($"一括マイリスト登録を完了---------------");
						ClearSelection();
					});

					await PageManager.StartNoUIWork("マイリスト登録", items.Count, () => action);
				}
			);


            AddPlaylistCommand = SelectionItemsChanged
                .Select(x => SelectedItems.Count > 0)
                .ToReactiveCommand<string>(false)
                .AddTo(_CompositeDisposable);

            AddPlaylistCommand.Subscribe(playlistId => 
            {
                var hohoemaPlaylist = HohoemaApp.Playlist;
                var playlist = hohoemaPlaylist.Playlists.FirstOrDefault(x => x.Id == playlistId);

                if (playlist == null) { return; }

                foreach (var item in SelectedItems)
                {
                    playlist.AddVideo(item.RawVideoId, item.Title);
                }
            });

            Playlists = HohoemaApp.Playlist.Playlists.ToReadOnlyReactiveCollection();
        }


		protected override void OnDispose()
		{
			base.OnDispose();
		}


		protected override Task OnSignIn(ICollection<IDisposable> userSessionDisposer, CancellationToken cancelToken)
        {
            ReflectCanDownloadStatus();

            return base.OnSignIn(userSessionDisposer, cancelToken);
        }

		public override void OnNavigatedTo(NavigatedToEventArgs e, Dictionary<string, object> viewModelState)
		{
			base.OnNavigatedTo(e, viewModelState);

			if (IncrementalLoadingItems == null
				|| CheckNeedUpdateOnNavigateTo(e.NavigationMode))
			{
				//				ResetList();
			}

            ReflectCanDownloadStatus();
        }

		protected override void OnHohoemaNavigatingFrom(NavigatingFromEventArgs e, Dictionary<string, object> viewModelState, bool suspending)
		{
			base.OnHohoemaNavigatingFrom(e, viewModelState, suspending);

			// 戻る時だけ
			if (e.NavigationMode == NavigationMode.Back 
				&& IncrementalLoadingItems != null
				&& IncrementalLoadingItems.Source is HohoemaVideoPreloadingIncrementalSourceBase<VIDEO_INFO_VM>)
			{
				var preloadSource = IncrementalLoadingItems.Source as HohoemaVideoPreloadingIncrementalSourceBase<VIDEO_INFO_VM>;
				preloadSource.CancelPreloading();
			}
		}

		private IEnumerable<VideoInfoControlViewModel> EnumerateCacheRequestedVideoItems()
		{
			return SelectedItems.Where(x =>
			{
				return x.NicoVideo.OriginalQuality.IsCacheRequested
					|| x.NicoVideo.LowQuality.IsCacheRequested;

			});
		}

		private IEnumerable<VideoInfoControlViewModel> EnumerateCanDownloadVideoItem(NicoVideoQuality? quality = null)
		{
			if (!quality.HasValue)
			{
				return SelectedItems.Where(x =>
				{
					var video = x.NicoVideo;
					if (video.OriginalQuality.CanRequestCache && !video.OriginalQuality.IsCached)
					{
						return true;
					}
					else if (video.LowQuality.CanRequestCache && !video.LowQuality.IsCached)
					{
						return true;
					}
					else
					{
						return false;
					}
				});
			}
            else
            {
                switch (quality)
                {
                    case NicoVideoQuality.Original:
                        return SelectedItems.Where(x => x.NicoVideo.OriginalQuality.CanRequestCache && !x.NicoVideo.OriginalQuality.IsCached);
                    case NicoVideoQuality.Low:
                        return SelectedItems.Where(x => x.NicoVideo.LowQuality.CanRequestCache && !x.NicoVideo.LowQuality.IsCached);
                    default:
                        return Enumerable.Empty<VideoInfoControlViewModel>();
                }
            }
        }

		

        private void ReflectCanDownloadStatus()
        {
            CanDownload = HohoemaApp.UserSettings.CacheSettings.IsUserAcceptedCache 
                && HohoemaApp.UserSettings.CacheSettings.IsEnableCache
                && HohoemaApp.IsLoggedIn
                ;
        }







        private bool _CanDownload;
		public bool CanDownload
		{
			get { return _CanDownload; }
			set { SetProperty(ref _CanDownload, value); }
		}

        public ReadOnlyReactiveCollection<Playlist> Playlists { get; private set; }

		public MylistRegistrationDialogService MylistDialogService { get; private set; }

		public ReactiveCommand PlayAllCommand { get; private set; }
        public ReactiveCommand<string> AddPlaylistCommand { get; private set; }
        public ReactiveCommand CancelCacheDownloadRequest { get; private set; }
		public ReactiveCommand DeleteOriginalQualityCache { get; private set; }
		public ReactiveCommand DeleteLowQualityCache { get; private set; }

		public ReactiveCommand RegistratioMylistCommand { get; private set; }


		// クオリティ指定なしのコマンド
		// VMがクオリティを実装している場合には、そのクオリティを仕様
		// そうでない場合は、リクエスト時は低クオリティのみを
		// 削除時はすべてのクオリティの動画を指定してアクションを実行します。
		// 基本的にキャッシュ管理画面でしか使わないはずです
		public ReactiveCommand RequestCacheDownload { get; private set; }
		public ReactiveCommand DeleteCache { get; private set; }
	}


    
}
