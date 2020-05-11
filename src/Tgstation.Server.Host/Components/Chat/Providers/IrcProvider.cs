﻿using Meebey.SmartIrc4net;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Api.Models;
using Tgstation.Server.Host.Core;
using Tgstation.Server.Host.Extensions;
using Tgstation.Server.Host.System;

namespace Tgstation.Server.Host.Components.Chat.Providers
{
	/// <summary>
	/// <see cref="IProvider"/> for internet relay chat
	/// </summary>
	sealed class IrcProvider : Provider
	{
		const int TimeoutSeconds = 5;

		/// <inheritdoc />
		public override bool Connected => client.IsConnected;

		/// <inheritdoc />
		public override string BotMention => client.Nickname;

		/// <summary>
		/// The <see cref="IAsyncDelayer"/> for the <see cref="IrcProvider"/>
		/// </summary>
		readonly IAsyncDelayer asyncDelayer;

		/// <summary>
		/// The <see cref="IrcFeatures"/> client
		/// </summary>
		readonly IrcFeatures client;

		/// <summary>
		/// Address of the server to connect to
		/// </summary>
		readonly string address;

		/// <summary>
		/// Port of the server to connect to
		/// </summary>
		readonly ushort port;

		/// <summary>
		/// IRC nickname
		/// </summary>
		readonly string nickname;

		/// <summary>
		/// Password which will used for authentication
		/// </summary>
		readonly string? password;

		/// <summary>
		/// The <see cref="IrcPasswordType"/> of <see cref="password"/>
		/// </summary>
		readonly IrcPasswordType? passwordType;

		/// <summary>
		/// Map of <see cref="ChannelRepresentation.RealId"/>s to channel names
		/// </summary>
		readonly Dictionary<ulong, string?> channelIdMap;

		/// <summary>
		/// Map of <see cref="ChannelRepresentation.RealId"/>s to query users
		/// </summary>
		readonly Dictionary<ulong, string?> queryChannelIdMap;

		/// <summary>
		/// The <see cref="Task"/> used for <see cref="IrcConnection.Listen(bool)"/>
		/// </summary>
		Task? listenTask;

		/// <summary>
		/// Id counter for <see cref="channelIdMap"/>
		/// </summary>
		ulong channelIdCounter;

		/// <summary>
		/// If we are disconnecting
		/// </summary>
		bool disconnecting;

		/// <summary>
		/// Construct an <see cref="IrcProvider"/>
		/// </summary>
		/// <param name="assemblyInformationProvider">The <see cref="IAssemblyInformationProvider"/> to get the <see cref="IAssemblyInformationProvider.VersionString"/> from</param>
		/// <param name="asyncDelayer">The value of <see cref="asyncDelayer"/></param>
		/// <param name="logger">The value of logger</param>
		/// <param name="address">The value of <see cref="address"/></param>
		/// <param name="port">The value of <see cref="port"/></param>
		/// <param name="nickname">The value of <see cref="nickname"/></param>
		/// <param name="password">The value of <see cref="password"/></param>
		/// <param name="passwordType">The value of <see cref="passwordType"/></param>
		/// <param name="reconnectInterval">The initial reconnect interval in minutes.</param>
		/// <param name="useSsl">If <see cref="IrcConnection.UseSsl"/> should be used</param>
		public IrcProvider(
			IAssemblyInformationProvider assemblyInformationProvider,
			IAsyncDelayer asyncDelayer,
			ILogger<IrcProvider> logger,
			string address,
			ushort port,
			string nickname,
			string password,
			IrcPasswordType? passwordType,
			uint reconnectInterval,
			bool useSsl)
			: base(logger, reconnectInterval)
		{
			if (assemblyInformationProvider == null)
				throw new ArgumentNullException(nameof(assemblyInformationProvider));
			this.asyncDelayer = asyncDelayer ?? throw new ArgumentNullException(nameof(asyncDelayer));

			this.address = address ?? throw new ArgumentNullException(nameof(address));
			this.port = port;
			this.nickname = nickname ?? throw new ArgumentNullException(nameof(nickname));

			if (passwordType.HasValue && password == null)
				throw new ArgumentNullException(nameof(password));

			if (password != null && !passwordType.HasValue)
				throw new ArgumentNullException(nameof(passwordType));

			this.password = password;
			this.passwordType = passwordType;

			client = new IrcFeatures
			{
				SupportNonRfc = true,
				CtcpUserInfo = "You are going to play. And I am going to watch. And everything will be just fine...",
				AutoRejoin = true,
				AutoRejoinOnKick = true,
				AutoRelogin = true,
				AutoRetry = true,
				AutoRetryLimit = TimeoutSeconds,
				AutoRetryDelay = TimeoutSeconds,
				ActiveChannelSyncing = true,
				AutoNickHandling = true,
				CtcpVersion = assemblyInformationProvider.VersionString,
				UseSsl = useSsl
			};
			if (useSsl)
				client.ValidateServerCertificate = true; // dunno if it defaults to that or what

			client.OnChannelMessage += Client_OnChannelMessage;
			client.OnQueryMessage += Client_OnQueryMessage;

			channelIdMap = new Dictionary<ulong, string?>();
			queryChannelIdMap = new Dictionary<ulong, string?>();
			channelIdCounter = 1;
			disconnecting = false;
		}

		/// <inheritdoc />
		public override void Dispose()
		{
			if (Connected)
			{
				disconnecting = true;
				client.Disconnect(); // just closes the socket
			}

			base.Dispose();
		}

		/// <summary>
		/// Handle an IRC message
		/// </summary>
		/// <param name="e">The <see cref="IrcEventArgs"/></param>
		/// <param name="isPrivate">If this is a query message</param>
		void HandleMessage(IrcEventArgs e, bool isPrivate)
		{
			if (e.Data.Nick.ToUpperInvariant() == client.Nickname.ToUpperInvariant())
				return;

			var username = e.Data.Nick;
			var channelName = isPrivate ? username : e.Data.Channel;

			ulong MapAndGetChannelId(Dictionary<ulong, string?> dicToCheck)
			{
				ulong? resultId = null;
				if (!dicToCheck.Any(x =>
				{
					if (x.Value != channelName)
						return false;
					resultId = x.Key;
					return true;
				}))
				{
					resultId = channelIdCounter++;
					dicToCheck.Add(resultId.Value, channelName);
					if (dicToCheck == queryChannelIdMap)
						channelIdMap.Add(resultId.Value, null);
				}

				return resultId!.Value;
			}

			ulong userId, channelId;
			lock (client)
			{
				userId = MapAndGetChannelId(queryChannelIdMap);
				channelId = isPrivate ? userId : MapAndGetChannelId(channelIdMap);
			}

			var message = new Message
			{
				Content = e.Data.Message,
				User = new ChatUser
				{
					Channel = new ChannelRepresentation
					{
						ConnectionName = address,
						FriendlyName = isPrivate ? String.Format(CultureInfo.InvariantCulture, "PM: {0}", channelName) : channelName,
						RealId = channelId,
						IsPrivateChannel = isPrivate

						// isAdmin and Tag populated by manager
					},
					FriendlyName = username,
					RealId = userId,
					Mention = username
				}
			};

			EnqueueMessage(message);
		}

		/// <summary>
		/// When a query message is received in IRC
		/// </summary>
		/// <param name="sender">The sender of the event</param>
		/// <param name="e">The <see cref="IrcEventArgs"/></param>
		void Client_OnQueryMessage(object sender, IrcEventArgs e) => HandleMessage(e, true);

		/// <summary>
		/// When a channel message is received in IRC
		/// </summary>
		/// <param name="sender">The sender of the event</param>
		/// <param name="e">The <see cref="IrcEventArgs"/></param>
		void Client_OnChannelMessage(object sender, IrcEventArgs e) => HandleMessage(e, false);

		/// <inheritdoc />
		public override Task<bool> Connect(CancellationToken cancellationToken) => Task.Factory.StartNew(() =>
		{
			disconnecting = false;
			lock (client)
				try
				{
					client.Connect(address, port);

					cancellationToken.ThrowIfCancellationRequested();

					if (passwordType == IrcPasswordType.Server)
						client.Login(nickname, nickname, 0, nickname, password);
					else
					{
						if (passwordType == IrcPasswordType.Sasl)
						{
							client.WriteLine("CAP REQ :sasl", Priority.Critical); // needs to be put in the buffer before anything else
							cancellationToken.ThrowIfCancellationRequested();
						}

						client.Login(nickname, nickname, 0, nickname);
					}

					if (passwordType == IrcPasswordType.NickServ)
					{
						cancellationToken.ThrowIfCancellationRequested();
						client.SendMessage(SendType.Message, "NickServ", String.Format(CultureInfo.InvariantCulture, "IDENTIFY {0}", password));
					}
					else if (passwordType == IrcPasswordType.Sasl)
					{
						// wait for the sasl ack or timeout
						var recievedAck = false;
						var recievedPlus = false;
						client.OnReadLine += (sender, e) =>
						{
							if (e.Line.Contains("ACK :sasl", StringComparison.Ordinal))
								recievedAck = true;
							else if (e.Line.Contains("AUTHENTICATE +", StringComparison.Ordinal))
								recievedPlus = true;
						};

						var startTime = DateTimeOffset.Now;
						var endTime = DateTimeOffset.Now.AddSeconds(TimeoutSeconds);
						cancellationToken.ThrowIfCancellationRequested();

						var listenTimeSpan = TimeSpan.FromMilliseconds(10);
						for (; !recievedAck && DateTimeOffset.Now <= endTime; asyncDelayer.Delay(listenTimeSpan, cancellationToken).GetAwaiter().GetResult())
							client.Listen(false);

						client.WriteLine("AUTHENTICATE PLAIN", Priority.Critical);
						cancellationToken.ThrowIfCancellationRequested();

						for (; !recievedPlus && DateTimeOffset.Now <= endTime; asyncDelayer.Delay(listenTimeSpan, cancellationToken).GetAwaiter().GetResult())
							client.Listen(false);

						// Stolen! https://github.com/znc/znc/blob/1e697580155d5a38f8b5a377f3b1d94aaa979539/modules/sasl.cpp#L196
						var authString = String.Format(CultureInfo.InvariantCulture, "{0}{1}{0}{1}{2}", nickname, '\0', password);
						var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString));
						var authLine = String.Format(CultureInfo.InvariantCulture, "AUTHENTICATE {0}", b64);
						var chars = authLine.ToCharArray();
						client.WriteLine(authLine, Priority.Critical);

						cancellationToken.ThrowIfCancellationRequested();
						client.WriteLine("CAP END", Priority.Critical);
					}

					client.Listen(false);

					listenTask = Task.Factory.StartNew(() =>
					{
						while (!disconnecting && client.IsConnected && client.Nickname != nickname)
						{
							client.ListenOnce(true);
							if (disconnecting || !client.IsConnected)
								break;
							client.Listen(false);

							// ensure we have the correct nick
							if (client.GetIrcUser(nickname) == null)
								client.RfcNick(nickname);
						}

						client.Listen();
					}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception e)
				{
					Logger.LogWarning("Unable to connect to IRC: {0}", e);
					return false;
				}

			return true;
		}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current);

		/// <inheritdoc />
		public override async Task Disconnect(CancellationToken cancellationToken)
		{
			if (!Connected)
				return;
			try
			{
				await Task.Factory.StartNew(() =>
				{
					try
					{
						client.RfcQuit("Mr. Stark, I don't feel so good...", Priority.Critical); // priocritical otherwise it wont go through
					}
					catch (Exception e)
					{
						Logger.LogWarning("Error quitting IRC: {0}", e);
					}
				}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current).ConfigureAwait(false);
				Dispose();
				await listenTask!.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception e)
			{
				Logger.LogWarning("Error disconnecting from IRC! Exception: {0}", e);
			}
		}

		/// <inheritdoc />
		public override Task<IReadOnlyCollection<ChannelRepresentation>> MapChannels(
			IEnumerable<ChatChannel> channels,
			CancellationToken cancellationToken)
			=> Task.Factory.StartNew(() =>
			{
				if (channels.Any(x => x.IrcChannel == null))
					throw new InvalidOperationException("ChatChannel missing IrcChannel!");
				lock (client)
				{
					var channelsWithKeys = new Dictionary<string, string>();
					var hs = new HashSet<string>(); // for unique inserts
					foreach (var channel in channels)
					{
						var name = channel.GetIrcChannelName();
						var key = channel.GetIrcChannelKey();
						if (hs.Add(name) && key != null)
							channelsWithKeys.Add(name, key);
					}

					var toPart = new List<string>();
					foreach (var activeChannel in client.JoinedChannels)
						if (!hs.Remove(activeChannel))
							toPart.Add(activeChannel);

					foreach (var channelToLeave in toPart)
						client.RfcPart(channelToLeave, "Pretty nice abscond!");
					foreach (var channelToJoin in hs)
						if (channelsWithKeys.TryGetValue(channelToJoin, out var key))
							client.RfcJoin(channelToJoin, key);
						else
							client.RfcJoin(channelToJoin);

					return (IReadOnlyCollection<ChannelRepresentation>)channels
						.Select(x =>
						{
							var channelName = x.GetIrcChannelName();
							ulong? id = null;
							if (!channelIdMap.Any(y =>
							{
								if (y.Value != channelName)
									return false;
								id = y.Key;
								return true;
							}))
							{
								id = channelIdCounter++;
								channelIdMap.Add(id.Value, channelName);
							}

							return new ChannelRepresentation
							{
								RealId = id.Value,
								IsAdminChannel = x.IsAdminChannel == true,
								ConnectionName = address,
								FriendlyName = channelIdMap[id.Value],
								IsPrivateChannel = false,
								Tag = x.Tag
							};
						})
						.ToList();
				}
			}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current);

		/// <inheritdoc />
		public override Task SendMessage(ulong channelId, string message, CancellationToken cancellationToken) => Task.Factory.StartNew(() =>
		{
			var channelName = channelIdMap[channelId];
			SendType sendType;
			if (channelName == null)
			{
				channelName = queryChannelIdMap[channelId];
				sendType = SendType.Notice;
			}
			else
				sendType = SendType.Message;
			try
			{
				client.SendMessage(sendType, channelName, message);
			}
			catch (Exception e)
			{
				Logger.LogWarning("Unable to send to channel: {0}", e);
			}
		}, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current);
	}
}
