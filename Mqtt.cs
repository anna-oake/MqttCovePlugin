using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;

namespace MqttCovePlugin;

public class Mqtt {
	public event Action<string>? OnChatMessage;
	public event Action<ulong>? OnKickPlayer;
	public event Action<ulong>? OnBanPlayer;
	public event Action? OnRequestChalk;

	private readonly string _prefix;
	private readonly IMqttClient _client;
	private static readonly JsonSerializerOptions SerializeOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
	};

	public Mqtt(string host, string prefix, string? user = null, string? password = null) {
		_prefix = prefix;

		var factory = new MqttFactory();
		var builder = factory.CreateClientOptionsBuilder()
			.WithTcpServer(host)
			.WithWillTopic(_prefix + "/status")
			.WithWillPayload("offline")
			.WithWillRetain()
			.WithKeepAlivePeriod(TimeSpan.FromSeconds(30));
		if (user != null && password != null) {
			builder = builder.WithCredentials(user, password);
		}
		_client = factory.CreateMqttClient();

		_client.ApplicationMessageReceivedAsync += e => {
			if (e.ApplicationMessage.Retain) {
				return Task.CompletedTask;
			}

			try {
				HandleMessage(e.ApplicationMessage);
			}
			catch {
				// ну а хуле ёпта
			}

			return Task.CompletedTask;
		};

		_client.ConnectAsync(builder.Build(), CancellationToken.None).Wait();

		var options = factory.CreateSubscribeOptionsBuilder()
			.WithTopicFilter(_prefix + "/command/+")
			.Build();
		_client.SubscribeAsync(options, CancellationToken.None).Wait();

		var msg = new MqttApplicationMessageBuilder()
			.WithTopic(_prefix + "/status")
			.WithPayload("online")
			.WithRetainFlag()
			.Build();
		_client.PublishAsync(msg, CancellationToken.None).Wait();
	}

	private void HandleMessage(MqttApplicationMessage m) {
		var command = m.Topic.Split('/').Last();
		var data = m.PayloadSegment.Count > 0 ? Encoding.UTF8.GetString(
						m.PayloadSegment.Array,
						m.PayloadSegment.Offset,
						m.PayloadSegment.Count) : null;

		switch (command) {
			case "chat":
				if (string.IsNullOrWhiteSpace(data)) {
					return;
				}
				OnChatMessage?.Invoke(data.Trim());
				break;
			case "kick":
				if (string.IsNullOrWhiteSpace(data)) {
					return;
				}
				try {
					var target = ulong.Parse(data.Trim());
					OnKickPlayer?.Invoke(target);
				}
				catch { }
				break;
			case "ban":
				if (string.IsNullOrWhiteSpace(data)) {
					return;
				}
				try {
					var target = ulong.Parse(data.Trim());
					OnBanPlayer?.Invoke(target);
				}
				catch { }
				break;
			case "chalk":
				OnRequestChalk?.Invoke();
				break;
		}
	}

	public void Disconnect() {
		var msg = new MqttApplicationMessageBuilder()
				.WithTopic(_prefix + "/status")
				.WithPayload("offline")
				.WithRetainFlag()
				.Build();
		_client.PublishAsync(msg, CancellationToken.None).Wait();
		_client.DisconnectAsync().Wait();
	}

	public void PublishChat(MqttPlayer sender, string message) {
		publishEvent(new ChatEvent(sender, message));
	}

	public void PublishJoin(MqttPlayer player) {
		publishEvent(new PlayerJoinEvent(player));
	}

	public void PublishLeave(MqttPlayer player) {
		publishEvent(new PlayerLeaveEvent(player));
	}

	public void PublishKick(MqttPlayer player) {
		publishEvent(new KickEvent(player));
	}

	public void PublishBan(MqttPlayer player) {
		publishEvent(new BanEvent(player));
	}

	public void PublishChalk(IEnumerable<MqttChalkCanvas> canvases) {
		var msg = new MqttApplicationMessageBuilder()
			.WithTopic(_prefix + "/chalk")
			.WithPayload(JsonSerializer.Serialize(canvases, SerializeOptions))
			.Build();
		_client.PublishAsync(msg, CancellationToken.None).Wait();
	}

	public void PublishPlayers(IEnumerable<MqttPlayer> players) {
		var msg = new MqttApplicationMessageBuilder()
			.WithTopic(_prefix + "/players")
			.WithPayload(JsonSerializer.Serialize(players, SerializeOptions))
			.WithRetainFlag()
			.Build();
		_client.PublishAsync(msg, CancellationToken.None).Wait();
	}

	public void PublishServerInfo(MqttServerInfo info) {
		var msg = new MqttApplicationMessageBuilder()
			.WithTopic(_prefix + "/info")
			.WithPayload(JsonSerializer.Serialize(info, SerializeOptions))
			.WithRetainFlag()
			.Build();
		_client.PublishAsync(msg, CancellationToken.None).Wait();
	}

	private void publishEvent(MqttEvent e) {
		var msg = new MqttApplicationMessageBuilder()
			.WithTopic(_prefix + "/event")
			.WithPayload(JsonSerializer.Serialize(e, SerializeOptions))
			.Build();
		_client.PublishAsync(msg, CancellationToken.None).Wait();
	}
}

public class KickEvent(MqttPlayer player)
	: MqttEvent(
		"kick",
		player) { }

public class BanEvent(MqttPlayer player)
	: MqttEvent(
		"ban",
		player) { }

public class PlayerJoinEvent(MqttPlayer player)
	: MqttEvent(
		"join",
		player) { }

public class PlayerLeaveEvent(MqttPlayer player)
	: MqttEvent(
		"leave",
		player) { }

public class ChatEvent(MqttPlayer player, string message)
	: MqttEvent(
		"chat",
		new {
			Player = player,
			Message = message
		}) { }

public abstract class MqttEvent(string type, object data) {
	public string Type { get; } = type;

	public object Data { get; } = data;
}

public class MqttPlayer {
	public required ulong SteamID { get; set; }
	public required string Username { get; set; }
	public bool IsAdmin { get; set; }
}

public class MqttServerInfo {
	public required string Name { get; set; }
	public required int MaxPlayers { get; set; }
	public required string LobbyCode { get; set; }
	public required bool CodeOnly { get; set; }
	public required bool FriendsOnly { get; set; }
	public required bool AgeRestricted { get; set; }
}

public class MqttChalkCanvas {
	public required long ID { get; set; }
	public required IEnumerable<float[]> Image { get; set; }
}