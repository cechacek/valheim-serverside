using BepInEx.Configuration;

namespace PluginConfiguration
{
	public class Configuration
	{
		public static ConfigEntry<bool> modEnabled;

		public static ConfigEntry<bool> maxObjectsPerFrameEnabled;
		public static ConfigEntry<int> maxObjectsPerFrame;

		public static ConfigEntry<bool> networkingEnabled;
		public static ConfigEntry<int> networkQueueSizeKB;
		public static ConfigEntry<int> networkSendRateMinKB;
		public static ConfigEntry<int> networkSendRateMaxKB;
		public static ConfigEntry<int> networkStatsMinutes;

		public static void Load(ConfigFile config)
		{
			modEnabled = config.Bind<bool>("General", "Enabled", true, "Enable or disable the mod");

			maxObjectsPerFrameEnabled = config.Bind<bool>("MaxObjectsPerFrame", "Enabled", true, "Enable or disable the feature");
			maxObjectsPerFrame = config.Bind<int>("MaxObjectsPerFrame", "MaxObjects", 100, "Maximum number of objects the server can create per frame.");

			networkingEnabled = config.Bind<bool>("Networking", "Enabled", true,
				"Raise the limits on how fast the server sends world data to each player (server-side part of BetterNetworking). Needs a restart.");
			networkQueueSizeKB = config.Bind<int>("Networking", "QueueSizeKB", 32,
				new ConfigDescription("Data queued per player before the server stops sending world updates for that tick. Valheim: 10. Above 80 Steam starts failing.",
					new AcceptableValueRange<int>(10, 80)));
			networkSendRateMinKB = config.Bind<int>("Networking", "SteamSendRateMinKB", 256,
				new ConfigDescription("Minimum rate Steam attempts to send to each player, KB/s. Valheim: 150. Keep it below the server's upload speed divided by the number of players.",
					new AcceptableValueRange<int>(64, 4096)));
			networkSendRateMaxKB = config.Bind<int>("Networking", "SteamSendRateMaxKB", 1024,
				new ConfigDescription("Maximum rate Steam sends to each player, KB/s. Valheim: 150.",
					new AcceptableValueRange<int>(64, 4096)));
			networkStatsMinutes = config.Bind<int>("Networking", "StatsIntervalMinutes", 5,
				"Every this many minutes, log per player how often their send queue was full. 0 disables.");
		}
	}
}
