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

		Off by default: the shout is seen by everyone nearby, and the server console (ServerConsole)
		offers the same commands to whoever runs the server, e.g. from a panel such as AMP.
	*/
	public class AdminChat : IFeature
	{
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
			int amount = 1;
			if (words.Length >= 3 && !AdminCommands.TryParseAmount(words[2], out amount))
			{
				Reply(peer, $"'{words[2]}' is not a number");
				return;
			}
			Reply(peer, AdminCommands.Give(peer, words[1], amount, $"{peer.m_playerName} ({host})"));
		}

		private static void Save(ZNetPeer peer, string host)
		{
			Reply(peer, AdminCommands.Save($"{peer.m_playerName} ({host})"));
		}

		private static void Reply(ZNetPeer peer, string text)
		{
			peer.m_rpc.Invoke("RemotePrint", "[server] " + text);
		}

	}
}
