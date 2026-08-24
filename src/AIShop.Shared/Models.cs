using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AIShop.Shared
{
    public enum SoftwareStatus
    {
        Draft,
        Published
    }

    public enum UpdateMode
    {
        Script,
        InstallOver,
        CleanInstall,
        Manual
    }

    public sealed class SoftwareItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public string Summary { get; set; }
        public DateTime PublishedAt { get; set; }
        public int DownloadCount { get; set; }
        public string PackageSha256 { get; set; }
        public double AverageRating { get; set; }
        public int RatingCount { get; set; }
        public SoftwareStatus Status { get; set; }
        public List<ChangelogEntry> Changelogs { get; set; } = new List<ChangelogEntry>();

        public string ToMainListText()
        {
            var rating = RatingCount == 0
                ? "暂无评分，0个评分"
                : string.Format(CultureInfo.InvariantCulture, "{0:0.0}星，{1}个评分", AverageRating, RatingCount);

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}，版本：{1}，作者，{2}，发布时间：{3:yyyy-MM-dd}，下载量：{4}，评价：{5}",
                Name,
                Version,
                Author,
                PublishedAt,
                DownloadCount,
                rating);
        }
    }

    public sealed class SubmissionItem
    {
        public string SoftwareId { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Summary { get; set; }
        public DateTime PublishedAt { get; set; }
        public int DownloadCount { get; set; }
        public double AverageRating { get; set; }
        public int RatingCount { get; set; }
        public SoftwareStatus Status { get; set; }

        public string ToListText()
        {
            var rating = RatingCount == 0
                ? "暂无评分，0个评分"
                : string.Format(CultureInfo.InvariantCulture, "{0:0.0}星，{1}个评分", AverageRating, RatingCount);

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}，版本：{1}，发布时间：{2:yyyy-MM-dd}，下载量：{3}，评价：{4}，状态：{5}",
                Name,
                Version,
                PublishedAt,
                DownloadCount,
                rating,
                Status == SoftwareStatus.Draft ? "草稿" : "上架");
        }
    }

    public sealed class ChangelogEntry
    {
        public string Version { get; set; }
        public DateTime Date { get; set; }
        public string Body { get; set; }

        public string ToListText()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}，{1:yyyy-MM-dd}", Version, Date);
        }
    }

    public sealed class RatingItem
    {
        public string Id { get; set; }
        public string SoftwareId { get; set; }
        public string Username { get; set; }
        public string Nickname { get; set; }
        public int Stars { get; set; }
        public string Comment { get; set; }
        public DateTime CreatedAt { get; set; }
        public int ReplyCount { get; set; }

        public string ToListText()
        {
            var comment = string.IsNullOrWhiteSpace(Comment) ? "无评论" : Comment.Trim();
            if (comment.Length > 30)
            {
                comment = comment.Substring(0, 30) + "...";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}，{1}星，{2:yyyy-MM-dd}，{3}，回复：{4}",
                Nickname,
                Stars,
                CreatedAt,
                comment,
                ReplyCount);
        }
    }

    public sealed class RatingReply
    {
        public string Id { get; set; }
        public string RatingId { get; set; }
        public string ParentReplyId { get; set; }
        public string Nickname { get; set; }
        public string Body { get; set; }
        public DateTime CreatedAt { get; set; }

        public string ToListText()
        {
            var prefix = string.IsNullOrWhiteSpace(ParentReplyId) ? "" : "回复：";
            return string.Format(CultureInfo.InvariantCulture, "{0}{1}，{2:yyyy-MM-dd}，{3}", prefix, Nickname, CreatedAt, Body);
        }
    }

    public sealed class UserSession
    {
        public string Username { get; set; }
        public string Nickname { get; set; }
    }

    public sealed class PackageManifest
    {
        public string id { get; set; }
        public string name { get; set; }
        public string version { get; set; }
        public string author { get; set; }
        public string summary { get; set; }
        public bool requiresAdmin { get; set; }
        public string install { get; set; }
        public string uninstall { get; set; }
        public string update { get; set; }
        public string updateMode { get; set; }
    }

    public sealed class InstalledPackage
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string InstallLocation { get; set; }
        public string PackageCacheDir { get; set; }
        public string UninstallCommand { get; set; }
        public string UninstallArguments { get; set; }
        public DateTime InstalledAt { get; set; }
    }

    public sealed class ProgressSnapshot
    {
        public int Percent { get; set; }
        public string Message { get; set; }
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public double BytesPerSecond { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsFailed { get; set; }
    }

    public static class RatingCalculator
    {
        public static double Average(IEnumerable<RatingItem> ratings)
        {
            var list = ratings.Where(x => x != null).ToList();
            if (list.Count == 0)
            {
                return 0;
            }

            return Math.Round(list.Average(x => x.Stars), 1, MidpointRounding.AwayFromZero);
        }
    }
}
