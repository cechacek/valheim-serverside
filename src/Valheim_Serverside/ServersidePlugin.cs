using BepInEx;
using BepInEx.Logging;
using FeaturesLib;
using HarmonyLib;
using PatchingLib;
using PluginConfiguration;
using Requirements;
using System;
using System.Collections.Generic;

namespace Valheim_Serverside
{

	[Harmony]
	[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
	[BepInDependency(ValheimPlusPluginId, BepInDependency.DependencyFlags.SoftDependency)]

	public class ServersidePlugin : BaseUnityPlugin
	{
		// Kept from Serverside Simulations (the original this is forked from), so other mods that
		// detect it by GUID still do and the two cannot be loaded side by side.
		public const string PluginGUID = "MVP.Valheim_Serverside_Simulations";
		public const string PluginName = "Sarkastic.eu Dedicated Simulation";
		public const string PluginVersion = "1.3.0";

		private static ServersidePlugin context;

		public static Configuration configuration;

		public static Harmony harmony;

		public const string ValheimPlusPluginId = "org.bepinex.plugins.valheim_plus";

		public static ManualLogSource logger;

		private void Awake()
		{
			context = this;
			logger = Logger;

			Configuration.Load(Config);

			if (!ModIsEnabled())
			{
				Logger.LogInfo($"{PluginName} is disabled. (configuration)");
				return;
			}
			else if (!IsDedicated())
			{
				Logger.LogInfo($"{PluginName} is disabled. (not a dedicated server)");
				return;
			}
			Logger.LogInfo($"Installing {PluginName}");

			harmony = new Harmony(PluginGUID);

			AvailableFeatures availableFeatures = new AvailableFeatures();
			availableFeatures.AddFeature(new Features.Core());
			availableFeatures.AddFeature(new Features.MaxObjectsPerFrame());
			availableFeatures.AddFeature(new Features.Networking());
			availableFeatures.AddFeature(new Features.Debugging());
			availableFeatures.AddFeature(new Features.Compat_ValheimPlus());

			PatchRequirements patchRequirements = new PatchRequirements();
			patchRequirements.AddRequirement(new PatchRequirement.DebugBuild());

			if (!PatchFeatures(availableFeatures, new HarmonyFeaturesPatcher(patchRequirements)))
			{
				return;
			}

			VanillaDrift.Check(Logger);
			Logger.LogInfo($"{PluginName} installed");
		}

		/*
			Each feature is patched through its own Harmony instance so a failure can be undone
			cleanly. A patch that fails usually means the game changed under it. Half of Core is
			worse than none -- e.g. objects created around players while zones are not -- so a
			Core failure removes every patch and leaves the server vanilla. Any other feature is
			just switched off.
		*/
		private bool PatchFeatures(AvailableFeatures availableFeatures, HarmonyFeaturesPatcher patcher)
		{
			List<Harmony> applied = new List<Harmony>();
			foreach (IFeature feature in availableFeatures.EnabledFeatures())
			{
				string featureName = feature.GetType().Name;
				Harmony featureHarmony = new Harmony($"{PluginGUID}.{featureName}");
				try
				{
					patcher.PatchAll(feature.GetType().GetNestedTypes(), featureHarmony);
					applied.Add(featureHarmony);
				}
				catch (Exception e)
				{
					featureHarmony.UnpatchSelf();
					if (feature is Features.Core)
					{
						Logger.LogError($"Core patches failed to apply; {PluginName} is disabled and the server runs vanilla. {e}");
						foreach (Harmony instance in applied)
						{
							instance.UnpatchSelf();
						}
						harmony.UnpatchSelf();
						return false;
					}
					Logger.LogError($"Feature {featureName} failed to apply and is disabled. {e}");
				}
			}
			return true;
		}

		public bool ModIsEnabled()
		{
			return Configuration.modEnabled.Value;
		}

		public static bool IsDedicated()
		{
			return new ZNet().IsDedicated();
		}
	}

}
