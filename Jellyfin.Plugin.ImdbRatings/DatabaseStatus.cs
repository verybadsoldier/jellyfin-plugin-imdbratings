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
    }
}
