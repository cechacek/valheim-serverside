using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace Valheim_Serverside
{
	/*
		Commands read from the server's standard input: `save`, `stop`, `players`,
		`give <item> <amount> <player>`.

		A vanilla dedicated server does not read its standard input, so a panel such as AMP can
		only stop it by closing or killing the process. On Windows that skips the world save on
		shutdown, and everything since the last autosave is lost. With this, the panel can send
		`stop` instead (in AMP: App.ExitMethod=String, App.ExitString=stop), and `save` can be
		typed into its console or scheduled.

		Standard input is read on a background thread; commands run on the main thread from
		ServersidePlugin.Update.
	*/
	public static class ServerConsole
	{
		private static readonly ConcurrentQueue<string> s_commands = new ConcurrentQueue<string>();

		public static void Start()
		{
			Thread reader = new Thread(ReadLoop) { IsBackground = true, Name = "Dedicated Simulation console" };
			reader.Start();
			ServersidePlugin.logger.LogInfo("Console commands enabled on standard input: save, stop, players, give <item> <amount> <player>");
		}

		private static void ReadLoop()
		{
			try
			{
				string line;
				while ((line = System.Console.In.ReadLine()) != null)
				{
					line = line.Trim();
					if (line.Length > 0)
					{
						s_commands.Enqueue(line);
					}
				}
			}
			catch (Exception e)
			{
				s_commands.Enqueue("\0" + e.Message);
			}
		}

		/*
			Replies go to the log and to standard output: with BepInEx's console off (needed on
			Windows for standard input to reach the game) the log is not on standard output, and
			standard output is what a panel such as AMP shows.
		*/
		private static void Reply(string text)
		{
			ServersidePlugin.logger.LogInfo("Console: " + text);
			try
			{
				System.Console.Out.WriteLine("Console: " + text);
				System.Console.Out.Flush();
			}
			catch (Exception)
			{
				// No standard output; the log has it.
			}
		}

		public static void ProcessPending()
		{
			while (s_commands.TryDequeue(out string command))
			{
				Execute(command);
			}
		}

		private static void Execute(string command)
		{
			if (command[0] == '\0')
			{
				ServersidePlugin.logger.LogWarning($"Console commands unavailable, cannot read standard input: {command.Substring(1)}");
				return;
			}
			string[] words = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			bool worldLoaded = ZNet.instance && ZNet.instance.IsServer();
			try
			{
				switch (words[0].ToLowerInvariant())
				{
					case "save":
						Reply(AdminCommands.Save("console"));
						break;
					case "stop":
					case "quit":
					case "shutdown":
						// Quitting runs Game.OnApplicationQuit, which saves the world before shutting down.
						Reply("saving world and shutting down");
						Application.Quit();
						break;
					case "players":
						Reply(worldLoaded ? AdminCommands.Players() : "no world loaded");
						break;
					case "give":
						// give <item> <amount> <player name, may contain spaces>
						if (!worldLoaded)
						{
							Reply("no world loaded");
						}
						else if (words.Length < 4 || !AdminCommands.TryParseAmount(words[2], out int amount))
						{
							Reply("usage: give <item> <amount> <player>, e.g. give Copper 120 Ulf");
						}
						else
						{
							string name = string.Join(" ", words, 3, words.Length - 3);
							ZNetPeer target = AdminCommands.FindPlayer(name, out string problem);
							Reply(target == null ? problem : AdminCommands.Give(target, words[1], amount, "console"));
						}
						break;
					case "help":
						Reply("commands: save | stop | players | give <item> <amount> <player>");
						break;
					default:
						Reply($"unknown command '{command}'. Commands: save, stop, players, give <item> <amount> <player>");
						break;
				}
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning($"Console: '{command}' failed: {e}");
				Reply($"'{command}' failed: {e.Message}");
			}
		}
	}
}
