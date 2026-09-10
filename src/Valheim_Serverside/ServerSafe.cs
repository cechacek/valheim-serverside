using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Valheim_Serverside
{
	/*
		Valheim 1.0 added `Player.m_localPlayer.X()` calls to code that in vanilla only ever
		runs on a client -- the owner of an object, or every instance of it. Once this mod makes
		the dedicated server instantiate and own world objects, that code runs on the server too,
		where Player.m_localPlayer is always null.

		The helpers below stand in for those member calls and return what "no local player"
		should mean. `NullSafeLocalPlayer` is a transpiler body that swaps them in.
	*/
	public static class ServerSafe
	{
		public static ZDOID LocalPlayerZDOID()
		{
			return Player.m_localPlayer ? Player.m_localPlayer.GetZDOID() : ZDOID.None;
		}

		public static long LocalPlayerID()
		{
			return Player.m_localPlayer ? Player.m_localPlayer.GetPlayerID() : 0L;
		}

		public static string LocalPlayerName()
		{
			return Player.m_localPlayer ? Player.m_localPlayer.GetPlayerName() : "";
		}

		private static readonly Dictionary<string, MethodInfo> Replacements = new Dictionary<string, MethodInfo>
		{
			{ nameof(Character.GetZDOID), AccessTools.Method(typeof(ServerSafe), nameof(LocalPlayerZDOID)) },
			{ nameof(Player.GetPlayerID), AccessTools.Method(typeof(ServerSafe), nameof(LocalPlayerID)) },
			{ nameof(Player.GetPlayerName), AccessTools.Method(typeof(ServerSafe), nameof(LocalPlayerName)) },
		};

		/*
			Replaces each `ldsfld Player::m_localPlayer; callvirt GetZDOID|GetPlayerID|GetPlayerName`
			pair with a call to the matching helper, carrying over labels and exception blocks so
			branch targets stay intact.

			`expected` is how many pairs the method had when the patch was written. A different count
			means the game changed the method, so it is logged -- a transpiler that silently stops
			matching is exactly how these regressions go unnoticed.
		*/
		public static IEnumerable<CodeInstruction> NullSafeLocalPlayer(IEnumerable<CodeInstruction> instructions, MethodBase original, int expected)
		{
			FieldInfo localPlayer = AccessTools.Field(typeof(Player), nameof(Player.m_localPlayer));
			List<CodeInstruction> codes = instructions.ToList();
			int replaced = 0;
			for (int i = 0; i < codes.Count - 1; i++)
			{
				if (!codes[i].LoadsField(localPlayer)
					|| codes[i + 1].opcode != OpCodes.Callvirt
					|| !(codes[i + 1].operand is MethodInfo callee)
					|| !callee.DeclaringType.IsAssignableFrom(typeof(Player))
					|| !Replacements.TryGetValue(callee.Name, out MethodInfo helper))
				{
					continue;
				}
				CodeInstruction call = new CodeInstruction(OpCodes.Call, helper);
				call.labels.AddRange(codes[i].labels);
				call.labels.AddRange(codes[i + 1].labels);
				call.blocks.AddRange(codes[i].blocks);
				call.blocks.AddRange(codes[i + 1].blocks);
				codes[i] = call;
				codes.RemoveAt(i + 1);
				replaced++;
			}
			if (replaced != expected)
			{
				ServersidePlugin.logger.LogWarning($"{original.DeclaringType.Name}.{original.Name}: replaced {replaced} Player.m_localPlayer call(s), expected {expected}. The game changed this method; the patch needs reviewing.");
			}
			return codes;
		}
	}
}
