using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using AIShop.Shared;
using Newtonsoft.Json;

namespace AIShop.Client.Services
{
    public sealed class ApiCatalogService
    {
        private readonly HttpClient _http;
        private readonly AuthStore _authStore;
        private string _token;
        private UserSession _session;

        public ApiCatalogService(string baseUrl, AuthStore authStore)
        {
            _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
            _authStore = authStore;
        }

        public UserSession CurrentUser => _session;

        public bool IsLoggedIn => _session != null && !string.IsNullOrWhiteSpace(_token);

        public bool IsDeveloper(SoftwareItem software)
        {
            return software != null &&
                   _session != null &&
                   string.Equals(software.Author, _session.Username, StringComparison.OrdinalIgnoreCase);
        }

        public Task<IReadOnlyList<SoftwareItem>> GetPublishedSoftwareAsync()
        {
            return GetAsync<IReadOnlyList<SoftwareItem>>("api/software");
        }

        public Task<SoftwareItem> GetSoftwareAsync(string softwareId)
        {
            return GetAsync<SoftwareItem>("api/software/" + Uri.EscapeDataString(softwareId));
        }

        public Task<IReadOnlyList<SubmissionItem>> GetMySubmissionsAsync()
        {
            return GetAsync<IReadOnlyList<SubmissionItem>>("api/me/submissions");
        }

        public Task<IReadOnlyList<RatingItem>> GetRatingsAsync(string softwareId)
        {
            return GetAsync<IReadOnlyList<RatingItem>>("api/software/" + Uri.EscapeDataString(softwareId) + "/ratings");
        }

        public Task<IReadOnlyList<RatingReply>> GetRepliesAsync(string ratingId)
        {
            return GetAsync<IReadOnlyList<RatingReply>>("api/ratings/" + Uri.EscapeDataString(ratingId) + "/replies");
        }

        public async Task LoginAsync(string username, string password)
        {
            var response = await PostAsync<AuthResponse>("api/auth/login", new { username, password }).ConfigureAwait(false);
            SetSession(response.Token, response.User, true);
        }

        public async Task RegisterAsync(string username, string nickname, string password)
        {
            var response = await PostAsync<AuthResponse>("api/auth/register", new { username, nickname, password }).ConfigureAwait(false);
            SetSession(response.Token, response.User, true);
        }

        public void Logout()
        {
            _token = null;
            _session = null;
            _authStore?.Clear();
        }

        public void RestoreSession(string token, UserSession user)
        {
            _token = token;
            _session = user;
        }

        public async Task<bool> RefreshCurrentUserAsync()
        {
            if (!IsLoggedIn)
            {
                return false;
            }

            try
            {
                var user = await GetAsync<UserSession>("api/me").ConfigureAwait(false);
                _session = user;
                _authStore?.Save(_token, _session);
                return true;
            }
            catch
            {
                Logout();
                return false;
            }
        }

        public Task UpdateProfileAsync(string username, string nickname)
        {
            return PostAsync<object>("api/me/profile", new { username, nickname }).ContinueWith(task =>
            {
                if (!task.IsFaulted && !task.IsCanceled)
                {
                    _session = new UserSession { Username = username, Nickname = nickname };
                    _authStore?.Save(_token, _session);
                }
                return task;
            }).Unwrap();
        }

        public Task ChangePasswordAsync(string oldPassword, string newPassword, string repeatedPassword)
        {
            return PostAsync<object>("api/me/password", new { oldPassword, newPassword, repeatedPassword });
        }

        public Task SaveRatingAsync(string softwareId, int stars, string comment)
        {
            return PostAsync<object>("api/software/" + Uri.EscapeDataString(softwareId) + "/ratings", new { stars, comment });
        }

        public Task AddReplyAsync(string ratingId, string parentReplyId, string body)
        {
            return PostAsync<object>("api/ratings/" + Uri.EscapeDataString(ratingId) + "/replies", new { parentReplyId, body });
        }

        public Task ToggleSubmissionStatusAsync(string softwareId)
        {
            return PostAsync<object>("api/me/submissions/" + Uri.EscapeDataString(softwareId) + "/toggle-status", new { });
        }

        public Task UpdateSoftwareInfoAsync(string softwareId, string name, string summary)
        {
            return PostAsync<object>("api/me/submissions/" + Uri.EscapeDataString(softwareId), new { name, summary });
        }

        public Task DeleteSubmissionAsync(string softwareId)
        {
            return PostAsync<object>("api/me/submissions/" + Uri.EscapeDataString(softwareId) + "/delete", new { });
        }

        public Task<UserSession> GetCurrentUserAsync()
        {
            return GetAsync<UserSession>("api/me");
        }

        public async Task UploadSubmissionAsync(string zipPath)
        {
            using (var content = new MultipartFormDataContent())
            using (var file = File.OpenRead(zipPath))
            using (var streamContent = new StreamContent(file))
            {
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                content.Add(streamContent, "package", Path.GetFileName(zipPath));
                await SendAsync<object>(HttpMethod.Post, "api/submissions", content).ConfigureAwait(false);
            }
        }

        public Task<ClientUpdateInfo> CheckClientUpdateAsync(string currentVersion)
        {
            return GetAsync<ClientUpdateInfo>("api/client/update?currentVersion=" + Uri.EscapeDataString(currentVersion ?? ""));
        }

        public string BuildDownloadUrl(string softwareId, string version)
        {
            return new Uri(_http.BaseAddress, "api/software/" + Uri.EscapeDataString(softwareId) + "/versions/" + Uri.EscapeDataString(version) + "/download").ToString();
        }

        private async Task<T> GetAsync<T>(string path)
        {
            return await SendAsync<T>(HttpMethod.Get, path, null).ConfigureAwait(false);
        }

        private Task<T> PostAsync<T>(string path, object body)
        {
            var payload = JsonConvert.SerializeObject(body);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            return SendAsync<T>(HttpMethod.Post, path, content);
        }

        private void SetSession(string token, UserSession user, bool persist)
        {
            _token = token;
            _session = user;
            if (persist)
            {
                _authStore?.Save(_token, _session);
            }
        }

        private async Task<T> SendAsync<T>(HttpMethod method, string path, HttpContent content)
        {
            using (var request = new HttpRequestMessage(method, path))
            {
                if (!string.IsNullOrWhiteSpace(_token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                }

                request.Content = content;

                using (var response = await _http.SendAsync(request).ConfigureAwait(false))
                {
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new ApiException(ParseError(text, response.StatusCode));
                    }

                    if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(text))
                    {
                        return default(T);
                    }

                    return JsonConvert.DeserializeObject<T>(text);
                }
            }
        }

        private string ParseError(string text, HttpStatusCode statusCode)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    var error = JsonConvert.DeserializeObject<ErrorResponse>(text);
                    if (!string.IsNullOrWhiteSpace(error.Error))
                    {
                        return error.Error;
                    }
                }
                catch
                {
                }
            }

            return "请求失败：" + (int)statusCode;
        }

        private sealed class AuthResponse
        {
            public string Token { get; set; }
            public UserSession User { get; set; }
        }

        private sealed class ErrorResponse
        {
            public string Error { get; set; }
        }
    }

    public sealed class ClientUpdateInfo
    {
        public bool HasUpdate { get; set; }
        public string Version { get; set; }
        public string Changelog { get; set; }
        public string DownloadUrl { get; set; }
        public string Sha256 { get; set; }
    }
}
