﻿using System;
using System.Threading;
using ZKWeb.Logging;
using ZKWeb.Server;
using ZKWebStandard.Extensions;
using ZKWebStandard.Utils;

namespace ZKWeb.Cache {
	/// <summary>
	/// 自动清理缓存的类
	/// </summary>
	internal static class AutomaticCacheCleaner {
		/// <summary>
		/// 启用自动清理缓存
		/// </summary>
		internal static void Start() {
			// 在内存占用超过设置值时自动清理缓存+回收内存
			// 检查间隔默认是15秒
			var configManager = Application.Ioc.Resolve<ConfigManager>();
			var thresholdMb = configManager.WebsiteConfig.Extra.GetOrDefault<int?>(
				ExtraConfigKeys.ClearCacheAfterUsedMemoryMoreThan);
			var intervalMs = (configManager.WebsiteConfig.Extra.GetOrDefault<int?>(
				ExtraConfigKeys.CleanCacheCheckInterval) ?? 15) * 1000;
			if (thresholdMb == null) {
				// 没有设置时不启用此功能
				return;
			}
			var thread = new Thread(() => {
				while (true) {
					Thread.Sleep(intervalMs);
					try {
						var usedMemory = SystemUtils.GetUsedMemoryBytes() / 1024 / 1024;
						if (usedMemory > thresholdMb.Value) {
							// 清理缓存+回收内存
							var cleaners = Application.Ioc.ResolveMany<ICacheCleaner>();
							cleaners.ForEach(c => c.ClearCache());
							GC.Collect();
						}
					} catch (Exception e) {
						var logManager = Application.Ioc.Resolve<LogManager>();
						logManager.LogError(e.ToString());
					}
				}
			});
			thread.IsBackground = true;
			thread.Start();
		}
	}
}
