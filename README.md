<p align="center">
  <img src="logo.png" alt="Jellyfin IMDb Ratings Plugin Logo" width="600">
</p>

<h1 align="center">Jellyfin IMDb Ratings Plugin</h1>

<p align="center">
  <strong>Automatically fetch official IMDb community ratings and calculate season averages for your Jellyfin media library.</strong>
</p>

<p align="center">
  <a href="https://github.com/verybadsoldier/jellyfin-plugin-imdbratings/releases"><img src="https://img.shields.io/github/v/release/verybadsoldier/jellyfin-plugin-imdbratings?style=flat-square" alt="Release"></a>
  <a href="https://jellyfin.org/"><img src="https://img.shields.io/badge/Jellyfin-10.11%20%7C%2012%2B-00a4dc?style=flat-square&logo=jellyfin&logoColor=white" alt="Jellyfin Compatibility"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-GPLv3-blue.svg?style=flat-square" alt="License: GPL v3"></a>
</p>

---

**Jellyfin.Plugin.ImdbRatings** is a custom metadata provider for [Jellyfin](https://jellyfin.org/) that imports and maintains community ratings for movies, series, and episodes using official IMDb datasets. It also automatically calculates average IMDb ratings for TV show seasons.

> [!NOTE]
> **Drastically Reduced Memory Footprint (v4.0.0+):**
> The plugin uses an embedded local SQLite database rather than an in-memory cache. It uses virtually zero permanent memory while idle and performs lightning-fast indexed lookups.

## ✨ Features

* ⭐ **Official IMDb Ratings:** Fetches ratings directly from the official IMDb flat-file dataset (`title.ratings.tsv.gz`). No web scraping, no API keys, and no rate limits.
* 🎬 **Automated Episode IMDb ID Resolution:** TV episodes often lack IMDb IDs from primary metadata providers (such as TMDb). The plugin automatically resolves missing episode IMDb IDs in-memory by matching the parent Series IMDb ID, Season number, and Episode number against IMDb's official `title.episode.tsv.gz` dataset to fetch ratings, without altering stored episode provider IDs.
* 🎯 **Flexible Rating Target:** Choose whether IMDb scores are saved as Community Rating, Critic Rating, or both to best fit your client UI and library preferences.
* 📊 **Calculated Season Ratings:** IMDb only provides ratings at the episode level. This plugin automatically computes and assigns weighted/average ratings for entire TV seasons based on their rated episodes.
* ⚡ **Ultra-Low Memory Footprint:** Cached in a compact, indexed SQLite database for fast lookups with near-zero idle RAM usage.
* 🔄 **Automatic Background Sync:** A built-in Jellyfin Scheduled Task keeps ratings fresh as IMDb scores update over time (runs daily at 3:00 AM by default).
* 🧩 **Seamless Provider Integration:** Plugs directly into Jellyfin's native metadata downloaders pipeline for Movies, Series, Seasons, and Episodes.
* 🎛️ **Dashboard & Customization:** View live database status, total indexed titles, disk usage, and customize rating target, cache refresh intervals, or season rating thresholds.

---

## 🚀 Installation

### Method 1: Jellyfin Plugin Repository (Recommended)

1. In your Jellyfin server interface, navigate to **Dashboard** > **Plugins** > **Repositories**.
2. Click the **`+`** icon to add a new repository.
3. Enter the following details:
   * **Repository Name:** `IMDb Ratings`
   * **Repository URL:**
     ```text
     https://verybadsoldier.github.io/jellyfin-plugin-imdbratings/manifest.json
     ```
4. Click **Save**.
5. Switch to the **Catalog** tab, find **IMDb Ratings**, and click **Install**.
6. **Restart** your Jellyfin server.

### Method 2: Manual ZIP Installation

1. Download the latest release `.zip` from the [Releases](https://github.com/verybadsoldier/jellyfin-plugin-imdbratings/releases) page.
2. Extract the `.zip` archive into your Jellyfin server's `plugins` folder (e.g., `plugins/IMDbRatings`).
3. **Restart** your Jellyfin server.

---

## 📖 How To Use

Once the plugin is installed and your server has restarted:

1. In Jellyfin, navigate to **Dashboard** > **Libraries**.
2. Select your **Movie** or **TV Shows** library.
3. Scroll to the metadata downloaders section and enable **The Internet Movie Database Ratings** under:
   * **Movie metadata downloaders**
   * **Series metadata downloaders**
   * **Season metadata downloaders** *(calculates season ratings from episode ratings)*
   * **Episode metadata downloaders**
4. **Order Dependency (Important):**
   Ensure **The Internet Movie Database Ratings** is placed **last at the bottom of the fetcher list** for each item type (below primary fetchers like TheMovieDb, TheTVDB, or OMDb).

> [!IMPORTANT]
> **Why provider ordering matters:**
> To avoid rate limits and scraping blocks, this plugin does not perform title-based searches on IMDb. It performs direct lookups using the media's IMDb ID (`tt...`). Placing this plugin at the bottom of the list ensures your primary scraper (e.g., TMDb) fetches and stores the IMDb ID first, allowing this plugin to immediately apply the rating during the same scan.
> *(If ordered higher, ratings will still apply, but only after the next scheduled scan or metadata refresh once the IMDb ID is present).*

5. Save your changes and re-scan your library or refresh metadata.

---

## ⚙️ Plugin Configuration

Navigate to **Dashboard** > **Plugins** > **IMDb Ratings** to access plugin settings and status:

* **Live Status:** Displays whether the database is ready or updating, total number of indexed ratings, total episodes mapped, database file size on disk, and dataset download timestamp.
* **Rating Target:** Choose whether IMDb ratings should be saved as Community Rating, Critic Rating, or both (default: `Community Rating`).
* **Cache Refresh Interval (Hours):** How often to check for an updated dataset from IMDb (default: `24` hours).
* **Minimum Episode Rating Threshold for Seasons (%):** The percentage (0–100%) of rated episodes required in a season before calculating and assigning an average rating (default: `0%`).
* **Enable Missing Episode IMDb ID Resolution:** Automatically resolve episode IMDb IDs using the official IMDb episode dataset when upstream metadata providers lack them (default: `enabled`).
* **Custom Dataset URLs:** Configure custom mirrors or proxies for `title.ratings.tsv.gz` and `title.episode.tsv.gz`.

---

## ⏰ Scheduled Task: "Update IMDb Ratings"

To keep your library's ratings synchronized as community scores change on IMDb, the plugin registers a scheduled task in Jellyfin.

* **What it does:** Scans your libraries for all Movies, Series, Episodes, and Seasons with the provider enabled, updates item ratings from the local SQLite cache, and recalculates season averages.
* **Default Schedule:** Runs automatically **every day at 3:00 AM**.
* **Manual Execution:** You can run this task at any time or adjust its schedule via **Dashboard** > **Scheduled Tasks** > **Library** > **Update IMDb Ratings**.

---

## 🔍 How It Works

1. The plugin periodically downloads the official IMDb non-commercial datasets (`title.ratings.tsv.gz` and `title.episode.tsv.gz`).
2. The flat-file dataset is extracted and stored locally in a lightweight, indexed SQLite database.
3. When Jellyfin scans media or executes the scheduled task, the plugin matches the item's stored IMDb ID against the SQLite database and updates the rating.
4. If an episode lacks an IMDb ID (common with upstream metadata providers like TMDb), the plugin uses the parent Series IMDb ID along with the Season and Episode numbers to look up the episode's IMDb ID in-memory and applies the official rating without altering stored provider IDs.
5. For seasons, the plugin queries ratings of all corresponding episodes and computes the season average.

---

## 🔧 Troubleshooting

### Missing IMDb IDs for TV episodes
Primary metadata providers (such as TMDb) frequently lack external IMDb IDs for individual episodes. With **Episode IMDb ID Resolution** enabled in the plugin settings (enabled by default), this is handled automatically:
* As long as the parent **Series** has an IMDb ID (which metadata providers almost always have), the plugin matches the Season and Episode numbers against the official IMDb episode dataset in-memory to fetch and apply the official community score, keeping your episode metadata clean and unmodified.
* If a series is completely missing an IMDb ID, ensure the parent Series is identified with an IMDb ID, or use **TheTVDB** or **OMDb** as a secondary provider.

---

## 📋 Compatibility & Requirements

* **Jellyfin Server:** Compatible with **Jellyfin 10.11.x** and **Jellyfin 12.x+**
* **Media Identification:** Requires media items to have an IMDb ID (`tt...`) populated via other metadata providers (TMDb, TVDb, OMDb) or local `.nfo` files.

---

## 📄 Data Notice & License

* **Data Source:** This plugin uses the [IMDb Non-Commercial Datasets](https://developer.imdb.com/non-commercial-datasets/), which are provided free of charge for **personal and non-commercial** use.
* **License:** This project is open source and licensed under the [GNU General Public License v3.0](LICENSE).
