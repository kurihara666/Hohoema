﻿using Mntone.Nico2;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NicoPlayerHohoema.Models
{
	abstract public class FavInfoGroupBaseTemplate<FAV_SOURCE> : FavInfoGroupBase
	{
		public FavInfoGroupBaseTemplate(HohoemaApp hohoemaApp, List<FavInfo> items) 
			: base(hohoemaApp, items)
		{

		}

		abstract protected Task<List<FAV_SOURCE>> GetFavSource();
		abstract protected string FavSourceToItemId(FAV_SOURCE source);
		abstract protected FavInfo ConvertToFavInfo(FAV_SOURCE source);


		


		public override async Task Sync()
		{
			var userFavDatas = await GetFavSource();

			// まだローカルデータとして登録されていないIDを追加分として抽出
			var addedItems = userFavDatas
				.Where(x =>
				{
					var itemId = FavSourceToItemId(x);
					return _FavInfoList.All(y => y.Id != itemId);
				})
				.Select(ConvertToFavInfo);

			foreach (var addItem in addedItems)
			{
				_FavInfoList.Add(addItem);
			}


			// オンラインデータから削除されているアイテムを抽出
			var itemIds = userFavDatas.Select(FavSourceToItemId).ToArray();
			var removedItems = _FavInfoList
				.Where(x => !itemIds.Any(y => x.Id == y))
				.ToList();
			foreach (var removeItem in removedItems)
			{
				_FavInfoList.Remove(removeItem);
			}
		}


		

	}
}