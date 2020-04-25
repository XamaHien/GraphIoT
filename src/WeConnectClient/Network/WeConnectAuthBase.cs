﻿using PhilipDaubmeier.WeConnectClient.Model.Auth;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PhilipDaubmeier.WeConnectClient.Network
{
    public abstract class WeConnectAuthBase
    {
        protected readonly IWeConnectConnectionProvider _connectionProvider;

        private const string _userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:66.0) Gecko/20100101 Firefox/66.0";

        private readonly HttpClient _client;
        private readonly HttpClient _authClient;

        private const string _authUri = "https://identity.vwgroup.io";

        private const string _baseUri = "https://www.portal.volkswagen-we.com";
        private const string _landingPageUri = "https://www.portal.volkswagen-we.com/portal/en_GB/web/guest/home";

        private static readonly Semaphore _renewTokenSemaphore = new Semaphore(1, 1);

        private readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public WeConnectAuthBase(IWeConnectConnectionProvider connectionProvider)
        {
            _connectionProvider = connectionProvider;
            _client = connectionProvider.Client;
            _authClient = connectionProvider.AuthClient;
        }

        /// <summary>
        /// Calls the given endpoint at the WeConnect Portal api and ensures the request is authenticated.
        /// </summary>
        protected async Task<HttpResponseMessage> RequestApi(Uri uri)
        {
            await Authenticate();

            if (_connectionProvider.AuthData.AccessToken is null)
                throw new IOException("Can not request data without access token.");

            var request = new HttpRequestMessage()
            {
                RequestUri = uri,
                Method = HttpMethod.Get
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _connectionProvider.AuthData.AccessToken);
            request.Headers.Add("Accept", "application/json");

            return await _client.SendAsync(request);
        }

        /// <summary>
        /// Ensures that after the completion of this task, a valid non-expired access token is
        /// present in the authentication data object, or throws an exception if unsuccessful.
        /// </summary>
        protected async Task Authenticate()
        {
            try
            {
                _renewTokenSemaphore.WaitOne();

                var state = new AuthState();
                await GetInitialCsrf(state);
                await GetLoginPageUri(state);
                await GetLoginRelayState(state);
                await GetLoginHmacToken(state);
                await LoginIdentifier(state);
                await LoginAuthenticate(state);
                await CompleteLogin(state);
                await GetFinalCsrf(state);

                throw new IOException("Could not authenticate");
            }
            catch (Exception innerEx)
            {
                throw new IOException("Could not authenticate, see inner exception for details", innerEx);
            }
            finally
            {
                _renewTokenSemaphore.Release();
            }
        }

        private class AuthState
        {
            public string Csrf { get; set; } = string.Empty;
            public string Referrer { get; set; } = string.Empty;
            public string BaseJsonUri { get; set; } = string.Empty;
            public string LoginUri { get; set; } = string.Empty;
            public string ClientId { get; set; } = string.Empty;
            public string RelayStateToken { get; set; } = string.Empty;
            public string LoginFormUrl { get; set; } = string.Empty;
            public string HmacToken1 { get; set; } = string.Empty;
            public string HmacToken2 { get; set; } = string.Empty;
            public string LoginCsrf { get; set; } = string.Empty;
            public string PortletAuthCode { get; set; } = string.Empty;
            public string PortletAuthState { get; set; } = string.Empty;
        }

        private void AddCommonAuthHeaders(HttpRequestHeaders headers, string? csrf, string? referrer)
        {
            headers.Add("Accept-Language", "en-US,en;q=0.5");
            headers.Add("Accept", "text/html,application/xhtml+xml,application/xml,application/json;q=0.9,*/*;q=0.8");
            headers.Add("Connection", "keep-alive");
            headers.Add("Pragma", "no-cache");
            headers.Add("Cache-Control", "no-cache");
            if (!string.IsNullOrWhiteSpace(csrf))
                headers.Add("X-CSRF-Token", csrf);
            if (!string.IsNullOrWhiteSpace(referrer))
                headers.Referrer = new Uri(referrer);
            headers.TryAddWithoutValidation("User-Agent", _userAgent);
        }

        /// <summary>
        /// Step 1
        /// Get initial CSRF from landing page to get login process started. HttpClient
        /// stores JSESSIONID cookie.
        ///
        /// Step 1a,1b
        /// Note: Portal performs a get-supported-browsers and get-countries at this point.
        /// Those steps are skipped - we assume en_GB
        /// </summary>
        private async Task GetInitialCsrf(AuthState state)
        {
            var landingPageUri = new Uri(new Uri(_baseUri), "/portal/en_GB/web/guest/home");
            var landingPageResponse = await _client.GetAsync(landingPageUri);
            if (!landingPageResponse.IsSuccessStatusCode)
                throw new IOException("Failed getting to portal landing page.");

            var landingPageBody = await landingPageResponse.Content.ReadAsStringAsync();
            if (!landingPageBody.TryExtractCsrf(out string csrf))
                throw new IOException("Failed to get CSRF from landing page.");

            state.Csrf = csrf;
            state.Referrer = landingPageUri.ToString();
        }

        /// <summary>
        /// Step 2
        /// Get login page url. POST returns JSON with loginURL for next step. Returned
        /// loginURL includes client_id for step 4.
        /// </summary>
        private async Task GetLoginPageUri(AuthState state)
        {
            var getLoginUri = new Uri(new Uri(_baseUri), "/portal/en_GB/web/guest/home/-/csrftokenhandling/get-login-url");
            var getLoginRequest = new HttpRequestMessage(HttpMethod.Post, getLoginUri);
            AddCommonAuthHeaders(getLoginRequest.Headers, state.Csrf, state.Referrer);
            var getLoginResponse = await _client.SendAsync(getLoginRequest);

            if (!getLoginResponse.IsSuccessStatusCode)
                throw new IOException("Failed to get login url.");

            var getLoginStream = await getLoginResponse.Content.ReadAsStreamAsync();
            var getLoginBody = await JsonSerializer.DeserializeAsync<LoginPageInfoResponse>(getLoginStream, _jsonSerializerOptions);
            if (int.TryParse(getLoginBody.ErrorCode, out int errorCode) && errorCode != 0)
                throw new IOException($"Error while getting login url, code {getLoginBody.ErrorCode}");
            if (getLoginBody?.LoginUrl?.Path is null)
                throw new IOException("Failed to deserialize body to get login url.");

            state.LoginUri = getLoginBody.LoginUrl.Path;
            if (!new Uri(state.LoginUri).TryExtractUriParameter("client_id", out string clientId))
                throw new IOException("Failed to get client_id.");

            state.ClientId = clientId;
        }

        /// <summary>
        /// Step 3
        /// Get login form url we are told to use, it will give us a new location.
        /// response header location (redirect URL) includes relayState for step 5
        /// https://identity.vwgroup.io/oidc/v1/authorize...
        /// </summary>
        private async Task GetLoginRelayState(AuthState state)
        {
            var loginUrlRequest = new HttpRequestMessage(HttpMethod.Get, state.LoginUri);
            AddCommonAuthHeaders(loginUrlRequest.Headers, state.Csrf, state.Referrer);
            var loginUrlResponse = await _authClient.SendAsync(loginUrlRequest);

            if (loginUrlResponse.StatusCode != HttpStatusCode.Found)
                throw new IOException("Failed to get authorization page.");

            var loginFormUrl = loginUrlResponse.Headers.Location;
            if (!loginFormUrl.TryExtractUriParameter("relayState", out string loginRelayStateToken))
                throw new IOException("Failed to get relay state.");

            state.RelayStateToken = loginRelayStateToken;
            state.LoginFormUrl = loginFormUrl.ToString();
        }

        /// <summary>
        /// Step 4
        /// Get login action url, relay state. hmac token 1 and login CSRF from form contents
        /// https://identity.vwgroup.io/signin-service/v1/signin/<client_id>@relayState=<relay_state>
        /// </summary>
        private async Task GetLoginHmacToken(AuthState state)
        {
            var loginFormLocationRequest = new HttpRequestMessage(HttpMethod.Get, state.LoginFormUrl);
            AddCommonAuthHeaders(loginFormLocationRequest.Headers, state.Csrf, state.Referrer);
            var loginFormLocationResponse = await _client.SendAsync(loginFormLocationRequest);

            if (!loginFormLocationResponse.IsSuccessStatusCode)
                throw new IOException("Failed to get sign-in page.");

            // We get a SESSION set-cookie here!
            // Get hmac and csrf tokens from form content.
            var loginFormPageBody = (await loginFormLocationResponse.Content.ReadAsStringAsync()).Replace("\n", "").Replace("\r", "");
            if (!loginFormPageBody.TryExtractLoginHmac(out string hmac))
                throw new IOException("Failed to get 1st HMAC token.");
            if (!loginFormPageBody.TryExtractLoginCsrf(out string csrf))
                throw new IOException("Failed to get login CSRF.");

            state.HmacToken1 = hmac;
            state.LoginCsrf = csrf;
            state.Referrer = state.LoginFormUrl;
        }

        /// <summary>
        /// Step 5
        /// Post initial login data
        /// https://identity.vwgroup.io/signin-service/v1/<client_id>/login/identifier
        /// </summary>
        private async Task LoginIdentifier(AuthState state)
        {
            var loginActionUri = new Uri(new Uri(_authUri), $"/signin-service/v1/{state.ClientId}/login/identifier");
            var loginActionUrlRequest = new HttpRequestMessage(HttpMethod.Post, loginActionUri);
            AddCommonAuthHeaders(loginActionUrlRequest.Headers, null, state.Referrer);
            loginActionUrlRequest.FormUrlEncoded(new[]
            {
                ("email", _connectionProvider.AuthData.Username),
                ("relayState", state.RelayStateToken),
                ("hmac", state.HmacToken1),
                ("_csrf", state.LoginCsrf)
            });
            var loginActionUrlResponse = await _client.SendAsync(loginActionUrlRequest);

            // performs a 303 redirect to https://identity.vwgroup.io/signin-service/v1/<client_id>/login/authenticate?relayState=<relayState>&email=<email>
            // redirected GET returns form used below.
            if (!loginActionUrlResponse.IsSuccessStatusCode)
                throw new IOException("Failed to get login/identifier page.");

            var loginActionBody = (await loginActionUrlResponse.Content.ReadAsStringAsync()).Replace("\n", "").Replace("\r", "");
            if (!loginActionBody.TryExtractLoginHmac(out string hmac))
                throw new IOException("Failed to get 2nd HMAC token.");

            state.HmacToken2 = hmac;
            state.Referrer = loginActionUri.ToString();
        }

        /// <summary>
        /// Step 6
        /// Post login data to "login action 2" url
        /// https://identity.vwgroup.io/signin-service/v1/<client_id>/login/authenticate
        /// </summary>
        private async Task LoginAuthenticate(AuthState state)
        {
            var loginAction2Uri = new Uri(new Uri(_authUri), $"/signin-service/v1/{state.ClientId}/login/authenticate");
            var loginAction2UrlRequest = new HttpRequestMessage(HttpMethod.Post, loginAction2Uri);
            AddCommonAuthHeaders(loginAction2UrlRequest.Headers, null, state.Referrer);
            loginAction2UrlRequest.FormUrlEncoded(new[]
            {
                ("email", _connectionProvider.AuthData.Username),
                ("password", _connectionProvider.AuthData.UserPassword),
                ("relayState", state.RelayStateToken),
                ("hmac", state.HmacToken2),
                ("_csrf", state.LoginCsrf),
                ("login", "true")
            });
            var loginAction2UrlResponse = await _authClient.SendAsync(loginAction2UrlRequest);

            // performs a 302 redirect to GET https://identity.vwgroup.io/oidc/v1/oauth/sso?clientId=<client_id>&relayState=<relay_state>&userId=<userID>&HMAC=<...>"
            // then a 302 redirect to GET https://identity.vwgroup.io/consent/v1/users/<userID>/<client_id>?scopes=openid%20profile%20birthdate%20nickname%20address%20email%20phone%20cars%20dealers%20mbb&relay_state=1bc582f3ff177afde55b590af92e17a006f9c532&callback=https://identity.vwgroup.io/oidc/v1/oauth/client/callback&hmac=<.....>
            // then a 302 redirect to https://identity.vwgroup.io/oidc/v1/oauth/client/callback/success?user_id=<userID>&client_id=<client_id>&scopes=openid%20profile%20birthdate%20nickname%20address%20email%20phone%20cars%20dealers%20mbb&consentedScopes=openid%20profile%20birthdate%20nickname%20address%20email%20phone%20cars%20dealers%20mbb&relay_state=<relayState>&hmac=<...>
            // then a 302 redirect to https://www.portal.volkswagen-we.com/portal/web/guest/complete-login?state=<csrf>&code=<....>
            Uri? lastLocation = null;
            while (loginAction2UrlResponse.StatusCode == HttpStatusCode.Found)
                loginAction2UrlResponse = await _authClient.GetAsync(lastLocation = loginAction2UrlResponse.Headers.Location);

            if (!loginAction2UrlResponse.IsSuccessStatusCode)
                throw new IOException("Failed to process login sequence.");
            if (lastLocation is null)
                throw new IOException("Failed to read auth code and state from url.");
            if (!lastLocation.TryExtractUriParameter("code", out string authCode))
                throw new IOException("Failed to get portlet code.");
            if (!lastLocation.TryExtractUriParameter("state", out string authState))
                throw new IOException("Failed to get state.");

            state.PortletAuthCode = authCode;
            state.PortletAuthState = authState;
        }

        /// <summary>
        /// Step 7
        /// Site first does a POST https://www.portal.volkswagen-we.com/portal/web/guest/complete-login/-/mainnavigation/get-countries
        /// Post login data to complete login url
        /// https://www.portal.volkswagen-we.com/portal/web/guest/complete-login?p_auth=<state>&p_p_id=33_WAR_cored5portlet&p_p_lifecycle=1&p_p_state=normal&p_p_mode=view&p_p_col_id=column-1&p_p_col_count=1&_33_WAR_cored5portlet_javax.portlet.action=getLoginStatus
        /// </summary>
        private async Task CompleteLogin(AuthState state)
        {
            // TODO
        }

        /// <summary>
        /// Step 8
        /// Get base JSON url for commands
        /// </summary>
        private async Task GetFinalCsrf(AuthState state)
        {
            // TODO
        }
    }
}