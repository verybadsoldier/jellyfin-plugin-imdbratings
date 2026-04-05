<p align="center">
  <img src="logo.png" alt="Jellyfin IMDb Ratings Plugin Logo" width="600">
</p>

# Jellyfin IMDb Ratings Plugin

**Jellyfin.Plugin.ImdbRatings** is a custom metadata plugin for Jellyfin that automatically fetches and updates community ratings for your media using the Internet Movie Database (IMDb).

## Features

* **IMDb Community Ratings:** Automatically retrieves ratings for Movies, Series, and Episodes. 
* **Official Data Source:** Downloads and caches the official IMDb ratings dataset (`title.ratings.tsv.gz`) directly from IMDb to provide fast, local lookups without third-party resources or API rate limits.
* **Provider Integration:** Acts as a seamless Remote Metadata Provider ("The Internet Movie Database Ratings") that integrates gracefully into Jellyfin's existing metadata refresh pipeline.
* **Automatic Background Updates:** Includes a built-in scheduler task to keep your library's ratings up-to-date as IMDb scores change over time (defaults to every day at 3 AM).

## Important Notes & Prerequisites

* **Requires IMDb IDs (via NFO or other Providers):** To avoid rate limits, this plugin does not perform title-based web searches on IMDb. It requires an IMDb ID (e.g., `tt0111161`) to look up the rating. It is **not** mandatory to have these IDs already inserted into your database via local `.nfo` files, provided you have other metadata fetchers (like TheMovieDb, OMDb, or TheTVDB) enabled for your library to fetch them during the scan.
* **Order Independent:** If your media item does not yet have an IMDb ID, this plugin will automatically query your other enabled metadata providers to fetch the ID before applying the rating. Because of this robust fallback mechanism, **the order of the metadata plugins in your library settings does not matter.**
* **Jellyfin Compatibility:** This plugin is built using .NET 9.0 and requires **Jellyfin Server 10.11.x or newer**. 

## Scheduled Task: "Update IMDb Ratings"

To ensure your media ratings don't become stale, this plugin registers a custom Scheduled Task within Jellyfin. 

* **What it does:** The task scans your library for all Movies, Series, and Episodes that have an associated IMDb ID. It then checks the locally cached IMDb database for the latest community rating and updates your media item if the rating has changed.
* **Default Schedule:** By default, the task is configured to run automatically **every day at 3:00 AM**.
* **Manual Execution:** You can manually trigger this task at any time or change its schedule by navigating to **Dashboard -> Scheduled Tasks -> Library -> Update IMDb Ratings** in your Jellyfin server interface.

## How It Works

The plugin matches your media's existing IMDb ID against the cached IMDb ratings database. If a match is found, the plugin applies the current IMDb community rating to your media item. 

To ensure optimal performance, the plugin caches the IMDb ratings flat file and only refreshes it from the internet if the local data is older than 24 hours.

## Building from Source

1. Clone the repository.
2. Ensure you have the .NET 9.0 SDK installed.
3. Run the following command to build the project in Release mode:
   ```bash
   dotnet build --no-restore -c Release
