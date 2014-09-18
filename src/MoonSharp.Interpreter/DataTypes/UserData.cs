﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using MoonSharp.Interpreter.Interop;

namespace MoonSharp.Interpreter
{
	public class UserData
	{
		private UserData()
		{ 
			// This type can only be instantiated using one of the Create methods
		}

		public object Object { get; set; }
		internal UserDataDescriptor Descriptor { get; set; }

		private static ReaderWriterLockSlim m_Lock = new ReaderWriterLockSlim();
		private static Dictionary<Type, UserDataDescriptor> s_Registry = new Dictionary<Type, UserDataDescriptor>();
		private static InteropAccessMode m_DefaultAccessMode;

		static UserData()
		{
			RegisterType<EnumerableWrapper>(InteropAccessMode.HideMembers);
			m_DefaultAccessMode = InteropAccessMode.LazyOptimized;
		}

		public static void RegisterType<T>(InteropAccessMode accessMode = InteropAccessMode.Default)
		{
			RegisterType_Impl(typeof(T), accessMode);
		}

		public static void RegisterType(Type type, InteropAccessMode accessMode = InteropAccessMode.Default)
		{
			RegisterType_Impl(type, accessMode);
		}

		public static DynValue Create(object o)
		{
			var descr = GetDescriptorForObject(o);
			if (descr == null) return null;

			return DynValue.NewUserData(new UserData()
				{
					Descriptor = descr,
					Object = o
				});
		}

		public static DynValue CreateStatic(Type t)
		{
			var descr = GetDescriptorForType(t, false);
			if (descr == null) return null;

			return DynValue.NewUserData(new UserData()
			{
				Descriptor = descr,
				Object = null
			});
		}

		public static DynValue CreateStatic<T>()
		{
			return CreateStatic(typeof(T));
		}

		public static InteropAccessMode DefaultAccessMode
		{
			get { return m_DefaultAccessMode; }
			set
			{
				if (value == InteropAccessMode.Default)
					throw new ArgumentException("DefaultAccessMode");

				m_DefaultAccessMode = value;
			}
		}



		private static void RegisterType_Impl(Type type, InteropAccessMode accessMode = InteropAccessMode.Default)
		{
			if (accessMode == InteropAccessMode.Default)
			{
				MoonSharpUserDataAttribute attr = type.GetCustomAttributes(true).OfType<MoonSharpUserDataAttribute>()
					.SingleOrDefault();

				if (attr != null)
					accessMode = attr.AccessMode;
			}


			if (accessMode == InteropAccessMode.Default)
				accessMode = m_DefaultAccessMode;

			m_Lock.EnterWriteLock();

			try
			{
				if (!s_Registry.ContainsKey(type))
				{
					UserDataDescriptor udd = new UserDataDescriptor(type, accessMode);
					s_Registry.Add(udd.Type, udd);

					if (accessMode == InteropAccessMode.BackgroundOptimized)
					{
						ThreadPool.QueueUserWorkItem(o => udd.Optimize());
					}
				}
			}
			finally
			{
				m_Lock.ExitWriteLock();
			}
		}

		private static UserDataDescriptor GetDescriptorForType<T>(bool deepSearch = true)
		{
			return GetDescriptorForType(typeof(T), deepSearch);
		}

		private static UserDataDescriptor GetDescriptorForType(Type type, bool deepSearch = true)
		{
			m_Lock.EnterReadLock();

			try
			{
				if (!deepSearch)
					return s_Registry.ContainsKey(type) ? s_Registry[type] : null;

				for (Type t = type; t != typeof(object); t = t.BaseType)
				{
					if (s_Registry.ContainsKey(t))
						return s_Registry[t];
				}

				foreach (Type t in type.GetInterfaces())
				{
					if (s_Registry.ContainsKey(t))
						return s_Registry[t];
				}

				if (s_Registry.ContainsKey(typeof(object)))
					return s_Registry[type];
			}
			finally
			{
				m_Lock.ExitReadLock();
			}

			return null;
		}


		private static UserDataDescriptor GetDescriptorForObject(object o)
		{
			return GetDescriptorForType(o.GetType(), true);
		}
	}
}