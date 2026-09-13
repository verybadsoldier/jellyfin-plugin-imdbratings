using System;

namespace Jellyfin.Plugin.ImdbRatings
{
    /// <summary>
    /// Represents the status of the local IMDb ratings database.
    /// </summary>
    public class DatabaseStatus
    {
        /// <summary>
        /// Gets or sets a value indicating whether the database file exists.
        /// </summary>
        public bool DatabaseExists { get; set; }

        /// <summary>
        /// Gets or sets the last modified time in UTC.
        /// </summary>
        public DateTime? LastModifiedUtc { get; set; }

        /// <summary>
        /// Gets or sets the database file size in bytes.
        /// </summary>
        public long SizeBytes { get; set; }

        /// <summary>
        /// Gets or sets the total number of ratings in the database.
        /// </summary>
        public int? TotalRatings { get; set; }

        /// <summary>
        /// Gets or sets the total number of mapped episodes in the database.
        /// </summary>
        public int? TotalEpisodes { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a database update is in progress.
        /// </summary>
        public bool IsUpdating { get; set; }

        /// <summary>
        /// Gets or sets the configured refresh interval in hours.
        /// </summary>
        public int RefreshIntervalHours { get; set; }

        /// <summary>
        /// Gets or sets the configured dataset download URL.
        /// </summary>
        public string DatasetUrl { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether episode resolution is enabled.
        /// </summary>
        public bool EnableEpisodeResolution { get; set; }

        /// <summary>
        /// Gets or sets the configured episode dataset download URL.
        /// </summary>
        public string EpisodeDatasetUrl { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the total number of library episodes evaluated during the last scan.
        /// </summary>
        public int? LibraryEpisodesTotal { get; set; }

        /// <summary>
        /// Gets or sets the number of library episodes that were missing an IMDb ID.
        /// </summary>
        public int? LibraryEpisodesMissingId { get; set; }

        /// <summary>
        /// Gets or sets the number of missing library episode IDs resolved by this feature.
        /// </summary>
        public int? LibraryEpisodesResolved { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of the last library ratings scan in UTC.
        /// </summary>
        public DateTime? LastLibraryScanUtc { get; set; }
    }
}
