﻿using Byond.TopicSender;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Host.Components.Byond;
using Tgstation.Server.Host.Components.Chat;
using Tgstation.Server.Host.Components.Chat.Commands;
using Tgstation.Server.Host.Components.Deployment;
using Tgstation.Server.Host.Components.Interop.Bridge;
using Tgstation.Server.Host.Components.Repository;
using Tgstation.Server.Host.Components.Session;
using Tgstation.Server.Host.Components.Watchdog;
using Tgstation.Server.Host.Core;
using Tgstation.Server.Host.Database;
using Tgstation.Server.Host.IO;
using Tgstation.Server.Host.Jobs;
using Tgstation.Server.Host.Security;
using Tgstation.Server.Host.System;

namespace Tgstation.Server.Host.Components
{
	/// <inheritdoc />
	sealed class InstanceFactory : IInstanceFactory
	{
		/// <summary>
		/// The <see cref="IIOManager"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IIOManager ioManager;

		/// <summary>
		/// The <see cref="IDatabaseContextFactory"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IDatabaseContextFactory databaseContextFactory;

		/// <summary>
		/// The <see cref="IAssemblyInformationProvider"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IAssemblyInformationProvider assemblyInformationProvider;

		/// <summary>
		/// The <see cref="ILoggerFactory"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly ILoggerFactory loggerFactory;

		/// <summary>
		/// The <see cref="ITopicClient"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly ITopicClient byondTopicSender;

		/// <summary>
		/// The <see cref="ICryptographySuite"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly ICryptographySuite cryptographySuite;

		/// <summary>
		/// The <see cref="ISynchronousIOManager"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly ISynchronousIOManager synchronousIOManager;

		/// <summary>
		/// The <see cref="ISymlinkFactory"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly ISymlinkFactory symlinkFactory;

		/// <summary>
		/// The <see cref="IByondInstaller"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IByondInstaller byondInstaller;

		/// <summary>
		/// The <see cref="IChatManagerFactory"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IChatManagerFactory chatFactory;

		/// <summary>
		/// The <see cref="IProcessExecutor"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IProcessExecutor processExecutor;

		/// <summary>
		/// The <see cref="IPostWriteHandler"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IPostWriteHandler postWriteHandler;

		/// <summary>
		/// The <see cref="IWatchdogFactory"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IWatchdogFactory watchdogFactory;

		/// <summary>
		/// The <see cref="IJobManager"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IJobManager jobManager;

		/// <summary>
		/// The <see cref="INetworkPromptReaper"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly INetworkPromptReaper networkPromptReaper;

		/// <summary>
		/// The <see cref="IGitHubClientFactory"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IGitHubClientFactory gitHubClientFactory;

		/// <summary>
		/// The <see cref="IPlatformIdentifier"/> for the <see cref="InstanceFactory"/>
		/// </summary>
		readonly IPlatformIdentifier platformIdentifier;

		/// <summary>
		/// The <see cref="ILibGit2RepositoryFactory"/> for the <see cref="InstanceFactory"/>.
		/// </summary>
		readonly ILibGit2RepositoryFactory repositoryFactory;

		/// <summary>
		/// The <see cref="ILibGit2Commands"/> for the <see cref="InstanceFactory"/>.
		/// </summary>
		readonly ILibGit2Commands repositoryCommands;

		/// <summary>
		/// The <see cref="IServerPortProvider"/> for the <see cref="InstanceFactory"/>.
		/// </summary>
		readonly IServerPortProvider serverPortProvider;

		/// <summary>
		/// Construct an <see cref="InstanceFactory"/>
		/// </summary>
		/// <param name="ioManager">The value of <see cref="ioManager"/></param>
		/// <param name="databaseContextFactory">The value of <see cref="databaseContextFactory"/></param>
		/// <param name="assemblyInformationProvider">The value of <see cref="assemblyInformationProvider"/></param>
		/// <param name="loggerFactory">The value of <see cref="loggerFactory"/></param>
		/// <param name="byondTopicSender">The value of <see cref="byondTopicSender"/></param>
		/// <param name="cryptographySuite">The value of <see cref="cryptographySuite"/></param>
		/// <param name="synchronousIOManager">The value of <see cref="synchronousIOManager"/></param>
		/// <param name="symlinkFactory">The value of <see cref="symlinkFactory"/></param>
		/// <param name="byondInstaller">The value of <see cref="byondInstaller"/></param>
		/// <param name="chatFactory">The value of <see cref="chatFactory"/></param>
		/// <param name="processExecutor">The value of <see cref="processExecutor"/></param>
		/// <param name="postWriteHandler">The value of <see cref="postWriteHandler"/></param>
		/// <param name="watchdogFactory">The value of <see cref="watchdogFactory"/></param>
		/// <param name="jobManager">The value of <see cref="jobManager"/></param>
		/// <param name="networkPromptReaper">The value of <see cref="networkPromptReaper"/></param>
		/// <param name="gitHubClientFactory">The value of <see cref="gitHubClientFactory"/></param>
		/// <param name="platformIdentifier">The value of <see cref="platformIdentifier"/></param>
		/// <param name="repositoryFactory">The value of <see cref="repositoryFactory"/>.</param>
		/// <param name="repositoryCommands">The value of <see cref="repositoryCommands"/>.</param>
		/// <param name="serverPortProvider">The value of <see cref="serverPortProvider"/>.</param>
		public InstanceFactory(
			IIOManager ioManager,
			IDatabaseContextFactory databaseContextFactory,
			IAssemblyInformationProvider assemblyInformationProvider,
			ILoggerFactory loggerFactory,
			ITopicClient byondTopicSender,
			ICryptographySuite cryptographySuite,
			ISynchronousIOManager synchronousIOManager,
			ISymlinkFactory symlinkFactory,
			IByondInstaller byondInstaller,
			IChatManagerFactory chatFactory,
			IProcessExecutor processExecutor,
			IPostWriteHandler postWriteHandler,
			IWatchdogFactory watchdogFactory,
			IJobManager jobManager,
			INetworkPromptReaper networkPromptReaper,
			IGitHubClientFactory gitHubClientFactory,
			IPlatformIdentifier platformIdentifier,
			ILibGit2RepositoryFactory repositoryFactory,
			ILibGit2Commands repositoryCommands,
			IServerPortProvider serverPortProvider)
		{
			this.ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
			this.databaseContextFactory = databaseContextFactory ?? throw new ArgumentNullException(nameof(databaseContextFactory));
			this.assemblyInformationProvider = assemblyInformationProvider ?? throw new ArgumentNullException(nameof(assemblyInformationProvider));
			this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
			this.byondTopicSender = byondTopicSender ?? throw new ArgumentNullException(nameof(byondTopicSender));
			this.cryptographySuite = cryptographySuite ?? throw new ArgumentNullException(nameof(cryptographySuite));
			this.synchronousIOManager = synchronousIOManager ?? throw new ArgumentNullException(nameof(synchronousIOManager));
			this.symlinkFactory = symlinkFactory ?? throw new ArgumentNullException(nameof(symlinkFactory));
			this.byondInstaller = byondInstaller ?? throw new ArgumentNullException(nameof(byondInstaller));
			this.chatFactory = chatFactory ?? throw new ArgumentNullException(nameof(chatFactory));
			this.processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
			this.postWriteHandler = postWriteHandler ?? throw new ArgumentNullException(nameof(postWriteHandler));
			this.watchdogFactory = watchdogFactory ?? throw new ArgumentNullException(nameof(watchdogFactory));
			this.jobManager = jobManager ?? throw new ArgumentNullException(nameof(jobManager));
			this.networkPromptReaper = networkPromptReaper ?? throw new ArgumentNullException(nameof(networkPromptReaper));
			this.gitHubClientFactory = gitHubClientFactory ?? throw new ArgumentNullException(nameof(gitHubClientFactory));
			this.platformIdentifier = platformIdentifier ?? throw new ArgumentNullException(nameof(platformIdentifier));
			this.repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
			this.repositoryCommands = repositoryCommands ?? throw new ArgumentNullException(nameof(repositoryCommands));
			this.serverPortProvider = serverPortProvider ?? throw new ArgumentNullException(nameof(serverPortProvider));
		}

		/// <inheritdoc />
#pragma warning disable CA1506 // TODO: Decomplexify
		public IInstance CreateInstance(IBridgeRegistrar bridgeRegistrar, Models.Instance metadata)
		{
			// Create the ioManager for the instance
			var instanceIoManager = new ResolvingIOManager(ioManager, metadata.Path);

			// various other ioManagers
			var repoIoManager = new ResolvingIOManager(instanceIoManager, "Repository");
			var byondIOManager = new ResolvingIOManager(instanceIoManager, "Byond");
			var gameIoManager = new ResolvingIOManager(instanceIoManager, "Game");
			var configurationIoManager = new ResolvingIOManager(instanceIoManager, "Configuration");

			var configuration = new StaticFiles.Configuration(configurationIoManager, synchronousIOManager, symlinkFactory, processExecutor, postWriteHandler, platformIdentifier, loggerFactory.CreateLogger<StaticFiles.Configuration>());
			var eventConsumer = new EventConsumer(configuration);
			var repoManager = new RepositoryManager(
				repositoryFactory,
				repositoryCommands,
				repoIoManager,
				eventConsumer,
				loggerFactory.CreateLogger<Repository.Repository>(),
				loggerFactory.CreateLogger<RepositoryManager>());
			try
			{
				var byond = new ByondManager(byondIOManager, byondInstaller, eventConsumer, loggerFactory.CreateLogger<ByondManager>());

				var commandFactory = new CommandFactory(assemblyInformationProvider, byond, repoManager, databaseContextFactory, metadata);

				var chatManager = chatFactory.CreateChatManager(instanceIoManager, commandFactory, metadata.ChatSettings);
				try
				{
					var sessionControllerFactory = new SessionControllerFactory(
						processExecutor,
						byond,
						byondTopicSender,
						cryptographySuite,
						assemblyInformationProvider,
						gameIoManager,
						chatManager,
						networkPromptReaper,
						platformIdentifier,
						bridgeRegistrar,
						serverPortProvider,
						loggerFactory,
						loggerFactory.CreateLogger<SessionControllerFactory>(),
						metadata.CloneMetadata());

					var dmbFactory = new DmbFactory(databaseContextFactory, gameIoManager, loggerFactory.CreateLogger<DmbFactory>(), metadata.CloneMetadata());
					try
					{
						var reattachInfoHandler = new ReattachInfoHandler(databaseContextFactory, dmbFactory, loggerFactory.CreateLogger<ReattachInfoHandler>(), metadata.CloneMetadata());
						var watchdog = watchdogFactory.CreateWatchdog(
							chatManager,
							dmbFactory,
							reattachInfoHandler,
							sessionControllerFactory,
							gameIoManager,
							metadata.CloneMetadata(),
							metadata.DreamDaemonSettings);
						eventConsumer.SetWatchdog(watchdog);
						commandFactory.SetWatchdog(watchdog);
						try
						{
							Instance instance = null;
							var dreamMaker = new DreamMaker(
								byond,
								gameIoManager,
								configuration,
								sessionControllerFactory,
								eventConsumer,
								chatManager,
								processExecutor,
								gitHubClientFactory,
								dmbFactory,
								repoManager,
								loggerFactory.CreateLogger<DreamMaker>(),
								metadata.CloneMetadata());

							instance = new Instance(
								metadata.CloneMetadata(),
								repoManager,
								byond,
								dreamMaker,
								watchdog,
								chatManager,
								configuration,
								databaseContextFactory,
								dmbFactory,
								jobManager,
								eventConsumer,
								loggerFactory.CreateLogger<Instance>());

							return instance;
						}
						catch
						{
							watchdog.Dispose();
							throw;
						}
					}
					catch
					{
						dmbFactory.Dispose();
						throw;
					}
				}
				catch
				{
					chatManager.Dispose();
					throw;
				}
			}
			catch
			{
				repoManager.Dispose();
				throw;
			}
		}
#pragma warning restore CA1506

		/// <inheritdoc />
		public Task StartAsync(CancellationToken cancellationToken)
		{
			CheckSystemCompatibility();
			return byondInstaller.CleanCache(cancellationToken);
		}

		/// <inheritdoc />
		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		/// <summary>
		/// Test that the <see cref="repositoryFactory"/> is functional.
		/// </summary>
		private void CheckSystemCompatibility() => repositoryFactory.CreateInMemory().Dispose();
	}
}
