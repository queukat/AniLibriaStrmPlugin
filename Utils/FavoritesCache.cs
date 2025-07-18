// ===== ADDED FILE: Utils/FavoritesCache.cs =====
// Простейший in-memory-кэш списка избранных ID, чтобы не плодить
// лишние файловые операции между вызовами задач.

using System.Collections.Generic;

namespace AniLibertyStrmPlugin.Utils
{
    internal static class FavoritesCache
    {
        private static readonly HashSet<int> _ids = new();

        /// <summary>Полностью заменяет содержимое кэша.</summary>
        public static void Update(IEnumerable<int> ids)
        {
            lock (_ids)
            {
                _ids.Clear();
                foreach (var id in ids)
                    _ids.Add(id);
            }
        }

        /// <summary>Проверка наличия ID в кэше.</summary>
        public static bool Contains(int id)
        {
            lock (_ids)
            {
                return _ids.Contains(id);
            }
        }
    }
}