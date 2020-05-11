﻿using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Api.Models.Internal;
using Tgstation.Server.Host.Components.Chat.Commands;
using Tgstation.Server.Host.Components.Chat.Providers;
using Tgstation.Server.Host.Core;
using Tgstation.Server.Host.IO;

namespace Tgstation.Server.Host.Components.Chat
{
	/// <inheritdoc />
	// TODO: Decomplexify
	#pragma warning disable CA1506
	sealed class ChatManager : IChatManager, IRestartHandler
	{
		const string CommonMention = "!tgs";

		/// <summary>
		/// The <see cref="IProviderFactory"/> for the <see cref="ChatManager"/>
		/// </summary>
		readonly IProviderFactory providerFactory;

		/// <summary>
		/// The <see cref="IIOManager"/> for the <see cref="ChatManager"/>
		/// </summary>
		readonly IIOManager ioManager;

		/// <summary>
		/// The <see cref="ICommandFactory"/> for the <see cref="ChatManager"/>
		/// </summary>
		readonly ICommandFactory commandFactory;

		/// <summary>
		/// The <see cref="IRestartRegistration"/> for the <see cref="ChatManager"/>
		/// </summary>
		readonly IRestartRegistration restartRegistration;

		/// <summary>
		/// The <see cref="IAsyncDelayer"/> for the <see cref="ChatManager"/>
		/// </summary>
		readonly IAsyncDelayer asyncDelayer;

		/// <summary>
		/// The <see cref="ILoggerFactory"/> for the <see cref="ChatManager"/>
		/// </summary>
		readonly ILoggerFactory loggerFactory;

		/// <summary>
		/// The <see cref="ILogger"/> for the <see cref="ChatManager"/>
		/// </summary>
		readonly ILogger<ChatManager> logger;

		/// <summary>
		/// Unchanging <see cref="ICommand"/>s in the <see cref="ChatManager"/> mapped by <see cref="ICommand.Name"/>
		/// </summary>
		readonly IDictionary<string, ICommand> builtinCommands;

		/// <summary>
		/// Map of <see cref="IProvider"/>s in use, keyed by <see cref="ChatBot.Id"/>
		/// </summary>
		readonly IDictionary<long, IProvider> providers;

		/// <summary>
		/// Map of <see cref="ChannelRepresentation.RealId"/>s to <see cref="ChannelMapping"/>s
		/// </summary>
		readonly IDictionary<ulong, ChannelMapping> mappedChannels;

		/// <summary>
		/// The active <see cref="IChatTrackingContext"/>s for the <see cref="ChatManager"/>
		/// </summary>
		readonly IList<IChatTrackingContext> trackingContexts;

		/// <summary>
		/// The <see cref="CancellationTokenSource"/> for <see cref="chatHandler"/>
		/// </summary>
		readonly CancellationTokenSource handlerCts;

		/// <summary>
		/// The active <see cref="Models.ChatBot"/> for the <see cref="ChatManager"/>
		/// </summary>
		readonly List<Models.ChatBot> activeChatBots;

		/// <summary>
		/// Used for various lock statements throughout this <see langword="class"/>.
		/// </summary>
		readonly object synchronizationLock;

		/// <summary>
		/// The <see cref="ICustomCommandHandler"/> for the <see cref="ChangeChannels(long, IEnumerable{Api.Models.ChatChannel}, CancellationToken)"/>
		/// </summary>
		ICustomCommandHandler? customCommandHandler;

		/// <summary>
		/// The <see cref="Task"/> that monitors incoming chat messages
		/// </summary>
		Task? chatHandler;

		/// <summary>
		/// The <see cref="TaskCompletionSource{TResult}"/> that completes when <see cref="ChatBot"/>s change
		/// </summary>
		TaskCompletionSource<object?> connectionsUpdated;

		/// <summary>
		/// Used for remapping <see cref="ChannelRepresentation.RealId"/>s
		/// </summary>
		ulong channelIdCounter;

		/// <summary>
		/// If <see cref="StartAsync(CancellationToken)"/> has been called
		/// </summary>
		bool started;

		/// <summary>
		/// Construct a <see cref="ChatManager"/>
		/// </summary>
		/// <param name="providerFactory">The value of <see cref="providerFactory"/></param>
		/// <param name="ioManager">The value of <see cref="ioManager"/></param>
		/// <param name="commandFactory">The value of <see cref="commandFactory"/></param>
		/// <param name="serverControl">The <see cref="IServerControl"/> to populate <see cref="restartRegistration"/> with</param>
		/// <param name="asyncDelayer">The value of <see cref="asyncDelayer"/></param>
		/// <param name="loggerFactory">The value of <see cref="loggerFactory"/></param>
		/// <param name="logger">The value of <see cref="logger"/></param>
		/// <param name="initialChatBots">The <see cref="IEnumerable{T}"/> used to populate <see cref="activeChatBots"/></param>
		public ChatManager(IProviderFactory providerFactory, IIOManager ioManager, ICommandFactory commandFactory, IServerControl serverControl, IAsyncDelayer asyncDelayer, ILoggerFactory loggerFactory, ILogger<ChatManager> logger, IEnumerable<Models.ChatBot> initialChatBots)
		{
			this.providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
			this.ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
			this.commandFactory = commandFactory ?? throw new ArgumentNullException(nameof(commandFactory));
			if (serverControl == null)
				throw new ArgumentNullException(nameof(serverControl));
			this.asyncDelayer = asyncDelayer ?? throw new ArgumentNullException(nameof(asyncDelayer));
			this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			activeChatBots = initialChatBots?.ToList() ?? throw new ArgumentNullException(nameof(initialChatBots));

			restartRegistration = serverControl.RegisterForRestart(this);

			synchronizationLock = new object();

			builtinCommands = new Dictionary<string, ICommand>();
			providers = new Dictionary<long, IProvider>();
			mappedChannels = new Dictionary<ulong, ChannelMapping>();
			trackingContexts = new List<IChatTrackingContext>();
			handlerCts = new CancellationTokenSource();
			connectionsUpdated = new TaskCompletionSource<object?>();
			channelIdCounter = 1;
		}

		/// <inheritdoc />
		public void Dispose()
		{
			restartRegistration.Dispose();
			handlerCts.Dispose();
			foreach (var I in providers)
				I.Value.Dispose();
		}

		/// <summary>
		/// Remove a <see cref="IProvider"/> from <see cref="providers"/> and <see cref="mappedChannels"/> optionally updating the <see cref="trackingContexts"/> as well
		/// </summary>
		/// <param name="connectionId">The <see cref="ChatBot.Id"/> of the <see cref="IProvider"/> to delete</param>
		/// <param name="updateTrackings">If <see cref="trackingContexts"/> should be update</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation.</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IProvider"/> being removed if it exists, <see langword="null"/> otherwise.</returns>
		async Task<IProvider?> RemoveProvider(long connectionId, bool updateTrackings, CancellationToken cancellationToken)
		{
			IProvider? provider;
			lock (providers)
				if (!providers.TryGetValue(connectionId, out provider))
					return null;

			Task trackingContextsUpdateTask;
			lock (mappedChannels)
			{
				foreach (var I in mappedChannels.Where(x => x.Value.ProviderId == connectionId).Select(x => x.Key).ToList())
					mappedChannels.Remove(I);

				var newMappedChannels = mappedChannels.Select(y => y.Value.Channel).ToList();

				if (updateTrackings)
					lock (trackingContexts)
						trackingContextsUpdateTask = Task.WhenAll(trackingContexts.Select(x => x.UpdateChannels(newMappedChannels, cancellationToken)));
				else
					trackingContextsUpdateTask = Task.CompletedTask;
			}

			await trackingContextsUpdateTask.ConfigureAwait(false);

			return provider;
		}

		/// <summary>
		/// Processes a <paramref name="message"/>
		/// </summary>
		/// <param name="provider">The <see cref="IProvider"/> who recevied <paramref name="message"/></param>
		/// <param name="message">The <see cref="Message"/> to process. If <see langword="null"/>, this indicates the provider reconnected.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		#pragma warning disable CA1502
		async Task ProcessMessage(IProvider provider, Message message, CancellationToken cancellationToken)
		#pragma warning restore CA1502
		{
			// provider reconnected, remap channels.
			if (message == null)
			{
				IEnumerable<Api.Models.ChatChannel>? channelsToMap;
				lock (activeChatBots)
					channelsToMap = activeChatBots.FirstOrDefault()?.Channels;

				if (channelsToMap?.Any() ?? false)
				{
					long providerId;
					lock (providers)
						providerId = providers.Where(x => x.Value == provider).Select(x => x.Key).First();
					await ChangeChannels(providerId, channelsToMap, cancellationToken).ConfigureAwait(false);
				}

				return;
			}

			// map the channel if it's private and we haven't seen it
			lock (providers)
			{
				var providerId = providers.Where(x => x.Value == provider).Select(x => x.Key).First();
				var enumerable = mappedChannels.Where(x => x.Value.ProviderId == providerId && x.Value.ProviderChannelId == message.User.Channel.RealId);
				if (message.User.Channel.IsPrivateChannel)
					lock (mappedChannels)
					{
						if (!provider.Connected)
							return;
						if (!enumerable.Any())
						{
							ulong newId;
							lock (synchronizationLock)
								newId = channelIdCounter++;
							logger.LogTrace(
								"Mapping private channel {0}:{1} as {2}",
								message.User.Channel.ConnectionName,
								message.User.FriendlyName,
								newId);
							mappedChannels.Add(newId, new ChannelMapping
							{
								IsWatchdogChannel = false,
								ProviderChannelId = message.User.Channel.RealId,
								ProviderId = providerId,
								Channel = message.User.Channel
							});
							message.User.Channel.RealId = newId;
						}
						else
							message.User.Channel.RealId = enumerable.First().Key;
					}
				else
				{
					// need to add tag and isAdminChannel
					var mapping = enumerable.First().Value;
					message.User.Channel.Id = mapping.Channel.Id;
					message.User.Channel.Tag = mapping.Channel.Tag;
					message.User.Channel.IsAdminChannel = mapping.Channel.IsAdminChannel;
				}
			}

			var splits = new List<string>(message.Content.Trim().Split(' '));
			var address = splits[0];
			if (address.Length > 1 && (address.Last() == ':' || address.Last() == ','))
				address = address[0..^1];

			address = address.ToUpperInvariant();

			var addressed = address == CommonMention.ToUpperInvariant() || address == provider.BotMention.ToUpperInvariant();

			// no mention
			if (!addressed && !message.User.Channel.IsPrivateChannel)
				return;

			logger.LogTrace("Chat command: {0}. User (True provider Id): {1}", message.Content, JsonConvert.SerializeObject(message.User));

			if (addressed)
				splits.RemoveAt(0);

			if (splits.Count == 0)
			{
				// just a mention
				await SendMessage("Hi!", new List<ulong> { message.User.Channel.RealId }, cancellationToken).ConfigureAwait(false);
				return;
			}

			var command = splits[0].ToUpperInvariant();
			splits.RemoveAt(0);
			var arguments = String.Join(" ", splits);

			try
			{
				ICommand GetCommand(string commandName)
				{
					if (!builtinCommands.TryGetValue(commandName, out var handler))
					{
						handler = trackingContexts
							.Where(x => x.CustomCommands != null)
							.SelectMany(x => x.CustomCommands)
							.Where(x => x.Name.ToUpperInvariant() == commandName)
							.FirstOrDefault();
					}

					return handler;
				}

				const string UnknownCommandMessage = "Unknown command! Type '?' or 'help' for available commands.";

				if (command == "HELP" || command == "?")
				{
					string helpText;
					if (splits.Count == 0)
					{
						var allCommands = builtinCommands.Select(x => x.Value).ToList();
						allCommands.AddRange(
							trackingContexts
								.Where(x => x.CustomCommands != null)
								.SelectMany(
									x => x.CustomCommands));
						helpText = String.Format(CultureInfo.InvariantCulture, "Available commands (Type '?' or 'help' and then a command name for more details): {0}", String.Join(", ", allCommands.Select(x => x.Name)));
					}
					else
					{
						var helpHandler = GetCommand(splits[0].ToUpperInvariant());
						if (helpHandler != default)
							helpText = String.Format(CultureInfo.InvariantCulture, "{0}: {1}{2}", helpHandler.Name, helpHandler.HelpText, helpHandler.AdminOnly ? " - May only be used in admin channels" : String.Empty);
						else
							helpText = UnknownCommandMessage;
					}

					await SendMessage(helpText, new List<ulong> { message.User.Channel.RealId }, cancellationToken).ConfigureAwait(false);
					return;
				}

				var commandHandler = GetCommand(command);

				if (commandHandler == default)
				{
					await SendMessage(UnknownCommandMessage, new List<ulong> { message.User.Channel.RealId }, cancellationToken).ConfigureAwait(false);
					return;
				}

				if (commandHandler.AdminOnly && !message.User.Channel.IsAdminChannel)
				{
					await SendMessage("Use this command in an admin channel!", new List<ulong> { message.User.Channel.RealId }, cancellationToken).ConfigureAwait(false);
					return;
				}

				var result = await commandHandler.Invoke(arguments, message.User, cancellationToken).ConfigureAwait(false);
				if (result != null)
					await SendMessage(result, new List<ulong> { message.User.Channel.RealId }, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception e)
			{
				// error bc custom commands should reply about why it failed
				logger.LogError("Error processing chat command: {0}", e);
				await SendMessage("Internal error processing command!", new List<ulong> { message.User.Channel.RealId }, cancellationToken).ConfigureAwait(false);
			}
		}

		/// <summary>
		/// Monitors active providers for new <see cref="Message"/>s
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		async Task MonitorMessages(CancellationToken cancellationToken)
		{
			var messageTasks = new Dictionary<IProvider, Task<Message>>();
			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					// prune disconnected providers
					foreach (var I in messageTasks.Where(x => !x.Key.Connected).ToList())
						messageTasks.Remove(I.Key);

					// add new ones
					Task updatedTask;
					lock (synchronizationLock)
						updatedTask = connectionsUpdated.Task;
					lock (providers)
						foreach (var I in providers)
							if (I.Value.Connected && !messageTasks.ContainsKey(I.Value))
								messageTasks.Add(I.Value, I.Value.NextMessage(cancellationToken));

					if (messageTasks.Count == 0)
					{
						await asyncDelayer.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
						continue;
					}

					// wait for a message
					await Task.WhenAny(updatedTask, Task.WhenAny(messageTasks.Select(x => x.Value))).ConfigureAwait(false);

					// process completed ones
					foreach (var I in messageTasks.Where(x => x.Value.IsCompleted).ToList())
					{
						var message = await I.Value.ConfigureAwait(false);
						await ProcessMessage(I.Key, message, cancellationToken).ConfigureAwait(false);
						messageTasks.Remove(I.Key);
					}
				}
			}
			catch (OperationCanceledException)
			{
				logger.LogTrace("Message processing loop cancelled!");
			}
			catch (Exception e)
			{
				logger.LogError("Message monitor crashed! Exception: {0}", e);
			}
		}

		/// <inheritdoc />
		public async Task ChangeChannels(long connectionId, IEnumerable<Api.Models.ChatChannel> newChannels, CancellationToken cancellationToken)
		{
			if (newChannels == null)
				throw new ArgumentNullException(nameof(newChannels));
			var provider = await RemoveProvider(connectionId, false, cancellationToken).ConfigureAwait(false);
			if (provider == null)
				return;
			var results = await provider.MapChannels(newChannels, cancellationToken).ConfigureAwait(false);
			lock (activeChatBots)
			{
				var botToUpdate = activeChatBots.FirstOrDefault(bot => bot.Id == connectionId);
				if (botToUpdate != null)
					botToUpdate.Channels = newChannels
						.Select(apiModel => new Models.ChatChannel
						{
							DiscordChannelId = apiModel.DiscordChannelId,
							IrcChannel = apiModel.IrcChannel,
							IsAdminChannel = apiModel.IsAdminChannel,
							IsUpdatesChannel = apiModel.IsUpdatesChannel,
							IsWatchdogChannel = apiModel.IsWatchdogChannel,
							Tag = apiModel.Tag
						})
						.ToList();
			}

			var mappings = Enumerable.Zip(newChannels, results, (x, y) => new ChannelMapping
			{
				IsWatchdogChannel = x.IsWatchdogChannel == true,
				IsUpdatesChannel = x.IsUpdatesChannel == true,
				ProviderChannelId = y.RealId,
				ProviderId = connectionId,
				Channel = y
			});

			ulong baseId;
			lock (synchronizationLock)
			{
				baseId = channelIdCounter;
				channelIdCounter += (ulong)results.Count;
			}

			Task trackingContextUpdateTask;
			lock (mappedChannels)
			{
				lock (providers)
					if (!providers.TryGetValue(connectionId, out IProvider? verify) || verify != provider) // aborted again
						return;
				foreach (var I in mappings)
				{
					var newId = baseId++;
					logger.LogTrace("Mapping channel {0}:{1} as {2}", I.Channel.ConnectionName, I.Channel.FriendlyName, newId);
					mappedChannels.Add(newId, I);
					I.Channel.RealId = newId;
				}

				lock (trackingContexts)
					trackingContextUpdateTask = Task.WhenAll(
						trackingContexts.Select(
							x => x.UpdateChannels(
								mappedChannels.Select(y => y.Value.Channel).ToList(),
								cancellationToken)));
			}

			await trackingContextUpdateTask.ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task ChangeSettings(ChatBot newSettings, CancellationToken cancellationToken)
		{
			if (newSettings == null)
				throw new ArgumentNullException(nameof(newSettings));

			async Task DisconnectProvider(IProvider p)
			{
				try
				{
					await p.Disconnect(cancellationToken).ConfigureAwait(false);
				}
				finally
				{
					p.Dispose();
				}
			}

			Task disconnectTask;
			IProvider? provider;
			lock (providers)
			{
				// raw settings changes forces a rebuild of the provider
				if (providers.TryGetValue(newSettings.Id, out provider))
				{
					providers.Remove(newSettings.Id);
					disconnectTask = DisconnectProvider(provider);
				}
				else
					disconnectTask = Task.CompletedTask;
				if (newSettings.Enabled.Value)
				{
					provider = providerFactory.CreateProvider(newSettings);
					providers.Add(newSettings.Id, provider);
				}
			}

			lock (mappedChannels)
				foreach (var I in mappedChannels.Where(x => x.Value.ProviderId == newSettings.Id).Select(x => x.Key).ToList())
					mappedChannels.Remove(I);

			await disconnectTask.ConfigureAwait(false);

			if (started)
			{
				if (newSettings.Enabled.Value)
					await provider.Connect(cancellationToken).ConfigureAwait(false);
				lock (synchronizationLock)
				{
					// same thread shennanigans
					var oldOne = connectionsUpdated;
					connectionsUpdated = new TaskCompletionSource<object>();
					oldOne.SetResult(null);
				}
			}

			Task reconnectionUpdateTask = Task.CompletedTask;
			lock (activeChatBots)
			{
				var originalChatBot = activeChatBots.FirstOrDefault(bot => bot.Id == newSettings.Id);
				if (originalChatBot != null)
				{
					if (originalChatBot.ReconnectionInterval != newSettings.ReconnectionInterval)
						reconnectionUpdateTask = provider.SetReconnectInterval(newSettings.ReconnectionInterval.Value);

					activeChatBots.Remove(originalChatBot);
				}

				activeChatBots.Add(new Models.ChatBot
				{
					Id = newSettings.Id,
					ConnectionString = newSettings.ConnectionString,
					Enabled = newSettings.Enabled,
					Name = newSettings.Name,
					ReconnectionInterval = newSettings.ReconnectionInterval,
					Provider = newSettings.Provider
				});
			}

			await reconnectionUpdateTask.ConfigureAwait(false);
		}

		/// <inheritdoc />
		public Task SendMessage(string message, IEnumerable<ulong> channelIds, CancellationToken cancellationToken)
		{
			if (message == null)
				throw new ArgumentNullException(nameof(message));
			if (channelIds == null)
				throw new ArgumentNullException(nameof(channelIds));

			logger.LogTrace("Chat send \"{0}\" to channels: {1}", message, String.Join(", ", channelIds));

			return Task.WhenAll(channelIds.Select(x =>
			{
				ChannelMapping channelMapping;
				lock (mappedChannels)
					if (!mappedChannels.TryGetValue(x, out channelMapping))
						return Task.CompletedTask;
				IProvider provider;
				lock (providers)
					if (!providers.TryGetValue(channelMapping.ProviderId, out provider))
						return Task.CompletedTask;
				return provider.SendMessage(channelMapping.ProviderChannelId, message, cancellationToken);
			}));
		}

		/// <inheritdoc />
		public Task SendWatchdogMessage(string message, CancellationToken cancellationToken)
		{
			List<ulong> wdChannels;
			message = String.Format(CultureInfo.InvariantCulture, "WD: {0}", message);
			lock (mappedChannels) // so it doesn't change while we're using it
				wdChannels = mappedChannels.Where(x => x.Value.IsWatchdogChannel).Select(x => x.Key).ToList();
			return SendMessage(message, wdChannels, cancellationToken);
		}

		/// <inheritdoc />
		public Task SendUpdateMessage(string message, CancellationToken cancellationToken)
		{
			List<ulong> wdChannels;
			message = String.Format(CultureInfo.InvariantCulture, "DM: {0}", message);
			lock (mappedChannels) // so it doesn't change while we're using it
				wdChannels = mappedChannels.Where(x => x.Value.IsUpdatesChannel).Select(x => x.Key).ToList();
			return SendMessage(message, wdChannels, cancellationToken);
		}

		/// <inheritdoc />
		public async Task StartAsync(CancellationToken cancellationToken)
		{
			foreach (var I in commandFactory.GenerateCommands())
				builtinCommands.Add(I.Name.ToUpperInvariant(), I);
			var initialChatBots = activeChatBots.ToList();
			await Task.WhenAll(initialChatBots.Select(x => ChangeSettings(x, cancellationToken))).ConfigureAwait(false);
			await Task.WhenAll(providers.Select(x => x.Value).Select(x => x.Connect(cancellationToken))).ConfigureAwait(false);
			await Task.WhenAll(initialChatBots.Select(x => ChangeChannels(x.Id, x.Channels, cancellationToken))).ConfigureAwait(false);
			chatHandler = MonitorMessages(handlerCts.Token);
			started = true;
		}

		/// <inheritdoc />
		public async Task StopAsync(CancellationToken cancellationToken)
		{
			handlerCts.Cancel();
			await chatHandler.ConfigureAwait(false);
			await Task.WhenAll(providers.Select(x => x.Value).Select(x => x.Disconnect(cancellationToken))).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public IChatTrackingContext CreateTrackingContext()
		{
			if (customCommandHandler == null)
				throw new InvalidOperationException("RegisterCommandHandler() hasn't been called!");

			IChatTrackingContext context = null;
			lock (mappedChannels)
				context = new ChatTrackingContext(
					customCommandHandler,
					mappedChannels.Select(y => y.Value.Channel),
					loggerFactory.CreateLogger<ChatTrackingContext>(),
					() =>
					{
						lock (trackingContexts)
							trackingContexts.Remove(context);
					});

			lock (trackingContexts)
				trackingContexts.Add(context);

			return context;
		}

		/// <inheritdoc />
		public void RegisterCommandHandler(ICustomCommandHandler customCommandHandler)
		{
			if (this.customCommandHandler != null)
				throw new InvalidOperationException("RegisterCommandHandler() already called!");
			this.customCommandHandler = customCommandHandler ?? throw new ArgumentNullException(nameof(customCommandHandler));
		}

		/// <inheritdoc />
		public async Task DeleteConnection(long connectionId, CancellationToken cancellationToken)
		{
			var provider = await RemoveProvider(connectionId, true, cancellationToken).ConfigureAwait(false);
			if (provider != null)
				try
				{
					await provider.Disconnect(cancellationToken).ConfigureAwait(false);
				}
				finally
				{
					provider.Dispose();
				}
		}

		/// <inheritdoc />
		public Task HandleRestart(Version updateVersion, CancellationToken cancellationToken)
		{
			var message = updateVersion == null ? "TGS: Restart requested..." : String.Format(CultureInfo.InvariantCulture, "TGS: Updating to version {0}...", updateVersion);
			List<ulong> wdChannels;
			lock (mappedChannels) // so it doesn't change while we're using it
				wdChannels = mappedChannels.Select(x => x.Key).ToList();
			return SendMessage(message, wdChannels, cancellationToken);
		}
	}
}
