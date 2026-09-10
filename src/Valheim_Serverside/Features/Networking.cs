using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		The server-side part of BetterNetworking by CW-Jesse (MIT,
		https://github.com/CW-Jesse/valheim-betternetworking): raises the two limits on how fast the
		server sends world data to each player.

		With this mod the server sends the updates of every object it simulates, so the limits are
		reached sooner than on a vanilla server -- mostly in bursts, when a player enters a new area
		or comes out of a portal. Clients stay vanilla. BetterNetworking's compression is not
		included: it needs the mod on both ends.
	*/
	public class Networking : IFeature
	{
		private const int VanillaQueueSize = 10240;

		public bool FeatureEnabled()
		{
			return Configuration.networkingEnabled.Value;
		}

		public static int QueueSize()
		{
			return Mathf.Clamp(Configuration.networkQueueSizeKB.Value, 10, 80) * 1024;
		}

		[HarmonyPatch(typeof(ZDOMan), "SendZDOs")]
		public static class ZDOMan_SendZDOs_Patch
		/*
			SendZDOs sends nothing to a peer while more than 10 KB is queued for it, and otherwise sends
			at most 10 KB minus what is queued. It runs 20 times a second, so ~200 KB/s per player at
			best. Both uses of the constant are replaced by the configured size.
		*/
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
			{
				MethodInfo queueSize = AccessTools.Method(typeof(Networking), nameof(QueueSize));
				List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
				int replaced = 0;
				for (int i = 0; i < codes.Count; i++)
				{
					if (codes[i].LoadsConstant(VanillaQueueSize))
					{
						CodeInstruction call = new CodeInstruction(OpCodes.Call, queueSize);
						call.labels.AddRange(codes[i].labels);
						call.blocks.AddRange(codes[i].blocks);
						codes[i] = call;
						replaced++;
					}
				}
				if (replaced != 2)
				{
					ServersidePlugin.logger.LogWarning($"{__originalMethod.DeclaringType.Name}.{__originalMethod.Name}: replaced {replaced} send queue limit(s), expected 2. The game changed this method; the patch needs reviewing.");
				}
				return codes;
			}

			static void Prefix(ZDOMan.ZDOPeer peer, bool flush)
			{
				Stats.Record(peer.m_peer, flush);
			}
		}

		[HarmonyPatch(typeof(ZSteamSocket), "RegisterGlobalCallbacks")]
		public static class ZSteamSocket_RegisterGlobalCallbacks_Patch
		/*
			Valheim sets Steam's send rate to exactly 150 KB/s per connection here. Set the configured
			range after it. Crossplay (PlayFab) connections do not use this setting.
		*/
		{
			static void Postfix()
			{
				int max = Configuration.networkSendRateMaxKB.Value * 1024;
				int min = Math.Min(Configuration.networkSendRateMinKB.Value * 1024, max);
				SetSteamConfig(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, min);
				SetSteamConfig(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, max);
				ServersidePlugin.logger.LogInfo($"Networking: Steam send rate {min / 1024}-{max / 1024} KB/s, send queue {QueueSize() / 1024} KB per player");
			}

			private static void SetSteamConfig(ESteamNetworkingConfigValue key, int value)
			{
				GCHandle handle = GCHandle.Alloc(value, GCHandleType.Pinned);
				try
				{
					SteamGameServerNetworkingUtils.SetConfigValue(key, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, handle.AddrOfPinnedObject());
				}
				finally
				{
					handle.Free();
				}
			}
		}

		/*
			How often each player's send queue was too full for SendZDOs to send anything, so the
			effect of QueueSizeKB can be measured instead of guessed.
		*/
		private static class Stats
		{
			private class PeerStats
			{
				public string name;
				public int ticks;
				public int full;
				public int maxQueued;
			}

			private static readonly Dictionary<long, PeerStats> s_peers = new Dictionary<long, PeerStats>();
			private static float s_nextReport = -1f;

			public static void Record(ZNetPeer peer, bool flush)
			{
				int minutes = Configuration.networkStatsMinutes.Value;
				if (minutes <= 0 || flush || peer == null || peer.m_socket == null)
				{
					return;
				}
				int queued = peer.m_socket.GetSendQueueSize();
				if (!s_peers.TryGetValue(peer.m_uid, out PeerStats stats))
				{
					stats = new PeerStats();
					s_peers[peer.m_uid] = stats;
				}
				stats.name = peer.m_playerName;
				stats.ticks++;
				// The same test SendZDOs makes: under 2 KB of room left means nothing is sent.
				if (QueueSize() - queued < 2048)
				{
					stats.full++;
				}
				stats.maxQueued = Math.Max(stats.maxQueued, queued);

				float now = Time.realtimeSinceStartup;
				if (s_nextReport < 0f)
				{
					s_nextReport = now + minutes * 60f;
				}
				else if (now >= s_nextReport)
				{
					Report();
					s_nextReport = now + minutes * 60f;
				}
			}

			private static void Report()
			{
				foreach (PeerStats stats in s_peers.Values)
				{
					ServersidePlugin.logger.LogInfo($"Networking {stats.name}: send queue full in {100f * stats.full / stats.ticks:0.0}% of {stats.ticks} send ticks, at most {stats.maxQueued / 1024} of {QueueSize() / 1024} KB queued");
				}
				s_peers.Clear();
			}
		}
	}
}
