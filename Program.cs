namespace TelegramChannelPostsExporter;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using HtmlAgilityPack;

class Program
{
	static async Task Main(string[] args)
	{
		// === COMMAND-LINE SETTINGS ===
		var options = ParseArgs(args);
		ValidateArgs(options);
		var channelUsername = options.ChannelUsername!;
		var startDate = options.StartDate ?? DateTime.UtcNow.AddDays(-2);
		var endDate = options.EndDate ?? DateTime.UtcNow;
		var outputFile = options.OutputFile!;
		var saveImages = options.SaveImages ?? false;
		// ======================================

		using var httpClient = new HttpClient();
		httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

		var allFilteredPosts = new List<TelegramPost>();
		var currentBeforeId = string.Empty;
		var keepScrolling = true;
		var currentOffsetId = 0;

		Console.WriteLine($"Starting post collection for channel @{channelUsername}");
		Console.WriteLine($"Date range: {startDate:yyyy-MM-dd HH:mm:ss} to {endDate:yyyy-MM-dd HH:mm:ss}\n");

		while (keepScrolling)
		{
			var url = $"https://t.me/s/{channelUsername}";
			if (!string.IsNullOrEmpty(currentBeforeId))
			{
				url += $"?before={currentBeforeId}";
			}

			try
			{
				var html = await httpClient.GetStringAsync(url);
				var htmlDoc = new HtmlDocument();
				htmlDoc.LoadHtml(html);

				var messageNodes = htmlDoc.DocumentNode.SelectNodes("//div[contains(@class, 'tgme_widget_message ')]");

				if (messageNodes == null || messageNodes.Count == 0)
				{
					Console.WriteLine("No more posts found.");
					break;
				}

				var pagePosts = new List<TelegramPost>();
				foreach (var node in messageNodes)
				{
					var dataPost = node.GetAttributeValue("data-post", string.Empty);
					var idStr = !string.IsNullOrEmpty(dataPost) ? dataPost.Split('/').LastOrDefault() : string.Empty;

					var textNode = node.SelectSingleNode(".//div[contains(@class, 'tgme_widget_message_text')]");
					var text = textNode != null ? textNode.InnerText.Trim() : string.Empty;

					var timeNode = node.SelectSingleNode(".//time[contains(@class, 'time')]");
					var dateStr = timeNode != null ? timeNode.GetAttributeValue("datetime", string.Empty) : string.Empty;

					var imageUrl = string.Empty;
					if (saveImages)
					{
						// Find the post's photo wrapper.
						var photoNode = node.SelectSingleNode(".//a[contains(@class, 'tgme_widget_message_photo_wrap')]");

						if (photoNode != null)
						{
							var style = photoNode.GetAttributeValue("style", string.Empty);
							var match = Regex.Match(style, @"url\(['""]?(.+?)['""]?\)");
							if (match.Success)
							{
								imageUrl = match.Groups[1].Value;
							}
						}
					}

					if (int.TryParse(idStr, out var id) && DateTime.TryParse(dateStr, out var postDate))
					{
						postDate = postDate.ToUniversalTime();
						pagePosts.Add(new TelegramPost
						{
							Id = id,
							Date = postDate,
							Text = text,
							ImageUrl = imageUrl
						});
					}
				}

				if (pagePosts.Count == 0) break;

				// Sort newest to oldest to paginate backward correctly.
				pagePosts = pagePosts.OrderByDescending(p => p.Id).ToList();

				var matches = pagePosts.Where(p => p.Date >= startDate && p.Date <= endDate).ToList();

				foreach (var post in matches)
				{
					// Convert the image URL to Base64 when one is available.
					if (!string.IsNullOrEmpty(post.ImageUrl))
					{
						try
						{
							Console.WriteLine($"Downloading and encoding the image for post #{post.Id}...");

							// Download the image bytes directly into memory.
							var imageBytes = await httpClient.GetByteArrayAsync(post.ImageUrl);

							// Convert the bytes to a Base64 string.
							var base64String = Convert.ToBase64String(imageBytes);

							// Build a data URL (Telegram serves the preview as a JPEG).
							post.ImageDataUrl = $"data:image/jpeg;base64,{base64String}";
						}
						catch (Exception imgEx)
						{
							Console.WriteLine($"Failed to convert the image for post #{post.Id}: {imgEx.Message}");
						}
					}
					allFilteredPosts.Add(post);
				}

				if (matches.Count > 0)
				{
					Console.WriteLine($"Processed {matches.Count} posts on this page (total collected: {allFilteredPosts.Count}).");
				}

				var oldestPostOnPage = pagePosts.Last();

				if (oldestPostOnPage.Date < startDate)
				{
					Console.WriteLine("\nReached a post older than the date limit. Stopping collection.");
					keepScrolling = false;
				}
				else
				{
					currentBeforeId = oldestPostOnPage.Id.ToString();
					if (currentOffsetId == oldestPostOnPage.Id) break;
					currentOffsetId = oldestPostOnPage.Id;

					// Pause between page requests to reduce the risk of being rate-limited by Telegram.
					await Task.Delay(2000);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Parsing error: {ex.Message}");
				break;
			}
		}

		// Write the resulting JSON.
		if (allFilteredPosts.Count > 0)
		{
			// Sort by ascending ID to keep the JSON in chronological order.
			var finalResult = allFilteredPosts.OrderBy(p => p.Id).ToList();

			var jsonOptions = new JsonSerializerOptions
			{
				WriteIndented = true,
				Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
			};

			var jsonOutput = JsonSerializer.Serialize(finalResult, jsonOptions);
			await File.WriteAllTextAsync(outputFile, jsonOutput);

			Console.WriteLine($"\nSuccess! Exported {finalResult.Count} posts.");
			Console.WriteLine($"JSON saved to: {Path.GetFullPath(outputFile)}");
		}
		else
		{
			Console.WriteLine("\nNo posts were found in the specified date range.");
		}
	}

	private static ProgramArgs ParseArgs(string[] args)
	{
		var result = new ProgramArgs();
		for (int i = 0; i < args.Length; i++)
		{
			var arg = args[i];
			if (arg.StartsWith("--", StringComparison.Ordinal))
			{
				var key = arg[2..].ToLowerInvariant();
				string? value = null;
				if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
				{
					value = args[++i];
				}

				switch (key)
				{
					case "channel" or "username":
						result.ChannelUsername = value;
						break;
					case "start" or "startdate":
						if (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var start))
						{
							result.StartDate = start;
						}
						break;
					case "end" or "enddate":
						if (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var end))
						{
							result.EndDate = end;
						}
						break;
					case "output" or "outfile":
						result.OutputFile = value;
						break;
					case "saveimages":
						if (bool.TryParse(value, out var save))
						{
							result.SaveImages = save;
						}
						break;
				}
			}
		}
		return result;
	}

	private static void ValidateArgs(ProgramArgs options)
	{
		if (string.IsNullOrEmpty(options.ChannelUsername))
		{
			Console.WriteLine("Error: Specify a channel username with --channel.");
			Environment.Exit(1);
		}

		if (string.IsNullOrEmpty(options.OutputFile))
		{
			Console.WriteLine("Error: Specify an output file with --output.");
			Environment.Exit(1);
		}
	}

	public class TelegramPost
	{
		public int Id { get; set; }

		public DateTime Date { get; set; }

		public string? Text { get; set; }

		[System.Text.Json.Serialization.JsonIgnore]
		public string? ImageUrl { get; set; }

		public string? ImageDataUrl { get; set; } // Complete data:image/jpeg;base64,... string.
	}

	private class ProgramArgs
	{
		public string? ChannelUsername { get; set; }
		public DateTime? StartDate { get; set; }
		public DateTime? EndDate { get; set; }
		public string? OutputFile { get; set; }
		public bool? SaveImages { get; set; }
	}
}
