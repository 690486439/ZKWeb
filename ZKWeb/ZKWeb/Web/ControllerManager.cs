﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.FastReflection;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using ZKWebStandard.Collections;
using ZKWebStandard.Extensions;
using ZKWeb.Web.ActionResults;
using ZKWeb.Web;
using ZKWebStandard.Web;

namespace ZKWeb.Web {
	/// <summary>
	/// 控制器管理器
	/// </summary>
	public class ControllerManager : IHttpRequestHandler {
		/// <summary>
		/// { (路径, 类型): 处理函数, ... }
		/// </summary>
		protected IDictionary<Pair<string, string>, Func<IActionResult>> Actions { get; set; }

		/// <summary>
		/// 初始化
		/// </summary>
		public ControllerManager() {
			Actions = new ConcurrentDictionary<Pair<string, string>, Func<IActionResult>>();
		}

		/// <summary>
		/// 处理http请求
		/// 查找路径对应的处理函数，存在时使用该函数否则跳过处理
		/// </summary>
		public virtual void OnRequest() {
			var context = HttpManager.CurrentContext;
			var action = GetAction(context.Request.Path, context.Request.Method);
			if (action != null) {
				var result = action();
				// 写入回应
				result.WriteResponse(context.Response);
				// 清理资源
				if (result is IDisposable) {
					((IDisposable)result).Dispose();
				}
				// 结束回应
				context.Response.End();
			}
		}

		/// <summary>
		/// 注册控制器类型
		/// </summary>
		/// <typeparam name="T">控制器类型</typeparam>
		public virtual void RegisterController<T>() {
			RegisterController(typeof(T));
		}

		/// <summary>
		/// 注册控制器类型
		/// </summary>
		/// <param name="type">控制器类型</param>
		public virtual void RegisterController(Type type) {
			// 枚举所有带ActionAttribute的属性的公开函数
			foreach (var method in type.FastGetMethods(
				BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)) {
				var attributes = method.GetCustomAttributes<ActionAttribute>();
				if (attributes.Any()) {
					// 预先编译好函数，提高实际调用时的性能
					var makeInstance = method.IsStatic ? null :
						Expression.New(type.GetTypeInfo().GetConstructors()[0]);
					Func<IActionResult> action;
					var actionResultType = typeof(IActionResult).GetTypeInfo();
					if (actionResultType.IsAssignableFrom(method.ReturnType)) {
						action = Expression.Lambda<Func<IActionResult>>(
							Expression.Call(makeInstance, method)).Compile();
					} else {
						var plainResultType = typeof(PlainResult).GetTypeInfo();
						action = Expression.Lambda<Func<IActionResult>>(
							Expression.New(plainResultType.GetConstructors()[0],
							Expression.Call(makeInstance, method))).Compile();
					}
					// 添加函数
					foreach (var attribute in attributes) {
						RegisterAction(attribute.Path, attribute.Method, action, attribute.OverrideExists);
					}
				}
			}
		}

		/// <summary>
		/// 正规化路径
		/// 路径前没有/时添加/
		/// 路径后有/时去除/
		/// </summary>
		/// <param name="path">路径</param>
		/// <returns></returns>
		public virtual string NormalizePath(string path) {
			if (path.Length > 1 && path.EndsWith("/")) {
				path = path.TrimEnd('/');
			}
			if (!path.StartsWith("/")) {
				path = "/" + path;
			}
			return path;
		}

		/// <summary>
		/// 注册单个http请求的处理函数
		/// </summary>
		/// <param name="path">路径</param>
		/// <param name="method">请求类型</param>
		/// <param name="action">处理函数</param>
		public virtual void RegisterAction(string path, string method, Func<IActionResult> action) {
			RegisterAction(path, method, action, false);
		}

		/// <summary>
		/// 注册单个http请求的处理函数
		/// </summary>
		/// <param name="path">路径</param>
		/// <param name="method">请求类型</param>
		/// <param name="action">处理函数</param>
		/// <param name="overrideExists">是否覆盖相同路径的函数</param>
		public virtual void RegisterAction(
			string path, string method, Func<IActionResult> action, bool overrideExists) {
			path = NormalizePath(path);
			var key = Pair.Create(path, method);
			if (!overrideExists && Actions.ContainsKey(key)) {
				throw new ArgumentException($"action for {path} already registered, try option `overrideExists`");
			}
			Actions[key] = action;
		}

		/// <summary>
		/// 注销单个http请求的处理函数
		/// </summary>
		/// <param name="path">路径</param>
		/// <param name="method">请求类型</param>
		/// <returns></returns>
		public virtual bool UnregisterAction(string path, string method) {
			path = NormalizePath(path);
			var key = Pair.Create(path, method);
			return Actions.Remove(key);
		}

		/// <summary>
		/// 获取路径和请求类型对应的处理函数
		/// 找不到时返回null
		/// </summary>
		/// <param name="path">路径</param>
		/// <param name="method">请求类型</param>
		/// <returns></returns>
		public virtual Func<IActionResult> GetAction(string path, string method) {
			path = NormalizePath(path);
			var key = Pair.Create(path, method);
			return Actions.GetOrDefault(key);
		}

		/// <summary>
		/// 初始化控制器管理器
		/// 添加在Ioc容器中注册的控制器下的处理函数
		/// </summary>
		internal static void Initialize() {
			var controllerManager = Application.Ioc.Resolve<ControllerManager>();
			foreach (var controller in Application.Ioc.ResolveMany<IController>()) {
				controllerManager.RegisterController(controller.GetType());
			}
		}
	}
}
