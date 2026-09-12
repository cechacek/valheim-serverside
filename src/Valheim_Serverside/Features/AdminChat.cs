using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		Admin commands typed into the game's chat, carried out by the server.

		Valheim 1.0 lets only the host use cheat commands: on a dedicated server an admin gets
		"not valid in the current context" from the console for spawn and the like, because
		Terminal.IsCheatsEnabled requires ZNet.IsServer. A shout does reach the server, as the
		routed "ChatMessage" RPC, so the server can act on it: the sender is checked against
		adminlist.txt and the answer goes to the sender's console (RemotePrint).

		  /give <item> [amount]   drops the items in front of the sender, stacked as the item allows
		  /save                   saves the world
		  /help                   lists these

		The shout itself is still seen by everyone nearby; that is the price of needing nothing on
		the client.
	*/
	public class AdminChat : IFeature
	{
		private static Dictionary<string, GameObject> s_items;
		private static bool s_checked;

		public bool FeatureEnabled()
		{
			return Configuration.adminChatEnabled.Value;
		}

		[HarmonyPatch(typeof(Chat), "RPC_ChatMessage")]
		public static class Chat_RPC_ChatMessage_Patch
		{
			static void Postfix(long sender, string text)
			{
				Handle(sender, text);
			}
		}

		// Chat is part of the game's main scene, on the server too. Should a game update drop it
		// there, the routed RPC is taken directly so the commands keep working.
		public static void Tick()
		{
			if (s_checked || !ZNet.instance || !ZNet.instance.IsServer() || !ZoneSystem.instance || !ZoneSystem.instance.LocationsGenerated)
			{
				return;
			}
			s_checked = true;
			if (Chat.instance)
			{
				return;
			}
			try
			{
				ZRoutedRpc.instance.Register<Vector3, int, UserInfo, string>("ChatMessage",
					(long sender, Vector3 position, int type, UserInfo user, string text) => Handle(sender, text));
				ServersidePlugin.logger.LogInfo("Admin chat: no Chat on this server, listening for chat messages directly");
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning($"Admin chat: cannot listen for chat messages, commands unavailable: {e.Message}");
			}
		}

		public static void Handle(long sender, string text)
		{
			try
			{
				string prefix = Configuration.adminChatPrefix.Value;
				if (!ZNet.instance || !ZNet.instance.IsServer() || string.IsNullOrEmpty(prefix) || text == null || !text.StartsWith(prefix, StringComparison.Ordinal))
				{
					return;
				}
				ZNetPeer peer = ZNet.instance.GetPeer(sender);
				if (peer == null || peer.m_socket == null)
				{
					return;
				}
				string host = peer.m_socket.GetHostName();
				string[] words = text.Substring(prefix.Length).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (words.Length == 0)
				{
					return;
				}
				if (!ZNet.instance.IsAdmin(host))
				{
					ServersidePlugin.logger.LogInfo($"Admin chat: {peer.m_playerName} ({host}) is not an admin, ignored '{text}'");
					Reply(peer, "You are not admin");
					return;
				}
				switch (words[0].ToLowerInvariant())
				{
					case "give":
						Give(peer, host, words);
						break;
					case "save":
						Save(peer, host);
						break;
					case "help":
						Reply(peer, $"{prefix}give <item> [amount] | {prefix}save | {prefix}help");
						break;
					default:
						Reply(peer, $"Unknown command '{words[0]}'. {prefix}help lists them.");
						break;
				}
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning($"Admin chat: '{text}' failed: {e}");
			}
		}

		private static void Give(ZNetPeer peer, string host, string[] words)
		{
			if (words.Length < 2)
			{
				Reply(peer, "Usage: give <item> [amount]");
				return;
			}
			GameObject prefab = FindItem(words[1]);
			if (!prefab)
			{
				Reply(peer, $"No item '{words[1]}'.{Suggest(words[1])}");
				return;
			}
			int amount = 1;
			if (words.Length >= 3 && !int.TryParse(words[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out amount))
			{
				Reply(peer, $"'{words[2]}' is not a number");
				return;
			}
			amount = Mathf.Clamp(amount, 1, Mathf.Max(1, Configuration.adminChatMaxGive.Value));

			ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
			Vector3 origin = (character != null ? character.GetPosition() : peer.GetRefPos()) + Vector3.up * 1.5f;
			int maxStack = Mathf.Max(1, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
			int stacks = 0;
			for (int left = amount; left > 0; left -= maxStack)
			{
				Vector2 spread = UnityEngine.Random.insideUnitCircle * 0.75f;
				Vector3 position = origin + new Vector3(spread.x, 0.25f * stacks, spread.y);
				GameObject go = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
				go.GetComponent<ItemDrop>().SetStack(Mathf.Min(left, maxStack));
				stacks++;
			}
			ServersidePlugin.logger.LogInfo($"Admin chat: {peer.m_playerName} ({host}) received {amount} x {prefab.name} in {stacks} stack(s) at {origin.x:0},{origin.z:0}");
			Reply(peer, $"Dropped {amount} x {prefab.name} in {stacks} stack(s)");
		}

		private static void Save(ZNetPeer peer, string host)
		{
			if (!ZNet.instance.EnoughDiskSpaceAvailable(out bool _))
			{
				Reply(peer, "Not enough disk space, world not saved");
				return;
			}
			// The same call as the autosave; the vanilla `save` command's path throws on a dedicated server.
			ServersidePlugin.logger.LogInfo($"Admin chat: {peer.m_playerName} ({host}) requested a world save");
			ZNet.instance.Save(sync: false, saveOtherPlayerProfiles: true, waitForNextFrame: true);
			Reply(peer, "Saving world");
		}

		private static void Reply(ZNetPeer peer, string text)
		{
			peer.m_rpc.Invoke("RemotePrint", "[server] " + text);
		}

		private static GameObject FindItem(string name)
		{
			if (s_items == null)
			{
				s_items = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
				foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
				{
					if (prefab && prefab.GetComponent<ItemDrop>() && !s_items.ContainsKey(prefab.name))
					{
						s_items[prefab.name] = prefab;
					}
				}
			}
			return s_items.TryGetValue(name, out GameObject found) ? found : null;
		}

		private static string Suggest(string name)
		{
			List<string> similar = new List<string>();
			foreach (string key in s_items.Keys)
			{
				if (key.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					similar.Add(key);
					if (similar.Count == 8)
					{
						break;
					}
				}
			}
			return similar.Count > 0 ? " Similar: " + string.Join(", ", similar) : "";
		}
	}
}
