using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace Valheim_Serverside
{
	/*
		Commands read from the server's standard input: `save` and `stop`.

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
			ServersidePlugin.logger.LogInfo("Console commands enabled on standard input: save, stop");
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
			switch (command.ToLowerInvariant())
			{
				case "save":
					if (!ZNet.instance || !ZNet.instance.IsServer())
					{
						ServersidePlugin.logger.LogInfo("Console: no world loaded, nothing to save");
					}
					else if (!ZNet.instance.EnoughDiskSpaceAvailable(out bool _))
					{
						ServersidePlugin.logger.LogWarning("Console: not enough disk space, world not saved");
					}
					else
					{
						// The same call as the server's own autosave (Game.UpdateSaving). The vanilla `save`
						// command goes through RPC_Save, which throws on a dedicated server when not sent
						// by a player.
						ServersidePlugin.logger.LogInfo("Console: saving world");
						ZNet.instance.Save(sync: false, saveOtherPlayerProfiles: true, waitForNextFrame: true);
					}
					break;
				case "stop":
				case "quit":
				case "shutdown":
					// Quitting runs Game.OnApplicationQuit, which saves the world before shutting down.
					ServersidePlugin.logger.LogInfo("Console: saving world and shutting down");
					Application.Quit();
					break;
				default:
					ServersidePlugin.logger.LogInfo($"Console: unknown command '{command}'. Commands: save, stop");
					break;
			}
		}
	}
}
