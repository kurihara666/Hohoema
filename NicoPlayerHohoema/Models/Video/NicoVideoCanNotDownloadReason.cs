﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NicoPlayerHohoema.Models
{
	public enum NicoVideoCanNotDownloadReason
	{
		Unknown,
		Offline,
		NotExist,
		OnlyLowQualityWithoutPremiumUser,
	}
}
