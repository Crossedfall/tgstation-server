﻿using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tgstation.Server.Api;
using Tgstation.Server.Api.Models;

namespace Tgstation.Server.Client
{
	/// <inheritdoc />
	sealed class ApiClient : IApiClient
	{
		/// <inheritdoc />
		public Uri Url { get; }

		/// <inheritdoc />
		public ApiHeaders Headers
		{
			get => headers;
			set => headers = value ?? throw new InvalidOperationException("Cannot set null headers!");
		}

		/// <inheritdoc />
		public TimeSpan Timeout
		{
			get => httpClient.Timeout;
			set => httpClient.Timeout = value;
		}

		/// <summary>
		/// The <see cref="HttpClient"/> for the <see cref="ApiClient"/>
		/// </summary>
		readonly IHttpClient httpClient;

		/// <summary>
		/// The <see cref="IRequestLogger"/>s used by the <see cref="ApiClient"/>
		/// </summary>
		readonly List<IRequestLogger> requestLoggers;

		/// <summary>
		/// Backing field for <see cref="Headers"/>
		/// </summary>
		readonly ApiHeaders? tokenRefreshHeaders;

		/// <summary>
		/// The <see cref="SemaphoreSlim"/> for <see cref="Token"/> refreshes.
		/// </summary>
		readonly SemaphoreSlim semaphoreSlim;

		/// <summary>
		/// Backing field for <see cref="Headers"/>
		/// </summary>
		ApiHeaders headers;

		static JsonSerializerSettings GetSerializerSettings() => new JsonSerializerSettings
		{
			ContractResolver = new CamelCasePropertyNamesContractResolver(),
			Converters = new[] { new VersionConverter() }
		};

		static void HandleBadResponse(HttpResponseMessage response, string json)
		{
			ErrorMessage? errorMessage = null;
			try
			{
				// check if json serializes to an error message
				errorMessage = JsonConvert.DeserializeObject<ErrorMessage>(json, GetSerializerSettings());
			}
			catch (JsonException) { }

#pragma warning disable IDE0010 // Add missing cases
			switch (response.StatusCode)
#pragma warning restore IDE0010 // Add missing cases
			{
				case HttpStatusCode.UpgradeRequired:
					throw new VersionMismatchException(errorMessage, response);
				case HttpStatusCode.Unauthorized:
					throw new UnauthorizedException(errorMessage, response);
				case HttpStatusCode.InternalServerError:
					throw new ServerErrorException(errorMessage, response);
				case HttpStatusCode.NotImplemented:
				// unprocessable entity
				case (HttpStatusCode)422:
					throw new MethodNotSupportedException(errorMessage, response);
				case HttpStatusCode.NotFound:
				case HttpStatusCode.Gone:
				case HttpStatusCode.Conflict:
					throw new ConflictException(errorMessage, response);
				case HttpStatusCode.Forbidden:
					throw new InsufficientPermissionsException(response);
				case HttpStatusCode.ServiceUnavailable:
					throw new ServiceUnavailableException(response);
				case HttpStatusCode.RequestTimeout:
					throw new RequestTimeoutException(response);
				case (HttpStatusCode)429:
					throw new RateLimitException(errorMessage, response);
				default:
					throw new ApiConflictException(errorMessage, response);
			}
		}

		/// <summary>
		/// Construct an <see cref="ApiClient"/>
		/// </summary>
		/// <param name="httpClient">The value of <see cref="httpClient"/></param>
		/// <param name="url">The value of <see cref="Url"/></param>
		/// <param name="apiHeaders">The value of <see cref="Headers"/></param>
		/// <param name="tokenRefreshHeaders">The value of <see cref="tokenRefreshHeaders"/></param>
		public ApiClient(IHttpClient httpClient, Uri url, ApiHeaders apiHeaders, ApiHeaders? tokenRefreshHeaders)
		{
			this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
			Url = url ?? throw new ArgumentNullException(nameof(url));
			headers = apiHeaders ?? throw new ArgumentNullException(nameof(apiHeaders));
			this.tokenRefreshHeaders = tokenRefreshHeaders;

			requestLoggers = new List<IRequestLogger>();
			semaphoreSlim = new SemaphoreSlim(1);
		}

		/// <inheritdoc />
		public void Dispose()
		{
			httpClient.Dispose();
			semaphoreSlim.Dispose();
		}

		/// <summary>
		/// Main request method
		/// </summary>
		/// <typeparam name="TResult">The resulting POCO type</typeparam>
		/// <param name="route">The route to run</param>
		/// <param name="body">The body of the request</param>
		/// <param name="method">The method of the request</param>
		/// <param name="instanceId">The optional <see cref="Instance.Id"/> for the request</param>
		/// <param name="tokenRefresh">If this is a token refresh operation.</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the response on success</returns>
		async Task<TResult> RunRequest<TResult>(string route, object? body, HttpMethod method, long? instanceId, bool tokenRefresh, CancellationToken cancellationToken)
		{
			if (route == null)
				throw new ArgumentNullException(nameof(route));
			if (method == null)
				throw new ArgumentNullException(nameof(method));
			if (body == null && (method == HttpMethod.Post || method == HttpMethod.Put))
				throw new InvalidOperationException("Body cannot be null for POST or PUT!");

			HttpResponseMessage response;
			var fullUri = new Uri(Url, route);
			var serializerSettings = GetSerializerSettings();
			using (var request = new HttpRequestMessage(method, fullUri))
			{
				if (body != null)
					request.Content = new StringContent(JsonConvert.SerializeObject(body, serializerSettings), Encoding.UTF8, ApiHeaders.ApplicationJson);

				var headersToUse = tokenRefresh ? tokenRefreshHeaders! : headers;
				headersToUse.SetRequestHeaders(request.Headers, instanceId);

				await Task.WhenAll(requestLoggers.Select(x => x.LogRequest(request, cancellationToken))).ConfigureAwait(false);

				response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
			}

			using (response)
			{
				await Task.WhenAll(requestLoggers.Select(x => x.LogResponse(response, cancellationToken))).ConfigureAwait(false);

				var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

				if (!response.IsSuccessStatusCode)
				{
					if (!tokenRefresh
						&& response.StatusCode == HttpStatusCode.Unauthorized
						&& await RefreshToken(cancellationToken).ConfigureAwait(false))
						return await RunRequest<TResult>(route, body, method, instanceId, false, cancellationToken).ConfigureAwait(false);
					HandleBadResponse(response, json);
				}

				if (String.IsNullOrWhiteSpace(json))
					json = JsonConvert.SerializeObject(new object());

				try
				{
					return JsonConvert.DeserializeObject<TResult>(json, serializerSettings) !;
				}
				catch (JsonException)
				{
					throw new UnrecognizedResponseException(response);
				}
			}
		}

		async Task<bool> RefreshToken(CancellationToken cancellationToken)
		{
			if (tokenRefreshHeaders == null)
				return false;

			var startingToken = headers.Token;
			await semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				if (startingToken != headers.Token)
					return true;

				var token = await RunRequest<Token>(Routes.Root, new object(), HttpMethod.Post, null, true, cancellationToken);
				headers = new ApiHeaders(headers.UserAgent!, token.Bearer!);
			}
			catch (ClientException)
			{
				return false;
			}
			finally
			{
				semaphoreSlim.Release();
			}

			return true;
		}

		/// <inheritdoc />
		public Task<TResult> Create<TResult>(string route, CancellationToken cancellationToken) => RunRequest<TResult>(route, new object(), HttpMethod.Put, null, false, cancellationToken);

		/// <inheritdoc />
		public Task<TResult> Read<TResult>(string route, CancellationToken cancellationToken) => RunRequest<TResult>(route, null, HttpMethod.Get, null, false, cancellationToken);

		/// <inheritdoc />
		public Task<TResult> Update<TResult>(string route, CancellationToken cancellationToken) => RunRequest<TResult>(route, new object(), HttpMethod.Post, null, false, cancellationToken);

		/// <inheritdoc />
		public Task<TResult> Update<TBody, TResult>(string route, TBody body, CancellationToken cancellationToken) => RunRequest<TResult>(route, body, HttpMethod.Post, null, false, cancellationToken);

		/// <inheritdoc />
		public Task Update<TBody>(string route, TBody body, CancellationToken cancellationToken) => RunRequest<object>(route, body, HttpMethod.Post, null, false, cancellationToken);

		/// <inheritdoc />
		public Task<TResult> Create<TBody, TResult>(string route, TBody body, CancellationToken cancellationToken) => RunRequest<TResult>(route, body, HttpMethod.Put, null, false, cancellationToken);

		/// <inheritdoc />
		public Task Delete(string route, CancellationToken cancellationToken) => RunRequest<object>(route, null, HttpMethod.Delete, null, false, cancellationToken);

		/// <inheritdoc />
		public Task<TResult> Create<TBody, TResult>(string route, TBody body, long instanceId, CancellationToken cancellationToken) => RunRequest<TResult>(route, body, HttpMethod.Put, instanceId, false, cancellationToken);

		/// <inheritdoc />
		public Task<TResult> Read<TResult>(string route, long instanceId, CancellationToken cancellationToken) => RunRequest<TResult>(route, null, HttpMethod.Get, instanceId, false, cancellationToken);

		/// <inheritdoc />
		public Task<TResult> Update<TBody, TResult>(string route, TBody body, long instanceId, CancellationToken cancellationToken) => RunRequest<TResult>(route, body, HttpMethod.Post, instanceId, false, cancellationToken);

		/// <inheritdoc />
		public Task Delete(string route, long instanceId, CancellationToken cancellationToken) => RunRequest<object>(route, null, HttpMethod.Delete, instanceId, false, cancellationToken);

		/// <inheritdoc />
		public Task Delete<TBody>(string route, TBody body, long instanceId, CancellationToken cancellationToken) => RunRequest<object>(route, body, HttpMethod.Delete, instanceId, false, cancellationToken);

		/// <inheritdoc />
		public Task<TResult> Delete<TResult>(string route, long instanceId, CancellationToken cancellationToken) => RunRequest<TResult>(route, null, HttpMethod.Delete, instanceId, false, cancellationToken);

		/// <inheritdoc />
		public Task<TResult> Create<TResult>(string route, long instanceId, CancellationToken cancellationToken) => RunRequest<TResult>(route, new object(), HttpMethod.Put, instanceId, false, cancellationToken);

		/// <inheritdoc />
		public Task<TResult> Patch<TResult>(string route, long instanceId, CancellationToken cancellationToken) => RunRequest<TResult>(route, new object(), HttpMethod.Patch, instanceId, false, cancellationToken);

		/// <inheritdoc />
		public void AddRequestLogger(IRequestLogger requestLogger) => requestLoggers.Add(requestLogger ?? throw new ArgumentNullException(nameof(requestLogger)));
	}
}