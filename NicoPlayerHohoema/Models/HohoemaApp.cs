﻿using Mntone.Nico2;
using Mntone.Nico2.Videos.Thumbnail;
using NicoPlayerHohoema.Models.AppMap;
using NicoPlayerHohoema.Util;
using Prism.Events;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Diagnostics;
using Windows.Media.Playback;
using Windows.Networking.Connectivity;
using Windows.Storage;
using Windows.System.Power;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace NicoPlayerHohoema.Models
{
	public class HohoemaApp : BindableBase, IDisposable
	{
		public static CoreDispatcher UIDispatcher { get; private set; }
		


        public const string PlaylistSaveFolderName = "Playlists";


        private static DateTime LastSyncRoamingData = DateTime.MinValue;

		public static async Task<HohoemaApp> Create(IEventAggregator ea)
		{
			HohoemaApp.UIDispatcher = Window.Current.CoreWindow.Dispatcher;

			var app = new HohoemaApp(ea);
			app.MediaManager = await NiconicoMediaManager.Create(app);
            
            await app.LoadUserSettings();
            await app.FeedManager.Initialize();

            var folder = ApplicationData.Current.LocalFolder;
            var playlistFolder = await folder.CreateFolderAsync(PlaylistSaveFolderName, CreationCollisionOption.OpenIfExists);
            app.Playlist = new HohoemaPlaylist(MediaPlayer, app.UserSettings.PlaylistSettings, playlistFolder);

            await app.Playlist.Load();

            app.RagistrationBackgroundUpdateHandle();

			return app;
		}

		public readonly static Guid HohoemaLoggerGroupGuid = Guid.NewGuid();


		private SemaphoreSlim _SigninLock;
		private const string ThumbnailLoadBackgroundTaskId = "ThumbnailLoader";

        public static MediaPlayer MediaPlayer { get; private set; } = new MediaPlayer()
        {
            AutoPlay = true,
            AudioCategory = MediaPlayerAudioCategory.Movie,            
        };

        private HohoemaApp(IEventAggregator ea)
		{
            EventAggregator = ea;
            NiconicoContext = new NiconicoContext();
			LoginUserId = uint.MaxValue;
			LoggingChannel = new LoggingChannel("HohoemaLog", new LoggingChannelOptions(HohoemaLoggerGroupGuid));
			UserSettings = new HohoemaUserSettings();
			ContentFinder = new NiconicoContentFinder(this);
			UserMylistManager = new UserMylistManager(this);
			FeedManager = new FeedManager(this);

            FollowManager = null;

			_SigninLock = new SemaphoreSlim(1, 1);

			BackgroundUpdater = new BackgroundUpdater("HohoemaBG", UIDispatcher);

			ApplicationData.Current.DataChanged += Current_DataChanged;


            UpdateServiceStatus();
            NetworkInformation.NetworkStatusChanged += NetworkInformation_NetworkStatusChanged;

            
        }

        private async void NetworkInformation_NetworkStatusChanged(object sender)
        {
            var isInternet = Util.InternetConnection.IsInternet();
            await HohoemaApp.UIDispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => 
            {
                if (isInternet)
                {
                    await SignInWithPrimaryAccount();
                }
                else
                {
                    await SignOut();
                }

            });
        }

        private void RagistrationBackgroundUpdateHandle()
		{
            // ホーム画面で表示するアプリマップ情報をリセット
            //AppMapManagerUpdater =

            // 非同期な初期化処理の遅延実行をスケジュール
            MediaManagerUpdater = BackgroundUpdater.RegistrationBackgroundUpdateScheduleHandler(
                MediaManager,
                "NicoMediaManager",
                label: "キャッシュ"
                );


			MylistManagerUpdater = BackgroundUpdater.RegistrationBackgroundUpdateScheduleHandler(
				UserMylistManager,
				"MylistManagerManager",
				label: "マイリスト一覧"
				);

            MediaManagerUpdater.ScheduleUpdate();
        }


		public async Task OnSuspending()
		{
			
		}


		#region SignIn/Out 


		

		public async Task<StorageFolder> GetFeedSettingsFolder()
		{
			return await ApplicationData.Current.LocalFolder.CreateFolderAsync("feed", CreationCollisionOption.OpenIfExists);
		}

		private async Task LoadUserSettings()
		{
			var folder = ApplicationData.Current.LocalFolder;

			UserSettings = await HohoemaUserSettings.LoadSettings(folder);
		}

		private static AsyncLock _RoamingDataSyncLock = new AsyncLock();
		
        public static async Task PushToRoamingData(StorageFile file)
		{
			var roamingFolder = ApplicationData.Current.RoamingFolder;
			var folder = ApplicationData.Current.LocalFolder;

            using (var releaser = await _RoamingDataSyncLock.LockAsync())
            {
                // fileの相対的なパスをLocalFolder基準で取得
                // LocalFolder外のアイテムは同期不可能
                var filePath = file.Path;
                if (filePath.StartsWith(folder.Path))
                {
                    filePath = filePath.Substring(folder.Path.Length + 1);
                }
                else
                {
                    throw new ArgumentException("file is not Local Folder item, cant sync to roaming folder.");
                }

                // ローミングから同期情報を取得、無ければ新規作成
                var syncInfoFileAccessor = new Util.FileAccessor<RoamingSyncInfo>(roamingFolder, "sync.json");
                var syncInfo = await syncInfoFileAccessor.Load();
                if (syncInfo == null)
                {
                    syncInfo = new RoamingSyncInfo();

                    // 同期情報がなかった場合は、同期不要なファイルになるので全てお掃除
                    var items = await roamingFolder.GetItemsAsync();
                    foreach (var item in items)
                    {
                        await item.DeleteAsync();
                    }
                }

                // 同期情報にfileの情報を追加してローミングフォルダに保存
                var fileProp = await file.GetBasicPropertiesAsync();
                syncInfo.AddOrReplace(filePath, fileProp.DateModified.DateTime);
                await syncInfoFileAccessor.Save(syncInfo);


                // ローミングフォルダにLocalFolderからfileまでの相対的なフォルダ構造を再現
                var folderPathStack = filePath.Split('/', '\\').ToList();
                var parentFolder = roamingFolder;
                foreach (var folderName in folderPathStack.Take(folderPathStack.Count - 1 /* without file name */ ))
                {
                    parentFolder = await parentFolder.CreateFolderAsync(folderName, CreationCollisionOption.OpenIfExists);
                }

                // ローミングフォルダ側のファイルを作成
                var fileName = Path.GetFileName(file.Path);
                var roamingFile = await parentFolder.CreateFileAsync(fileName, CreationCollisionOption.OpenIfExists);

                // ローミング側のファイルに元ファイルをコピー
                await file.CopyAndReplaceAsync(roamingFile);
            }
        }

        public static async Task RoamingDataRemoved(StorageFile file)
        {
            if (file == null)
            {
                return;
            }

            var romingFolder = ApplicationData.Current.RoamingFolder;
            var folder = ApplicationData.Current.LocalFolder;

            using (var releaser = await _RoamingDataSyncLock.LockAsync())
            {
                // fileの相対的なパスをLocalFolder基準で取得
                // LocalFolder外のアイテムは同期不可能
                var filePath = file.Path;
                if (filePath.StartsWith(folder.Path))
                {
                    filePath = filePath.Substring(folder.Path.Length - 1);
                }
                else
                {
                    return;
                }

                // ローミングから同期情報を取得、無ければ新規作成
                var syncInfoFileAccessor = new Util.FileAccessor<RoamingSyncInfo>(romingFolder, "sync.json");
                var syncInfo = await syncInfoFileAccessor.Load();
                if (syncInfo == null)
                {
                    return;
                }

                // 同期情報にfileの情報を削除して保存
                syncInfo.Remove(filePath);
                await syncInfoFileAccessor.Save(syncInfo);
            }
        }

        public static async Task PullRoamingData()
        {
            var roamingFolder = ApplicationData.Current.RoamingFolder;
            var folder = ApplicationData.Current.LocalFolder;

            using (var releaser = await _RoamingDataSyncLock.LockAsync())
            {
                var syncInfoFileAccessor = new Util.FileAccessor<RoamingSyncInfo>(roamingFolder, "sync.json");
                var syncInfo = await syncInfoFileAccessor.Load();

                if (syncInfo == null)
                {
                    // 同期情報がなかった場合は、同期不要なファイルになるので全てお掃除
                    var items = await roamingFolder.GetItemsAsync();
                    foreach (var item in items)
                    {
                        await item.DeleteAsync();
                    }

                    return;
                }

                List<FileSyncInfo> removeItems = new List<FileSyncInfo>();
                foreach (var syncFileInfo in syncInfo.SyncInfoItems)
                {
                    if (false == await PullRoamingData(syncFileInfo))
                    {
                        removeItems.Add(syncFileInfo);
                    }
                }

                if (removeItems.Count > 0)
                {
                    foreach (var removeItem in removeItems)
                    {
                        syncInfo.SyncInfoItems.Remove(removeItem);
                    }

                    await syncInfoFileAccessor.Save(syncInfo);
                }
            }
        }

        private static async Task<bool> PullRoamingData(FileSyncInfo info)
        {
            var romingFolder = ApplicationData.Current.RoamingFolder;
            var folder = ApplicationData.Current.LocalFolder;

            // ローカル側のファイルを取得
            // LocalFolderからfileまでの相対的なフォルダ構造を再現
            IStorageFile localFile = null;
            bool isNotExistLocalFile = false;
            {
                var folderPathStack = info.RelativeFilePath.Split('/', '\\').ToList();
                var parentFolder = folder;
                foreach (var folderName in folderPathStack.Take(folderPathStack.Count - 1 /* without file name */ ))
                {
                    parentFolder = await parentFolder.CreateFolderAsync(folderName, CreationCollisionOption.OpenIfExists);
                }

                var fileName = folderPathStack.Last();
                localFile = await parentFolder.TryGetItemAsync(fileName) as IStorageFile;
                if (localFile == null)
                {
                    localFile = await parentFolder.CreateFileAsync(fileName);
                    isNotExistLocalFile = true;
                }

                if (localFile == null)
                {
                    throw new Exception();
                }
            }

            IStorageFile roamingFile = null;
            {
                var folderPathStack = info.RelativeFilePath.Split('/', '\\').ToList();
                var parentFolder = romingFolder;
                foreach (var folderName in folderPathStack.Take(folderPathStack.Count - 1 /* without file name */ ))
                {
                    parentFolder = await parentFolder.CreateFolderAsync(folderName, CreationCollisionOption.OpenIfExists);
                }

                var fileName = folderPathStack.Last();
                roamingFile = await parentFolder.TryGetItemAsync(fileName) as IStorageFile;
                if (roamingFile == null)
                {
                    Debug.WriteLine(info.RelativeFilePath + " はローミングフォルダに存在しません。");
                    return false;
                }
            }

            

            if (isNotExistLocalFile)
            {
                await roamingFile.CopyAndReplaceAsync(localFile);

                Debug.WriteLine(localFile.Path + "をローカルにコピー");
            }
            else
            {
                // ローカル側ファイルとinfo.UpdateAtを比較して
                // ローカル側ファイルが古い場合
                // ローミングファイルをローカル側ファイルに上書きコピー
                var localProp = await localFile.GetBasicPropertiesAsync();
                if (info.UpdateAt > localProp.DateModified)
                {
                    await roamingFile.CopyAndReplaceAsync(localFile);
                    Debug.WriteLine(localFile.Path + "をローカルにコピー（上書き）");
                }
            }

            return true;
        }


        static TimeSpan SyncIgnoreTimeSpan = TimeSpan.FromMinutes(3);

		private async void Current_DataChanged(ApplicationData sender, object args)
		{
			if (LastSyncRoamingData + SyncIgnoreTimeSpan > DateTime.Now)
			{
				Debug.WriteLine("ローミングデータの同期：一定時間内の同期をキャンセル");
				return;
			}

			LastSyncRoamingData = DateTime.Now;

			Debug.WriteLine("ローミングデータの同期：開始");

            await PullRoamingData();

			Debug.WriteLine("ローミングデータの同期：完了");

			// ローカルフォルダを利用する機能を再初期化
			try
			{
				await _SigninLock.WaitAsync();

//				await LoadUserSettings();
				if (IsLoggedIn)
				{
//					FeedManager = new FeedManager(this);
//					await FeedManager.Initialize();
				}
			}
			finally
			{
				_SigninLock.Release();
			}
		
				
		}

		/// <summary>
		/// ユーザーIDに基づいたユーザー設定を0.4.0以降のユーザー設定として移行します。
		/// すでに0.4.0環境のユーザー設定が存在する場合や
		/// ユーザーIDに基づいたユーザー設定が存在しない場合は何もしません。
		/// 読み込みに成功するとUserSettingsが上書き更新されます。
		/// ユーザーIDに基づいたユーザー設定はフォルダごと削除されます。
		/// </summary>
		/// <param name="userId"></param>
		/// <returns></returns>
		public async Task MigrateLegacyUserSettings(string userId)
		{
			var folder = await ApplicationData.Current.LocalFolder.TryGetItemAsync(userId) as StorageFolder;
			if (folder != null)
			{
				var fileAccessor = new FileAccessor<CacheSettings>(ApplicationData.Current.LocalFolder, HohoemaUserSettings.CacheSettingsFileName);
				if (false == await fileAccessor.ExistFile())
				{
					await MoveFiles(folder, ApplicationData.Current.LocalFolder);

					await LoadUserSettings();
				}
			}
		}

		public async Task SaveUserSettings()
		{
			await UserSettings?.Save();
		}

		public async Task<NiconicoSignInStatus> SignInWithPrimaryAccount()
		{
			// 資格情報からログインパラメータを取得
			string primaryAccount_id = null;
			string primaryAccount_Password = null;

			var account = await AccountManager.GetPrimaryAccount();
			if (account != null)
			{
				primaryAccount_id = account.Item1;
				primaryAccount_Password = account.Item2;
			}

			if (String.IsNullOrWhiteSpace(primaryAccount_id) || String.IsNullOrWhiteSpace(primaryAccount_Password))
			{
				return NiconicoSignInStatus.Failed;
			}

			
			return await SignIn(primaryAccount_id, primaryAccount_Password);
		}


        public async Task<bool> CanSignInWithPrimaryAccount()
        {
            string primaryAccount_id = null;
            string primaryAccount_Password = null;

            var account = await AccountManager.GetPrimaryAccount();
            if (account != null)
            {
                primaryAccount_id = account.Item1;
                primaryAccount_Password = account.Item2;
            }

            if (String.IsNullOrWhiteSpace(primaryAccount_id) || String.IsNullOrWhiteSpace(primaryAccount_Password))
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// Appから呼び出します
        /// 他の場所からは呼ばないようにしてください
        /// </summary>
        public void Resumed()
		{
			OnResumed?.Invoke();
		}

		public async Task Relogin()
		{
			if (NiconicoContext != null)
			{
				var context = new NiconicoContext(NiconicoContext.AuthenticationToken);

				if (await context.SignInAsync() == NiconicoSignInStatus.Success)
				{
					NiconicoContext = context;
				}
			}
		}


		public IAsyncOperation<NiconicoSignInStatus> SignIn(string mailOrTelephone, string password)
		{
			return AsyncInfo.Run<NiconicoSignInStatus>(async (cancelToken) => 
			{
                if (!Util.InternetConnection.IsInternet())
                {
                    NiconicoContext?.Dispose();
                    NiconicoContext = new NiconicoContext();
                    return NiconicoSignInStatus.Failed;
                }

                if (NiconicoContext != null 
				    && NiconicoContext.AuthenticationToken?.MailOrTelephone == mailOrTelephone 
				    && NiconicoContext.AuthenticationToken?.Password == password)
				{
					return NiconicoSignInStatus.Success;
				}

				await SignOut();

				try
				{
					await _SigninLock.WaitAsync();

                    

					var context = new NiconicoContext(new NiconicoAuthenticationToken(mailOrTelephone, password));

					context.AdditionalUserAgent = HohoemaUserAgent;

					LoginErrorText = "";

					Debug.WriteLine("try login");

					NiconicoSignInStatus result = NiconicoSignInStatus.Failed;
					try
					{
						result = await context.SignInAsync();
					}
					catch
					{
						LoginErrorText = "サインインに失敗、再起動をお試しください";
					}

                    UpdateServiceStatus(result);

                    NiconicoContext = context;

                    if (result == NiconicoSignInStatus.Success)
					{
						Debug.WriteLine("login success");


						using (var loginActivityLogger = LoggingChannel.StartActivity("login process"))
						{

							loginActivityLogger.LogEvent("begin login process.");

							var fields = new LoggingFields();

							await Task.Delay(500);
							try
							{
								loginActivityLogger.LogEvent("getting UserInfo.");
								var userInfo = await NiconicoContext.User.GetInfoAsync();

								LoginUserId = userInfo.Id;
								IsPremiumUser = userInfo.IsPremium;

								{
									try
									{
										var user = await NiconicoContext.User.GetUserDetail(LoginUserId.ToString());
										LoginUserName = user.Nickname;
                                        UserIconUrl = user.ThumbnailUri;

                                        OnPropertyChanged(nameof(LoginUserName));
                                        OnPropertyChanged(nameof(UserIconUrl));
                                    }
									catch (Exception ex)
									{
										throw new Exception("ユーザー名取得のフォールバック処理に失敗 + " + LoginUserId, ex);
									}
								}

								fields.AddString("user id", LoginUserId.ToString());
								fields.AddString("user name", LoginUserName);
								fields.AddBoolean("is premium", IsPremiumUser);

								loginActivityLogger.LogEvent("[Success]:get UserInfo.", fields, LoggingLevel.Information);
							}
							catch (Exception ex)
							{
								LoginErrorText = $"ユーザー情報の取得に失敗しました。再起動をお試しください。（{ex.Message}）";

								fields.AddString("mail", mailOrTelephone);
								loginActivityLogger.LogEvent(LoginErrorText, fields, LoggingLevel.Warning);

								NiconicoContext.Dispose();
                                NiconicoContext = new NiconicoContext();

                                return NiconicoSignInStatus.Failed;
							}

							fields.Clear();




							Debug.WriteLine("user id is : " + LoginUserId);


							// 0.4.0以前のバージョンからのログインユーザー情報の移行処理
							try
							{
								await MigrateLegacyUserSettings(LoginUserId.ToString());
							}
							catch
							{
								LoginErrorText = "ユーザー設定の過去バージョンとの統合処理に失敗しました。";

								return NiconicoSignInStatus.Failed;
							}


							try
							{
								Debug.WriteLine("initilize: fav");
								loginActivityLogger.LogEvent("initialize user favorite");
								FollowManager = await FollowManager.Create(this, LoginUserId);
							}
							catch
							{
								LoginErrorText = "お気に入り情報の取得に失敗しました。再起動をお試しください。";
								Debug.WriteLine(LoginErrorText);
								loginActivityLogger.LogEvent(LoginErrorText, fields, LoggingLevel.Error);
								NiconicoContext.Dispose();
                                NiconicoContext = new NiconicoContext();
                                return NiconicoSignInStatus.Failed;
							}

							FollowManagerUpdater = BackgroundUpdater.RegistrationBackgroundUpdateScheduleHandler(
								FollowManager,
								"FollowManager",
								label: "フォロー"
							);


							Debug.WriteLine("Login done.");
							loginActivityLogger.LogEvent("[Success]: Login done");
						}

                        // BG更新をスケジュール
                        UpdateAllComponent();

						// 動画のキャッシュフォルダの選択状態をチェック
						await (App.Current as App).CheckVideoCacheFolderState();


						// サインイン完了
						OnSignin?.Invoke();

						// TODO: 途中だった動画のダウンロードを再開
						// await MediaManager.StartBackgroundDownload();

                        

                        // ニコニコサービスの裏で取得させたいので強制的に待ちを挟む
                        await Task.Delay(1000);
                    }
					else
					{
						Debug.WriteLine("login failed");
                        NiconicoContext?.Dispose();
                        NiconicoContext = null;
                    }

					return result;
				}
				finally
				{
					_SigninLock.Release();
				}
				
			});
			
		}

		public void UpdateAllComponent()
		{
            FollowManagerUpdater.ScheduleUpdate();
            MylistManagerUpdater.ScheduleUpdate();
		}




		public async Task<NiconicoSignInStatus> SignOut()
		{
            NiconicoSignInStatus result = NiconicoSignInStatus.Failed;

            try
			{
				await _SigninLock.WaitAsync();

                if (NiconicoContext == null)
				{
					return result;
				}

				// 全てのバックグラウンド処理をキャンセル
				BackgroundUpdater.CancelAll();

                
                try
                {
                    MediaManager.StopCacheDownload();
				}
				catch { }

				try
				{
                    if (Util.InternetConnection.IsInternet())
                    {
                        result = await NiconicoContext.SignOutOffAsync();
                    }
                    else
                    {
                        result = NiconicoSignInStatus.Success;
                    }

                    NiconicoContext.Dispose();
				}
				finally
				{
                    NiconicoContext = new NiconicoContext();

					FollowManager = null;
					LoginUserId = uint.MaxValue;

					OnSignout?.Invoke();
				}

			}
			finally
			{
                UpdateServiceStatus();

                _SigninLock.Release();
            }

            return result;
        }

        private void UpdateServiceStatus(NiconicoSignInStatus status = NiconicoSignInStatus.Failed)
        {
            var isOnline = Util.InternetConnection.IsInternet();
            if (status == NiconicoSignInStatus.Success)
            {
                ServiceStatus = HohoemaAppServiceLevel.LoggedIn;
            }
            else if (isOnline)
            {
                if (status == NiconicoSignInStatus.ServiceUnavailable)
                {
                    ServiceStatus = HohoemaAppServiceLevel.OnlineButServiceUnavailable;
                }
                else
                {
                    ServiceStatus = HohoemaAppServiceLevel.OnlineWithoutLoggedIn;
                }
            }
            else
            {
                ServiceStatus = HohoemaAppServiceLevel.Offline;
            }

            OnPropertyChanged(nameof(IsLoggedIn));
        }

        public async Task<NiconicoSignInStatus> CheckSignedInStatus()
		{
            NiconicoSignInStatus result = NiconicoSignInStatus.Failed;

            try
			{
				await _SigninLock.WaitAsync();

                if (Util.InternetConnection.IsInternet() && NiconicoContext != null)
				{
                    result = await ConnectionRetryUtil.TaskWithRetry(
						() => NiconicoContext.GetIsSignedInAsync()
						, retryInterval:1000
						);
				}
			}
			catch
			{
                // ログイン処理時には例外を捕捉するが、ログイン状態チェックでは例外は無視する
                result = NiconicoSignInStatus.Failed;
            }
			finally
			{
                UpdateServiceStatus(result);

                _SigninLock.Release();
			}

            return result;
		}



        #endregion






        StorageFolder _DownloadFolder;

		public async Task<bool> IsAvailableUserDataFolder()
		{
			var folder = await GetCurrentUserDataFolder();

			return folder != null;
		}


		
		private async Task CacheFolderMigration(StorageFolder newVideoFolder)
		{
			if (_DownloadFolder?.Path != newVideoFolder.Path)
			{
                // フォルダーの移行作業を開始

                // 現在あるダウンロードタスクは必ず終了させる必要があります
                MediaManager?.StopCacheDownload();

                // v0.4.0以降の移行処理
                if (_DownloadFolder != null)
				{
					await MoveFiles(_DownloadFolder, newVideoFolder);
				}


				// v0.3.9以前 からの移行処理
				if (_DownloadFolder != null)
				{
					var oldSaveFolder = _DownloadFolder;
					var oldVideoSaveFolder = (await oldSaveFolder.TryGetItemAsync("video")) as StorageFolder;
					if (oldVideoSaveFolder != null)
					{
						var newVideoSaveFolder = newVideoFolder;
						await MoveFiles(oldVideoSaveFolder, newVideoSaveFolder);
					}

					// DL/Hohoema/{Userid}/Fav/Feed の内容を AppData/Hohoema/LocalState/Feedに移動
					var oldFavSaveFolder = (await oldSaveFolder.TryGetItemAsync("fav")) as StorageFolder;
					if (oldFavSaveFolder != null)
					{
						var oldFeedSaveFolder = (await oldFavSaveFolder.TryGetItemAsync("feed")) as StorageFolder;
						if (oldFeedSaveFolder != null)
						{
							await MoveFiles(oldFeedSaveFolder, await GetFeedDataFolder());
						}
					}
				}



				#region v0.3.3 からの移行処理


				// Note: v0.3.3以前にLocalFolderに作成されていたフォルダが存在する場合、そのフォルダの内容を
				// _DownlaodFolderに移動させます

				{
					var oldVersionDataFolder = await GetLegacyUserDataFolder(LoginUserId.ToString());
					if (oldVersionDataFolder != null)
					{
						var oldVideoSaveFolder = (await oldVersionDataFolder.TryGetItemAsync("video")) as StorageFolder;
						if (oldVideoSaveFolder != null)
						{
							//						var newVideoSaveFolder = await newVideoFolder.CreateFolderAsync("video", CreationCollisionOption.OpenIfExists);
							await MoveFiles(oldVideoSaveFolder, newVideoFolder);
						}
					}

					// favの保存は v0.3.10 or v0.4.0からは廃止しています
					//					var oldFavSaveFolder = (await oldVersionDataFolder.TryGetItemAsync("fav")) as StorageFolder;
					//					if (oldFavSaveFolder != null)
					{
//						var newFavSaveFolder = await newVideoFolder.CreateFolderAsync("fav", CreationCollisionOption.OpenIfExists);
//						await MoveFiles(oldFavSaveFolder, newFavSaveFolder);
					}
				}

				#endregion


				try
				{
					await MediaManager.OnCacheFolderChanged();
				}
				catch (Exception ex) { Debug.WriteLine(ex.ToString()); }
			}

			_DownloadFolder = newVideoFolder;
		}


		private async Task MoveFiles(StorageFolder source, StorageFolder dest)
		{
			try
			{
				// ファイルを全部移動させて
				var files = await source.GetFilesAsync();
				foreach (var file in files)
				{
					try
					{
						await file.MoveAsync(dest);
					}
					catch { }
				}

				// 古いフォルダを削除
				var remainSomething = await source.GetItemsAsync();
				if (remainSomething.Count == 0)
				{
					await source.DeleteAsync(StorageDeleteOption.PermanentDelete);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.ToString());
			}
		}

		


		
		public async Task<bool> ChangeUserDataFolder()
		{
            MediaManager.StopCacheDownload();


			try
			{
				var folderPicker = new Windows.Storage.Pickers.FolderPicker();
				folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
				folderPicker.FileTypeFilter.Add("*");

				var folder = await folderPicker.PickSingleFolderAsync();
				if (folder != null && folder.Path != _DownloadFolder?.Path)
				{
					try
					{
						await CacheFolderMigration(folder);
					}
					catch (Exception ex)
					{
						Debug.WriteLine(ex.ToString());
					}

					if (false == String.IsNullOrWhiteSpace(CurrentFolderAccessToken))
					{
						Windows.Storage.AccessCache.StorageApplicationPermissions.
						FutureAccessList.Remove(CurrentFolderAccessToken);
						CurrentFolderAccessToken = null;
					}

					Windows.Storage.AccessCache.StorageApplicationPermissions.
					FutureAccessList.AddOrReplace(FolderAccessToken, folder);

					_DownloadFolder = folder;

					CurrentFolderAccessToken = FolderAccessToken;
				}
				else
				{
					return false;
				}
			}
			catch
			{

			}
			finally
			{
				try
				{
					if (MediaManager != null)
					{
						await MediaManager.OnCacheFolderChanged();
					}
				}
				catch { }
			}

			return true;
		}






		#region lagacy user data folder access

		private static async Task<StorageFolder> GetLegacyUserDataFolder(string loginUserId)
		{
			var folderAccessToken = loginUserId;
			try
			{
				// v0.3.9以前のバージョンでの動作との整合性を取るためのコード
				// 以前はログインユーザーIDで

				if (Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.ContainsItem(folderAccessToken))
				{
					// 既にフォルダを指定済みの場合
					return await Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.GetFolderAsync(folderAccessToken);
				}
			}
			catch (Exception ex)
			{
				Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.Remove(folderAccessToken);

				Debug.WriteLine(ex.ToString());
			}

			return null;
		}

		private static async Task<bool> RemoveLegacyUserDataFolder(string loginUserId)
		{
			var folderAccessToken = loginUserId;
			try
			{
				// v0.3.9以前のバージョンでの動作との整合性を取るためのコード
				if (Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.ContainsItem(folderAccessToken))
				{
					var folder = await Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.GetFolderAsync(folderAccessToken);

					await folder.DeleteAsync();

					Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.Remove(folderAccessToken);
					
					return true;
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.ToString());
			}

			return false;
		}


		#endregion


		const string FolderAccessToken = "HohoemaVideoCache";

		// 旧バージョンで指定されたフォルダーでも動くようにするためにFolderAccessTokenを動的に扱う
		// 0.4.0以降はFolderAccessTokenで指定したトークンだが、
		// それ以前では ログインユーザーIDをトークンとして DL/Hohoema/ログインユーザーIDフォルダ/ をDLフォルダとして指定していた
		string CurrentFolderAccessToken = null;

		private async Task<StorageFolder> GetEnsureVideoFolder()
		{
			if (_DownloadFolder == null)
			{
				try
				{
					// 既にフォルダを指定済みの場合
					if (Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.ContainsItem(FolderAccessToken))
					{
						_DownloadFolder = await Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.GetFolderAsync(FolderAccessToken);
						CurrentFolderAccessToken = FolderAccessToken;
					}
				}
				catch (FileNotFoundException)
				{
					throw;
				}
				catch (Exception ex)
				{
					//					Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.Remove(FolderAccessToken);
					Debug.WriteLine(ex.ToString());
				}
			}

			// 旧バージョン利用ユーザーが新バージョンを利用しても問題なくDLフォルダにアクセスできるようにする
			if (_DownloadFolder == null && Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.ContainsItem(LoginUserId.ToString()))
			{
				var token = LoginUserId.ToString();
				_DownloadFolder = await Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.GetFolderAsync(token);
				CurrentFolderAccessToken = token;
			}

			return _DownloadFolder;
		}


		public async Task<bool> CanReadAccessVideoCacheFolder()
		{
			var status = await GetVideoCacheFolderState();
			return status == CacheFolderAccessState.Exist || status == CacheFolderAccessState.NotEnabled;
		}

		public async Task<bool> CanWriteAccessVideoCacheFolder()
		{
			var status = await GetVideoCacheFolderState();
			return status == CacheFolderAccessState.Exist;
		}

		public async Task<CacheFolderAccessState> GetVideoCacheFolderState()
		{
			if (false == UserSettings.CacheSettings.IsUserAcceptedCache)
			{
				return CacheFolderAccessState.NotAccepted;
			}

			try
			{
				var videoFolder = await GetEnsureVideoFolder();

				if (videoFolder == null)
				{
					return CacheFolderAccessState.NotSelected;
				}
			}
			catch (FileNotFoundException)
			{
				return CacheFolderAccessState.SelectedButNotExist;
			}

			if (false == UserSettings.CacheSettings.IsEnableCache)
			{
				return CacheFolderAccessState.NotEnabled;
			}
			else
			{
				return CacheFolderAccessState.Exist;
			}
		}



		public async Task<StorageFolder> GetVideoCacheFolder()
		{
			try
			{
				return await GetEnsureVideoFolder();
			}
			catch (FileNotFoundException)
			{
				return null;
			}
		}



		public Task<StorageFolder> GetApplicationLocalDataFolder()
		{
			return Task.FromResult(ApplicationData.Current.LocalFolder);
		}


		public Task<StorageFolder> GetCurrentUserDataFolder()
		{
			return ApplicationData.Current.LocalFolder.CreateFolderAsync(LoginUserId.ToString(), CreationCollisionOption.OpenIfExists).AsTask();
		}

		public async Task<StorageFolder> GetFeedDataFolder()
		{
			var folder = await GetApplicationLocalDataFolder();

			if (folder == null) { return null; }

			return await folder.CreateFolderAsync("feed", CreationCollisionOption.OpenIfExists);
		}


		public void Dispose()
		{
			MediaManager?.Dispose();
			LoggingChannel?.Dispose();
			BackgroundUpdater?.Dispose();
		}


		


		public bool IsLoggedIn
		{
			get
			{
				return ServiceStatus.IsLoggedIn();
			}
		}

        public HohoemaUserSettings UserSettings { get; private set; }

		public uint LoginUserId { get; private set; }
		public bool IsPremiumUser { get; private set; }
		public string LoginUserName { get; private set; }
        public string UserIconUrl { get; private set; }

		private NiconicoContext _NiconicoContext;
		public NiconicoContext NiconicoContext
		{
			get { return _NiconicoContext; }
			set { SetProperty(ref _NiconicoContext, value); }
		}

		public NiconicoMediaManager MediaManager { get; private set; }

		public NiconicoContentFinder ContentFinder { get; private set; }

		private FollowManager _FollowManager;
		public FollowManager FollowManager
		{
			get { return _FollowManager; }
			set { SetProperty(ref _FollowManager, value); }
		}

		private FeedManager _FeedManager;
		public FeedManager FeedManager
		{
			get { return _FeedManager; }
			set { SetProperty(ref _FeedManager, value); }
		}

        private HohoemaAppServiceLevel _ServiceStatus;
        public HohoemaAppServiceLevel ServiceStatus
        {
            get { return _ServiceStatus; }
            set { SetProperty(ref _ServiceStatus, value); }
        }


        public HohoemaPlaylist Playlist { get; private set; }

        public UserMylistManager UserMylistManager { get; private set; }



		public const string HohoemaUserAgent = "Hohoema_UWP";

		public IEventAggregator EventAggregator { get; private set; }


		public BackgroundUpdater BackgroundUpdater { get; private set; }

		public BackgroundUpdateScheduleHandler MylistManagerUpdater { get; private set; }
		public BackgroundUpdateScheduleHandler FeedManagerUpdater { get; private set; }
		public BackgroundUpdateScheduleHandler FollowManagerUpdater { get; private set; }
		public BackgroundUpdateScheduleHandler MediaManagerUpdater { get; private set; }
		

		public LoggingChannel LoggingChannel { get; private set; }


		public string LoginErrorText { get; private set; }

		public event Action OnSignout;
		public event Action OnSignin;
		public event Action OnResumed;



	}


	public enum CacheFolderAccessState
	{
		NotAccepted,
		NotEnabled,
		NotSelected,
		SelectedButNotExist,
		Exist
	}

	public class NGResult
	{
		public NGReason NGReason { get; set; }
		public string NGDescription { get; set; } = "";
		public string Content { get; set; }

		internal string GetReasonText()
		{
			switch (NGReason)
			{
				case NGReason.VideoId:
					return $"NG対象の動画ID : {Content}";
				case NGReason.UserId:
					return $"NG対象の投稿者ID : {Content}";
				case NGReason.Keyword:
					return $"NG対象のキーワード : {Content}";
				default:
					throw new NotSupportedException();
			}
		}
	}

	public enum NGReason
	{
		VideoId,
		UserId,
		Keyword,
		Tag,
	}
}
