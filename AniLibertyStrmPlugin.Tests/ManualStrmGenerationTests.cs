// ===== File: ManualStrmGenerationTests.cs =====
// Ручной тест: берёт избранные релизы и генерит все папки сериалов/сезонов.
//
// ВАЖНО:
//  - Этот тест намеренно SKIP'нут по умолчанию, чтобы не запускаться в CI.
//  - ТОКЕН НЕ ХРАНИМ В РЕПО.
//
// Как запустить локально:
//  1) Задай переменные окружения:
//     ANI_TOKEN      = <JWT AniLiberty>
//     ANI_OUTPUT_DIR = <папка, куда складывать результат>
//  2) Убери Skip у [Fact] (или временно закомментируй Skip строкой).
//  3) Запусти тест.

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniLibertyStrmPlugin.Tests
{
    public class ManualStrmGenerationTests
    {
        // Какое разрешение предпочитать: "1080", "720" или "480"
        private const string PreferredResolution = "1080";

        // Параметры пагинации для избранного
        private const int PageSize = 50;
        private const int MaxPages = 20;

        private static AniLibertyClient NewClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var log  = NullLogger<AniLibertyClient>.Instance;
            return new AniLibertyClient(http, log);
        }

        [Fact(Skip = "Manual test. Set ANI_TOKEN and ANI_OUTPUT_DIR and remove Skip to run locally.")]
        public async Task Generate_Folders_From_Favorites()
        {
            var aniToken = Environment.GetEnvironmentVariable("ANI_TOKEN") ?? string.Empty;
            var outputDir = Environment.GetEnvironmentVariable("ANI_OUTPUT_DIR") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(aniToken))
                throw new InvalidOperationException("ANI_TOKEN is empty. Set env var ANI_TOKEN to your JWT.");

            if (string.IsNullOrWhiteSpace(outputDir))
                throw new InvalidOperationException("ANI_OUTPUT_DIR is empty. Set env var ANI_OUTPUT_DIR to output folder.");

            Directory.CreateDirectory(outputDir);

            var client = NewClient();
            var genLogger = NullLogger<AniLibertyStrmGenerator>.Instance;

            // В тесте Jellyfin-окружения нет → library/chapters = null! (генератор это переживёт)
            IAniLibertyStrmGenerator generator = new AniLibertyStrmGenerator(
                genLogger,
                library: null!,
                chapters: null!,
                client: client
            );

            var ct = CancellationToken.None;

            // 1) тянем избранное
            var favorites = await client.FetchFavoritesAsync(aniToken, PageSize, MaxPages, ct);

            if (favorites.Count == 0)
                throw new InvalidOperationException("Избранное пустое или токен невалиден (API вернул 0 элементов).");

            // 2) генерим STRM/папки/сезоны по тем же правилам, что и в плагине
            await generator.GenerateTitlesAsync(
                favorites,
                outputDir,
                PreferredResolution,
                progress: null,
                token: ct
            );
        }
    }
}
