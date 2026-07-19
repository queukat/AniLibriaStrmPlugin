namespace AniLibertyStrmPlugin.Web;

internal static class AniLibertyPopularityWebUiScript
{
    public static string Build()
        => """
           (() => {
               if (window.__anilibertyPopularityBadgeLoaded) {
                   return;
               }
               window.__anilibertyPopularityBadgeLoaded = true;

               const badgeClass = "aniliberty-popularity-badge";
               const scriptId = "aniliberty-popularity-badge-script";
               const styleId = "aniliberty-popularity-badge-style";
               const popularityEndpoint = "AniLibertyMetadata/Popularity";
               const iconEndpoint = "AniLibertyMetadata/Assets/aniliberty-rating.png";
               const cache = new Map();
               let renderTimer = 0;
               let requestSeq = 0;

               const loaderScript = document.currentScript || document.getElementById(scriptId);
               if (loaderScript) {
                   loaderScript.setAttribute("data-aniliberty-loaded", "true");
               }

               function apiUrl(path, query) {
                   if (window.ApiClient && typeof window.ApiClient.getUrl === "function") {
                       return window.ApiClient.getUrl(path, query || {});
                   }

                   const url = new URL("/" + path.replace(/^\/+/, ""), window.location.origin);
                   Object.entries(query || {}).forEach(([key, value]) => {
                       if (value !== undefined && value !== null && value !== "") {
                           url.searchParams.set(key, String(value));
                       }
                   });
                   return url.toString();
               }

               function readItemIdFromSearch(search) {
                   if (!search) {
                       return "";
                   }

                   const params = new URLSearchParams(search.startsWith("?") ? search.slice(1) : search);
                   return params.get("id") || params.get("itemId") || "";
               }

               function currentItemId() {
                   try {
                       const url = new URL(window.location.href);
                       const direct = readItemIdFromSearch(url.search);
                       if (direct) {
                           return direct;
                       }

                       const hash = url.hash || "";
                       const queryStart = hash.indexOf("?");
                       if (queryStart >= 0) {
                           const fromHashQuery = readItemIdFromSearch(hash.slice(queryStart + 1));
                           if (fromHashQuery) {
                               return fromHashQuery;
                           }
                       }

                       const match = hash.match(/(?:^|[?&#/])(itemId|id)=([0-9a-fA-F-]{32,36})(?:$|[&#/])/);
                       return match ? match[2] : "";
                   } catch {
                       return "";
                   }
               }

               function ensureStyle() {
                   if (document.getElementById(styleId)) {
                       return;
                   }

                   const style = document.createElement("style");
                   style.id = styleId;
                   style.textContent = `
                       .${badgeClass} {
                           display: inline-flex;
                           align-items: center;
                           gap: .25em;
                           min-height: 1em;
                           vertical-align: middle;
                           white-space: nowrap;
                           font-size: inherit;
                           font-weight: inherit;
                           line-height: inherit;
                           color: inherit;
                       }
                       .${badgeClass} img {
                           width: 1.05em;
                           height: 1.05em;
                           object-fit: contain;
                           flex: 0 0 auto;
                           opacity: .92;
                       }
                       .${badgeClass} span {
                           font: inherit;
                           line-height: inherit;
                       }
                   `;
                   document.head.appendChild(style);
               }

               function removeStaleBadges(itemId) {
                   document.querySelectorAll("." + badgeClass).forEach((node) => {
                       if (node.getAttribute("data-item-id") !== itemId) {
                           node.remove();
                       }
                   });
               }

               function hasBadgeForItem(itemId) {
                   return Array.from(document.querySelectorAll("." + badgeClass))
                       .some((node) => node.getAttribute("data-item-id") === itemId);
               }

               function findRatingsAnchor() {
                   const selectors = [
                       ".itemMiscInfo .mediaInfoCriticRating",
                       ".itemMiscInfo .starRatingContainer",
                       ".itemMiscInfo .mediaInfoItem:not(." + badgeClass + "):last-child",
                       ".itemNameContainer .mediaInfoItem:not(." + badgeClass + "):last-child",
                       ".detailPagePrimaryContainer .mediaInfoItem:not(." + badgeClass + "):last-child",
                       ".itemMiscInfo",
                       ".itemNameContainer",
                       ".detailPagePrimaryContainer",
                       "[data-testid='item-detail-header']",
                       "[data-testid='item-detail-overview']",
                       "main h1",
                       "h1"
                   ];

                   for (const selector of selectors) {
                       const node = document.querySelector(selector);
                       if (node) {
                           return node;
                       }
                   }

                   return document.querySelector("main, #reactRoot");
               }

               function formatCompact(value) {
                   try {
                       return new Intl.NumberFormat(undefined, {
                           notation: "compact",
                           maximumFractionDigits: 1
                       }).format(value);
                   } catch {
                       return String(value);
                   }
               }

               function formatFull(value) {
                   try {
                       return new Intl.NumberFormat().format(value);
                   } catch {
                       return String(value);
                   }
               }

               async function getJson(url) {
                   if (window.ApiClient && typeof window.ApiClient.ajax === "function") {
                       try {
                           const data = await window.ApiClient.ajax({
                               type: "GET",
                               url,
                               dataType: "json"
                           });
                           return typeof data === "string" ? JSON.parse(data) : data;
                       } catch {
                           return null;
                       }
                   }

                   try {
                       const response = await fetch(url, {
                           credentials: "same-origin",
                           headers: { "Accept": "application/json" }
                       });
                       return response.ok ? await response.json() : null;
                   } catch {
                       return null;
                   }
               }

               async function loadPopularity(itemId) {
                   if (cache.has(itemId)) {
                       return cache.get(itemId);
                   }

                   const url = apiUrl(popularityEndpoint, { itemId });
                   const data = await getJson(url);
                   cache.set(itemId, data);
                   return data;
               }

               function buildBadge(itemId, data) {
                   const count = Number(data && data.count);
                   if (!Number.isFinite(count) || count < 0) {
                       return null;
                   }

                   const fullCount = formatFull(count);
                   const title = `AniLiberty popularity: ${fullCount} users added this title to favorites`;
                   const badge = document.createElement("span");
                   badge.className = "mediaInfoItem " + badgeClass;
                   badge.setAttribute("data-item-id", itemId);
                   badge.setAttribute("title", title);
                   badge.setAttribute("aria-label", title);

                   const icon = document.createElement("img");
                   icon.src = apiUrl(iconEndpoint);
                   icon.alt = "AniLiberty";
                   icon.loading = "lazy";

                   const value = document.createElement("span");
                   value.textContent = formatCompact(count);

                   badge.appendChild(icon);
                   badge.appendChild(value);
                   return badge;
               }

               async function render() {
                   const itemId = currentItemId();
                   removeStaleBadges(itemId);
                   if (!itemId || hasBadgeForItem(itemId)) {
                       return;
                   }

                   const seq = ++requestSeq;
                   const data = await loadPopularity(itemId);
                   if (seq !== requestSeq || !data || currentItemId() !== itemId || hasBadgeForItem(itemId)) {
                       return;
                   }

                   const anchor = findRatingsAnchor();
                   if (!anchor) {
                       return;
                   }

                   const badge = buildBadge(itemId, data);
                   if (!badge) {
                       return;
                   }

                   ensureStyle();
                   anchor.insertAdjacentElement("afterend", badge);
               }

               function scheduleRender() {
                   window.clearTimeout(renderTimer);
                   renderTimer = window.setTimeout(render, 180);
               }

               function patchHistoryMethod(name) {
                   const original = history[name];
                   if (typeof original !== "function" || original.__anilibertyPopularityBadgePatched) {
                       return;
                   }

                   function patchedHistoryMethod() {
                       const result = original.apply(this, arguments);
                       scheduleRender();
                       return result;
                   }

                   patchedHistoryMethod.__anilibertyPopularityBadgePatched = true;
                   history[name] = patchedHistoryMethod;
               }

               patchHistoryMethod("pushState");
               patchHistoryMethod("replaceState");
               window.addEventListener("popstate", scheduleRender);
               window.addEventListener("hashchange", scheduleRender);
               window.addEventListener("pageshow", scheduleRender);
               document.addEventListener("visibilitychange", scheduleRender);

               const observerTarget = document.body || document.documentElement;
               if (observerTarget) {
                   new MutationObserver(scheduleRender).observe(observerTarget, {
                       childList: true,
                       subtree: true
                   });
               }

               scheduleRender();
           })();
           """;
}
