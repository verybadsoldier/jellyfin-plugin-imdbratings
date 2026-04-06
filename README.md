<p align="center">
  <img src="logo.png" alt="Jellyfin IMDb Ratings Plugin Logo" width="600">
</p>

# Jellyfin IMDb Ratings Plugin

**Jellyfin.Plugin.ImdbRatings** is a custom metadata plugin for Jellyfin that automatically fetches and updates community ratings for your media using the Internet Movie Database (IMDb).

While the source code is open source, the data source for this is the "IMDb Non-Commercial Datasets" which is free to use for **personal and non-commercial projects**:

Link: https://developer.imdb.com/non-commercial-datasets/

## Features

* **IMDb Community Ratings:** Automatically retrieves ratings for Movies, Series, and Episodes. 
* **Official Data Source:** Downloads and caches the official IMDb ratings dataset (`title.ratings.tsv.gz`) directly from IMDb to provide fast, local lookups without web scraping or API rate limits.
* **Provider Integration:** Acts as a seamless Remote Metadata Provider ("The Internet Movie Database Ratings") that integrates gracefully into Jellyfin's existing metadata refresh pipeline.
* **Automatic Background Updates:** Includes a built-in scheduler task to keep your library's ratings up-to-date as IMDb scores change over time (defaults to every day at 3 AM).

## How It Works

The plugin matches your media's existing IMDb ID against the cached IMDb ratings database. If a match is found, the plugin applies the current IMDb community rating to your media item. 

To ensure optimal performance, the plugin caches the IMDb ratings flat file and only refreshes it from the internet if the local data is older than 24 hours.

## How To Use

It's easy: Just enable the plugin in your movie or TV show libraries if your choice. IMDb ratings will be added to your media when adding new media or re-scanning existing libraries.

## Important Notes & Prerequisites

* **Requires IMDb IDs (via NFO or other Providers):** To avoid rate limits, this plugin does not perform title-based web searches on IMDb. It requires an IMDb ID (e.g., `tt0111161`) to look up the rating. It is **not** mandatory to have these IDs already inserted into your database via local `.nfo` files, provided you have other metadata fetchers (like TheMovieDb, OMDb, or TheTVDB) enabled for your library to fetch them during the scan.
* **Order Independent:** If your media item does not yet have an IMDb ID, this plugin will automatically query your other enabled metadata providers to fetch the ID before applying the rating. Because of this robust fallback mechanism, **the order of the metadata plugins in your library settings does not matter.**
* **Jellyfin Compatibility:** This plugin is built using .NET 9.0 and requires **Jellyfin Server 10.11.x or newer**. 

## Scheduled Task: "Update IMDb Ratings"

To ensure your media ratings don't become stale, this plugin registers a custom Scheduled Task within Jellyfin. 

* **What it does:** The task scans your library for all Movies, Series, and Episodes that have an associated IMDb ID. It then checks the locally cached IMDb database for the latest community rating and updates your media item if the rating has changed.
* **Default Schedule:** By default, the task is configured to run automatically **every day at 3:00 AM**.
* **Manual Execution:** You can manually trigger this task at any time or change its schedule by navigating to **Dashboard -> Scheduled Tasks -> Library -> Update IMDb Ratings** in your Jellyfin server interface.


## Troubleshooting 

**IMDb ratings are missing for Episodes after upgrading to Jellyfin 10.11+**
Jellyfin 10.11 heavily refactored its database and metadata parsers. Sometimes, upgrading from 10.10 leaves episode metadata in an incomplete state, or the primary TMDb scraper fails to pull the external IMDb ID for episodes.
If your ratings are not appearing for episodes:
1. Ensure you have a fallback metadata provider like **OMDb** or **TheTVDB** installed and enabled under your library's **Episode Metadata Fetchers**.
2. Go to the affected TV Show, click the three dots (...), and select **Refresh Metadata**.
3. Choose **Replace all metadata**. This forces Jellyfin to rebuild the underlying database links and pull the missing IMDb IDs so this plugin can read them.

## Installation

**Method 1: Using the Jellyfin Plugin Repository (Recommended)**
1. In your Jellyfin server, navigate to **Dashboard -> Plugins -> Repositories**.
2. Click the `+` icon to add a new repository.
3. Enter a name (e.g., "IMDb Ratings") and the following URL: 
   `https://raw.githubusercontent.com/verybadsoldier/jellyfin-plugin-imdbratings/refs/heads/repository/manifest.json`
4. Go to the **Catalog** tab, find "IMDb Ratings", and click Install.
5. Restart your Jellyfin server.

**Method 2: Manual ZIP Installation**
1. Download the latest release `.zip` file from the repository releases page.
2. Extract the `.zip` file into your Jellyfin server's `plugins` directory (e.g., `plugins/IMDbRatings`).
3. Restart your Jellyfin server.

**Post-Installation Steps:**
* Navigate to **Dashboard -> Plugins** to verify the plugin is installed.
* Go to **Dashboard -> Scheduled Tasks** to adjust the execution triggers for the "Update IMDb Ratings" task if desired.

## License

This plugin is licensed under the GNU General Public License v3.0. See the `LICENSE` file for more details.
