using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Timeline;

namespace OrcaCharacter;

internal static class OrcaEpochRegistry
{
	private static bool _done;

	internal static readonly EpochModel[] All = (EpochModel[])(object)new EpochModel[7]
	{
		new Orca1Epoch(),
		new Orca2Epoch(),
		new Orca3Epoch(),
		new Orca4Epoch(),
		new Orca5Epoch(),
		new Orca6Epoch(),
		new Orca7Epoch()
	};

	internal static void Register()
	{
		if (_done)
		{
			return;
		}
		_done = true;
		try
		{
			Type typeFromHandle = typeof(EpochModel);
			if (typeFromHandle.GetField("_allEpochs", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) is List<Type> list)
			{
				EpochModel[] all = All;
				foreach (EpochModel val in all)
				{
					if (!list.Contains(((object)val).GetType()))
					{
						list.Add(((object)val).GetType());
					}
				}
			}
			else
			{
				OrcaLog.Warn("[Orca] epoch 注册：拿不到 _allEpochs（时间线列表里可能看不到奥卡的格子）");
			}
			if (typeFromHandle.GetField("_epochTypeDictionary", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) is Dictionary<string, Type> dictionary)
			{
				EpochModel[] all = All;
				foreach (EpochModel val2 in all)
				{
					dictionary[val2.Id] = ((object)val2).GetType();
				}
			}
			if (typeFromHandle.GetField("_typeToIdDictionary", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) is Dictionary<Type, string> dictionary2)
			{
				EpochModel[] all = All;
				foreach (EpochModel val3 in all)
				{
					dictionary2[((object)val3).GetType()] = val3.Id;
				}
			}
			if (typeFromHandle.GetField("_epochIdsHashSet", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) is HashSet<string> hashSet)
			{
				EpochModel[] all = All;
				foreach (EpochModel val4 in all)
				{
					hashSet.Add(val4.Id);
				}
			}
			OrcaLog.Info($"[Orca] 时间线 epoch 已注册进原版表（{All.Length} 个）：" + string.Join(" / ", Array.ConvertAll(All, (EpochModel e) => e.Id)));
		}
		catch (Exception ex)
		{
			OrcaLog.Warn("[Orca] epoch 注册失败（时间线相关功能会退化，但不影响游戏）：" + ex.Message);
		}
	}
}
