﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// 空白ページのアイテム テンプレートについては、http://go.microsoft.com/fwlink/?LinkId=234238 を参照してください

namespace NicoPlayerHohoema.Views
{
	/// <summary>
	/// それ自体で使用できる空白ページまたはフレーム内に移動できる空白ページ。
	/// </summary>
	public sealed partial class PortalPage : Page
	{
		public PortalPage()
		{
			this.InitializeComponent();
		}
	}
	

	public class AppMapItemDataTemplateSelector : DataTemplateSelector
	{
		public DataTemplate CardContainer { get; set; }
		public DataTemplate TextListContainer { get; set; }
		public DataTemplate Item { get; set; }

		protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
		{
			if (item is ViewModels.AppMapContainerViewModel)
			{
				var casted = item as ViewModels.AppMapContainerViewModel;
				if (casted.OriginalContainer.ItemDisplayType == Models.AppMap.ContainerItemDisplayType.Card)
				{
					return CardContainer;
				}
				else
				{
					return TextListContainer;
				}
			}
			else if (item is ViewModels.AppMapItemViewModel)
			{
				return Item;
			}

			return base.SelectTemplateCore(item, container);
		}
	}
}
