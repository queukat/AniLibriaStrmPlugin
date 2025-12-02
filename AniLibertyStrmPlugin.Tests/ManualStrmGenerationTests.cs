// ===== File: ManualStrmGenerationTests.cs =====
// Ручной тест: берёт избранные релизы и генерит все папки сериалов/сезонов.
// 1) Вставь AniToken и OutputDir ниже.
// 2) Сними Skip у теста ([Fact] без параметров).
// 3) Запусти через тестовый раннер.

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
        // ← ВСТАВЬ СЮДА СВОЙ JWT AniLiberty
        private const string AniToken = "eyJpdiI6InhHUGNVd1hiNFFKbVczenU5Zm9rTWc9PSIsInZhbHVlIjoidGdOejRwUko2RU5VTDFhTkwweXh1UTlXOWlaRGNqdmJGS2JJVTNzYVU3bC8yd0FwRWpubHVLZ0JzRVI0anl0VyIsIm1hYyI6IjlkNWFlMmVjYTVjZTY2YmUwNzBmNGI0ZDk0YzdlZGJiYzc3MzdjZDE4YWRlZGQzMWQ0ODA3NjQwYzg5NDJmMDYiLCJ0YWciOiIifQ";

        // ← КУДА СКЛАДЫВАТЬ ПАПКИ СЕРИАЛОВ/СЕЗОНОВ
        private const string OutputDir = @"D:\video\Anime\AniLibertyManualTest";

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

        // <<< ВАЖНО: вот этого атрибута не хватало >>>
        [Fact()]
        public async Task Generate_Folders_From_Favorites()
        {
            if (string.IsNullOrWhiteSpace(AniToken) || AniToken == "PASTE_YOUR_TOKEN_HERE")
                throw new InvalidOperationException("Сначала вставь JWT в константу AniToken.");

            if (string.IsNullOrWhiteSpace(OutputDir))
                throw new InvalidOperationException("OutputDir не задан.");

            Directory.CreateDirectory(OutputDir);

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
            var favorites = await client.FetchFavoritesAsync(AniToken, PageSize, MaxPages, ct);

            if (favorites.Count == 0)
                throw new InvalidOperationException("Избранное пустое или токен невалиден (API вернул 0 элементов).");

            // 2) генерим STRM/папки/сезоны по тем же правилам, что и в плагине
            await generator.GenerateTitlesAsync(
                favorites,
                OutputDir,
                PreferredResolution,
                progress: null,
                token: ct
            );
        }
    }
}
