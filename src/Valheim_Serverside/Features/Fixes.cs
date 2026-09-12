using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;

namespace Valheim_Serverside.Features
{
	/*
		Fixes for vanilla server behaviour that loses data.

		Valheim 1.0 saves the world in chunks and only rewrites the chunks it marked as changed.
		A chunk is marked when the server itself changes an object (ZDO.IncreaseDataRevision) or
		an object enters or leaves it. A change that arrives from a player for an object the player
		owns is applied through ZDO.Deserialize, which marks nothing, so until something else in
		that chunk changes the new state exists only in memory and is gone after a restart. With
		this mod the server owns nearly everything near players, so the window is small, but a
		player still owns what they just built, the ship they steer and their own drops.
		Reported for 1.0 by ValheimCommunityPatch ("Fix Unsaved Client Changes").
	*/
	public class Fixes : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.fixSaveClientChanges.Value;
		}

		[HarmonyPatch(typeof(ZDO), "Deserialize")]
		public static class ZDO_Deserialize_Patch
		{
			static void Postfix(ZDO __instance)
			{
				if (__instance.Persistent && ZNet.instance && ZNet.instance.IsServer() && ZDOMan.instance != null)
				{
					ZDOMan.instance.SetDirtySector(__instance);
				}
			}
		}
	}
}
