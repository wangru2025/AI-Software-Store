using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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
            await UploadSubmissionAsync(zipPath, null, CancellationToken.None).ConfigureAwait(false);
        }

        public async Task UploadSubmissionAsync(string zipPath, IProgress<ProgressSnapshot> progress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                throw new FileNotFoundException("请选择要上传的 zip 投稿包。", zipPath);
            }

            var watch = Stopwatch.StartNew();
            progress?.Report(new ProgressSnapshot { Percent = 0, Message = "正在本地校验投稿包" });
            await ValidateSubmissionPackageAsync(zipPath).ConfigureAwait(false);
            progress?.Report(new ProgressSnapshot { Percent = 0, Message = "正在准备上传" });

            using (var content = new MultipartFormDataContent())
            using (var file = File.OpenRead(zipPath))
            using (var streamContent = new ProgressableStreamContent(file, (sent, total) =>
            {
                progress?.Report(new ProgressSnapshot
                {
                    Percent = Percent(sent, total),
                    Message = sent >= total ? "正在等待服务器校验" : "正在上传投稿包",
                    BytesTransferred = sent,
                    TotalBytes = total,
                    BytesPerSecond = sent / Math.Max(0.001, watch.Elapsed.TotalSeconds)
                });
            }))
            {
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                content.Add(streamContent, "package", Path.GetFileName(zipPath));
                using (var request = new HttpRequestMessage(HttpMethod.Post, "api/submissions"))
                {
                    if (!string.IsNullOrWhiteSpace(_token))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                    }

                    request.Content = content;
                    using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        progress?.Report(new ProgressSnapshot
                        {
                            Percent = 100,
                            Message = "正在等待服务器校验",
                            BytesTransferred = file.Length,
                            TotalBytes = file.Length,
                            BytesPerSecond = file.Length / Math.Max(0.001, watch.Elapsed.TotalSeconds)
                        });

                        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new ApiException(ParseError(text, response.StatusCode));
                        }
                    }
                }
            }

            progress?.Report(new ProgressSnapshot { Percent = 100, Message = "上传完成", IsCompleted = true });
        }

        private async Task ValidateSubmissionPackageAsync(string zipPath)
        {
            PackageManifest manifest;
            string changelog;
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entries = archive.Entries.ToDictionary(x => x.FullName.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
                if (!entries.ContainsKey("aishop.json"))
                {
                    throw new ApiException("投稿包根目录必须包含 aishop.json。");
                }
                if (!entries.ContainsKey("CHANGELOG.txt"))
                {
                    throw new ApiException("投稿包根目录必须包含 CHANGELOG.txt。");
                }

                manifest = ReadJsonEntry<PackageManifest>(entries["aishop.json"]);
                if (manifest == null ||
                    string.IsNullOrWhiteSpace(manifest.id) ||
                    string.IsNullOrWhiteSpace(manifest.name) ||
                    string.IsNullOrWhiteSpace(manifest.version) ||
                    string.IsNullOrWhiteSpace(manifest.summary))
                {
                    throw new ApiException("aishop.json 必须填写 id、name、version、summary。");
                }

                var install = string.IsNullOrWhiteSpace(manifest.install) ? "install.ps1" : manifest.install;
                RequireRootScript(entries, install, "安装脚本");
                if (!string.IsNullOrWhiteSpace(manifest.uninstall))
                {
                    RequireRootScript(entries, manifest.uninstall, "卸载脚本");
                }
                if (!string.IsNullOrWhiteSpace(manifest.update))
                {
                    RequireRootScript(entries, manifest.update, "更新脚本");
                }

                changelog = ReadTextEntry(entries["CHANGELOG.txt"]);
            }

            if (!ChangelogContainsVersion(changelog, manifest.version))
            {
                throw new ApiException("CHANGELOG.txt 必须包含当前版本，格式为 === 版本号 | 日期 ===。");
            }

            IReadOnlyList<SubmissionItem> mine = IsLoggedIn ? await GetMySubmissionsAsync().ConfigureAwait(false) : new List<SubmissionItem>();
            var published = await GetPublishedSoftwareAsync().ConfigureAwait(false);
            var mySoftware = mine.FirstOrDefault(x => Same(x.SoftwareId, manifest.id));

            foreach (var item in published)
            {
                var sameId = Same(item.Id, manifest.id);
                var sameName = Same(item.Name, manifest.name);
                if ((sameId || sameName) && !IsDeveloper(item))
                {
                    throw new ApiException(sameId ? "这个软件 id 已被其它投稿者使用。" : "这个软件名称已被其它投稿者使用。");
                }
            }

            if (mySoftware != null && CompareVersion(manifest.version, mySoftware.Version) <= 0)
            {
                throw new ApiException("新版本号必须高于当前版本。");
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

        private static int Percent(long transferred, long total)
        {
            if (total <= 0)
            {
                return 0;
            }

            return Math.Max(0, Math.Min(100, (int)(transferred * 100 / total)));
        }

        private static void RequireRootScript(IDictionary<string, ZipArchiveEntry> entries, string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Contains("/") || path.Contains("\\"))
            {
                throw new ApiException(label + "必须位于 zip 根目录。");
            }
            if (!entries.ContainsKey(path))
            {
                throw new ApiException("投稿包根目录缺少" + label + "：" + path);
            }
        }

        private static T ReadJsonEntry<T>(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return JsonConvert.DeserializeObject<T>(reader.ReadToEnd());
            }
        }

        private static string ReadTextEntry(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return reader.ReadToEnd();
            }
        }

        private static bool ChangelogContainsVersion(string text, string version)
        {
            foreach (Match match in ChangelogHeader.Matches(text ?? ""))
            {
                if (Same(match.Groups[1].Value.Trim(), version) &&
                    DateTime.TryParseExact(match.Groups[2].Value, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _))
                {
                    return true;
                }
            }
            return false;
        }

        private static int CompareVersion(string left, string right)
        {
            var leftParts = (left ?? "").Split('.');
            var rightParts = (right ?? "").Split('.');
            var max = Math.Max(leftParts.Length, rightParts.Length);
            for (var i = 0; i < max; i++)
            {
                var l = i < leftParts.Length ? VersionPart(leftParts[i]) : 0;
                var r = i < rightParts.Length ? VersionPart(rightParts[i]) : 0;
                if (l != r)
                {
                    return l.CompareTo(r);
                }
            }
            return 0;
        }

        private static int VersionPart(string value)
        {
            var result = 0;
            foreach (var ch in value ?? "")
            {
                if (ch < '0' || ch > '9')
                {
                    break;
                }
                result = result * 10 + ch - '0';
            }
            return result;
        }

        private static bool Same(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static readonly Regex ChangelogHeader = new Regex(@"(?m)^===\s*([^\|]+?)\s*\|\s*(\d{4}-\d{2}-\d{2})\s*===$", RegexOptions.Compiled);

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
