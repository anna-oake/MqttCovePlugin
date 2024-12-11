using Cove.Server.Plugins;
using Cove.Server;
using Cove.Server.Actor;

using Cove.Server.Chalk;
using Cove.Server.Utils;

namespace MqttCovePlugin {
	public class MqttCovePlugin(CoveServer server) : CovePlugin(server) {
		private readonly Mutex _mutex = new();
		private Mqtt? _mqtt;

		public override void onInit() {
			base.onInit();

			string? host, user, password, prefix;
			password = null;

			try {
				var config = ConfigReader.ReadConfig("mqtt.cfg");
				if (!config.TryGetValue("host", out host)) {
					throw new Exception("'host' is required");
				}
				if (!config.TryGetValue("prefix", out prefix)) {
					throw new Exception("'prefix' is required");
				}
				if (config.TryGetValue("user", out user)) {
					if (!config.TryGetValue("password", out password)) {
						throw new Exception("if 'user' is present, 'password' is required");
					}
				}
			}
			catch (Exception e) {
				Log($"Couldn't start, config issue: {e.Message}");
				return;
			}

			try {
				_mqtt = new Mqtt(host, prefix, user, password);
				_mqtt.OnChatMessage += HandleChatMessage;
				_mqtt.OnKickPlayer += HandleKickPlayer;
				_mqtt.OnBanPlayer += HandleBanPlayer;
				_mqtt.OnRequestChalk += HandleRequestChalk;
				_mqtt.PublishServerInfo(ConvertServerInfo(ParentServer));
				_mqtt.PublishPlayers(GetPlayers());
				Log($"Connected to {host}");
			}
			catch (Exception e) {
				Log($"Couldn't connect: {e.Message}");
			}
		}

		public override void onEnd() {
			base.onEnd();
			_mutex.WaitOne();
			_mqtt?.PublishPlayers([]);
			_mqtt?.Disconnect();
			_mqtt = null;
			_mutex.ReleaseMutex();
		}

		public override void onPlayerJoin(WFPlayer player) {
			base.onPlayerJoin(player);
			if (ParentServer.isPlayerBanned(player.SteamId)) {
				return;
			}
			_mqtt?.PublishJoin(ConvertPlayer(player));
			_mqtt?.PublishPlayers(GetPlayers());
		}

		public override void onPlayerLeave(WFPlayer player) {
			base.onPlayerLeave(player);
			_mqtt?.PublishPlayers(GetPlayers().Where(p => p.SteamID != player.SteamId.m_SteamID));
			if (ParentServer.isPlayerBanned(player.SteamId)) {
				return;
			}
			_mqtt?.PublishLeave(ConvertPlayer(player));
		}

		public override void onChatMessage(WFPlayer sender, string message) {
			base.onChatMessage(sender, message);
			_mqtt?.PublishChat(ConvertPlayer(sender), message);
		}

		private void HandleChatMessage(string message) {
			SendGlobalChatMessage(message);
			Log($"Chat: {message}");
		}

		private void HandleKickPlayer(ulong id) {
			var player = GetAllPlayers().Where(x => x.SteamId.m_SteamID == id).FirstOrDefault();
			if (player == null) {
				Log($"Kick: failed, {id} not found");
				return;
			}
			KickPlayer(player);
			_mqtt?.PublishKick(ConvertPlayer(player));
			Log($"Kick: success, [{player.FisherID}] {player.Username} ({id})");
		}

		private void HandleBanPlayer(ulong id) {
			var player = GetAllPlayers().Where(x => x.SteamId.m_SteamID == id).FirstOrDefault();
			if (player == null) {
				Log($"Ban: failed, {id} not found");
				return;
			}
			BanPlayer(player);
			_mqtt?.PublishBan(ConvertPlayer(player));
			Log($"Ban: success, [{player.FisherID}] {player.Username} ({id})");
		}

		private void HandleRequestChalk() {
			List<ChalkCanvas> chalkData = [.. ParentServer.chalkCanvas];
			_mqtt?.PublishChalk(chalkData.Select(ConvertChalkCanvas));
		}

		private IEnumerable<MqttPlayer> GetPlayers() {
			return GetAllPlayers().Where(x => !ParentServer.isPlayerBanned(x.SteamId)).Select(ConvertPlayer);
		}

		private MqttPlayer ConvertPlayer(WFPlayer player) {
			return new MqttPlayer {
				SteamID = player.SteamId.m_SteamID,
				Username = player.Username,
				IsAdmin = IsPlayerAdmin(player)
			};
		}

		private MqttServerInfo ConvertServerInfo(CoveServer server) {
			return new MqttServerInfo {
				Name = server.ServerName,
				MaxPlayers = server.MaxPlayers,
				LobbyCode = server.LobbyCode,
				CodeOnly = server.codeOnly,
				FriendsOnly = server.friendsOnly,
				AgeRestricted = server.ageRestricted
			};
		}

		private MqttChalkCanvas ConvertChalkCanvas(ChalkCanvas c) {
			return new MqttChalkCanvas {
				ID = c.canvasID,
				Image = c.chalkImage.Select(x => new float[3] { x.Key.x, x.Key.y, x.Value })
			};
		}
	}
}