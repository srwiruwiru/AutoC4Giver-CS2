using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

using AutoC4Giver.Utils;
using AutoC4Giver.Config;

namespace AutoC4Giver;

[MinimumApiVersion(369)]
public class AutoC4Giver : BasePlugin, IPluginConfig<BaseConfigs>
{
	public override string ModuleName => "AutoC4Giver";
	public override string ModuleVersion => "1.0.1";
	public override string ModuleAuthor => "luca.uy";
	public override string ModuleDescription => "Automatically transfers the C4 to a nearby alive teammate if it's dropped shortly after spawn";

	public required BaseConfigs Config { get; set; }

	private Timer? _spawnTimer;
	private bool _isInSpawnPeriod = false;
	private readonly HashSet<CCSPlayerController> _playersWhoDroppedC4 = new();

	public void OnConfigParsed(BaseConfigs config)
	{
		Config = config;
		Utils.Logger.Config = config;
	}

	public override void Load(bool hotReload)
	{
		Utils.Logger.LogDebug("Core", "Plugin loaded successfully!");
		Utils.Logger.LogInfo("Config", $"Spawn Duration: {Config.SpawnDuration}s");
		Utils.Logger.LogInfo("Config", $"Transfer Delay: {Config.TransferDelay}s");
		Utils.Logger.LogInfo("Config", $"Debug Enabled: {Config.EnableDebug}");
	}

	[GameEventHandler]
	public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
	{
		Utils.Logger.LogInfo("Events [RoundStart]", "Round started, initializing spawn period timer");

		_isInSpawnPeriod = true;
		_playersWhoDroppedC4.Clear();
		_spawnTimer?.Kill();

		_spawnTimer = AddTimer(Config.SpawnDuration, () =>
		{
			_isInSpawnPeriod = false;
			Utils.Logger.LogInfo("Events [Timer]", $"Spawn period ended after {Config.SpawnDuration} seconds");
		});

		return HookResult.Continue;
	}

	[GameEventHandler]
	public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
	{
		Utils.Logger.LogInfo("Events [RoundEnd]", "Round ended, cleaning up");

		_spawnTimer?.Kill();
		_spawnTimer = null;
		_isInSpawnPeriod = false;
		_playersWhoDroppedC4.Clear();

		return HookResult.Continue;
	}

	[GameEventHandler]
	public HookResult OnItemPickup(EventItemPickup @event, GameEventInfo info)
	{
		var player = @event.Userid;
		var item = @event.Item;

		if (!PlayerUtils.IsValidPlayer(player) || item != "weapon_c4" || player?.Team != CsTeam.Terrorist)
			return HookResult.Continue;

		Utils.Logger.LogInfo("Events [ItemPickup]", $"Terrorist {player?.PlayerName} picked up C4");

		if (player != null)
		{
			_playersWhoDroppedC4.Remove(player);
		}

		return HookResult.Continue;
	}

	[GameEventHandler]
	public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
	{
		var player = @event.Userid;
		if (player != null && player.Team == CsTeam.Terrorist)
		{
			_playersWhoDroppedC4.Remove(player);
		}

		return HookResult.Continue;
	}

	[GameEventHandler]
	public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
	{
		var player = @event.Userid;
		if (player != null && player.Team == CsTeam.Terrorist)
		{
			_playersWhoDroppedC4.Remove(player);
		}

		Utils.Logger.LogInfo("Events [PlayerDeath]", $"Terrorist {player?.PlayerName} died - C4 will remain on ground if dropped");

		return HookResult.Continue;
	}

	private void CheckForDroppedC4()
	{
		if (!_isInSpawnPeriod)
			return;

		Utils.Logger.LogInfo("CheckDroppedC4", "Checking for dropped C4 on the ground");

		var c4Entities = Utilities.FindAllEntitiesByDesignerName<CC4>("weapon_c4");
		foreach (var c4 in c4Entities)
		{
			if (c4?.OwnerEntity?.Value == null && c4?.AbsOrigin != null)
			{
				Utils.Logger.LogInfo("CheckDroppedC4", "Found C4 on the ground, looking for recipient");

				var nearestTerrorist = PlayerUtils.FindNearestAliveTerrorist(c4.AbsOrigin, _playersWhoDroppedC4);
				if (nearestTerrorist != null)
				{
					Utils.Logger.LogInfo("CheckDroppedC4", $"Transferring C4 to {nearestTerrorist.PlayerName}");

					c4.Remove();

					PlayerUtils.GiveC4ToPlayer(nearestTerrorist);
					nearestTerrorist.PrintToChat($"{Localizer["prefix"]} {Localizer["autoc4giver.c4_received"]}");

					Utils.Logger.LogInfo("CheckDroppedC4", $"C4 successfully transferred to {nearestTerrorist.PlayerName}");
				}
				else
				{
					Utils.Logger.LogWarning("CheckDroppedC4", "No eligible terrorists found to transfer C4 to (excluding players who dropped it)");
				}

				break;
			}
		}
	}

	[GameEventHandler]
	public HookResult OnBombDropped(EventBombDropped @event, GameEventInfo info)
	{
		var player = @event.Userid;
		if (!PlayerUtils.IsValidPlayer(player))
			return HookResult.Continue;

		Utils.Logger.LogInfo("Events [BombDropped]", $"Player {player?.PlayerName} dropped the bomb");

		if (player != null && _isInSpawnPeriod)
		{
			_playersWhoDroppedC4.Add(player);
			Utils.Logger.LogInfo("Events [BombDropped]", $"Added {player.PlayerName} to exclusion list for C4 transfer");

			AddTimer(Config.TransferDelay, CheckForDroppedC4);
		}

		return HookResult.Continue;
	}

	public override void Unload(bool hotReload)
	{
		_spawnTimer?.Kill();
		_playersWhoDroppedC4.Clear();
		Utils.Logger.LogDebug("Core", "Plugin unloaded");
	}
}