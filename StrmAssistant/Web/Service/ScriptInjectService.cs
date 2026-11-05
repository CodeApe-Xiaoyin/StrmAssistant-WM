using System.Text;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    /// <summary>对应 PRO 的 ScriptInjectService：向前端返回注入脚本。</summary>
    [Unauthenticated]
    public class ScriptInjectService : IService, IRequiresRequest
    {
        private const string Js = @"
(function () {
    if (window.__strmAssistant) return;

    var api = { version: '1.2', config: null, stats: { hiddenPersons: 0, runs: 0 } };
    window.__strmAssistant = api;

    function injectStyle() {
        if (document.getElementById('strm-assistant-style')) return;
        var style = document.createElement('style');
        style.id = 'strm-assistant-style';
        style.textContent =
            'html.strm-assistant .strm-hidden-person { display: none !important; }' +
            'html.strm-assistant.strm-reverse-episodes .childrenItemsContainer { flex-direction: row-reverse !important; }' +
            'html.strm-assistant.strm-reverse-seasons .seriesItemsSection .childrenItemsContainer { flex-direction: row-reverse !important; }';
        (document.head || document.documentElement).appendChild(style);
        document.documentElement.classList.add('strm-assistant');
    }

    function hasChinese(text) {
        for (var i = 0; i < text.length; i++) {
            var code = text.charCodeAt(i);
            if (code >= 0x4E00 && code <= 0x9FFF) return true;
        }
        return false;
    }

    function applyHidePerson() {
        var cfg = api.config;
        if (!cfg) return;

        var hideNoImage = cfg.HidePersonNoImage === true || cfg.hidePersonNoImage === true;
        var hideNonChinese = cfg.HidePersonNonChinese === true || cfg.hidePersonNonChinese === true;
        var actorOnly = cfg.HidePersonActorOnly === true || cfg.hidePersonActorOnly === true;
        if (!hideNoImage && !hideNonChinese && !actorOnly) return;

        var cards = document.querySelectorAll('.personItems .cardBox, .personsContainer .cardBox, ' +
            '.peopleItemsContainer .cardBox');
        var hidden = 0;

        for (var i = 0; i < cards.length; i++) {
            var card = cards[i];
            var nameEl = card.querySelector('.cardText');
            var name = nameEl ? (nameEl.textContent || '') : '';

            // Emby 有头像时渲染 img.cardImage，没有头像时渲染 i.cardImageIcon 占位图标
            var hasImage = !!card.querySelector('img.cardImage');
            var placeholder = !!card.querySelector('.cardImageIcon');

            var secondaryEl = card.querySelector('.cardText-secondary, .secondaryText, .cardText-tiny');
            var role = secondaryEl ? (secondaryEl.textContent || '').trim() : '';

            var hide = false;
            if (hideNoImage && (!hasImage || placeholder)) hide = true;
            if (!hide && hideNonChinese && name) {
                if (!hasChinese(name)) hide = true;
            }
            if (!hide && actorOnly) {
                // 只保留演员：角色为空或明显非表演岗位的隐藏
                if (!role) hide = true;
                else if (/director|writer|producer|creator|composer|导演|導演|编剧|編劇|制片|製片|监制|監製|出品|原著|作曲|摄影|攝影/i.test(role)) hide = true;
            }

            if (hide && !card.classList.contains('strm-hidden-person')) {
                card.classList.add('strm-hidden-person');
                hidden++;
            }
        }

        api.stats.hiddenPersons += hidden;
    }

    // 首页媒体库条目数
    function applyLibraryCount() {
        var cfg = api.config;
        if (!cfg) return;
        var on = cfg.ShowLibraryItemCount === true || cfg.showLibraryItemCount === true;
        if (!on) return;
        if (!window.ApiClient || !window.ApiClient.getCurrentUserId) return;

        var userId = window.ApiClient.getCurrentUserId();
        var cards = document.querySelectorAll('.homeSectionsContainer .cardBox');
        if (!cards.length) return;

        // 走 getJson 的缓存：页面每次变化都会调到这里，以前每次都直接发请求（条目数为 0 的库永远没有徽标，会一直重发）
        getJson('Users/' + userId + '/Views', function (res) {
            var views = (res && res.Items) || [];
            for (var i = 0; i < cards.length; i++) {
                var card = cards[i];
                if (card.getAttribute('data-strm-count')) continue;

                var textEl = card.querySelector('.cardText, .text');
                var name = textEl ? (textEl.textContent || '').trim() : '';
                if (!name) continue;

                var view = null;
                for (var j = 0; j < views.length; j++) {
                    if (views[j].Name === name) { view = views[j]; break; }
                }
                if (!view) continue;

                card.setAttribute('data-strm-count', 'pending');
                (function (targetCard, viewId) {
                    getJson('Users/' + userId + '/Items?ParentId=' + viewId +
                        '&Recursive=true&IncludeItemTypes=Movie,Series&Limit=0', function (r) {
                        var total = (r && r.TotalRecordCount) || 0;
                        targetCard.setAttribute('data-strm-count', String(total));
                        if (!total || targetCard.querySelector('.strm-count-badge')) return;

                        var badge = document.createElement('div');
                        badge.className = 'strm-count-badge';
                        badge.textContent = total;
                        badge.style.cssText = 'position:absolute;top:4px;right:4px;background:rgba(0,0,0,.75);' +
                            'color:#fff;font-size:11px;line-height:1;padding:3px 6px;border-radius:10px;z-index:5;';
                        if (getComputedStyle(targetCard).position === 'static') {
                            targetCard.style.position = 'relative';
                        }
                        targetCard.appendChild(badge);
                    });
                })(card, view.Id);
            }
        });
    }

    // 未看集数显示为总集数
    function applyReplaceUnplayed() {
        var cfg = api.config;
        if (!cfg) return;
        var on = cfg.ReplaceUnplayedWithTotal === true || cfg.replaceUnplayedWithTotal === true;
        if (!on) return;
        if (!window.ApiClient || !window.ApiClient.getCurrentUserId) return;

        var cards = Array.from(document.querySelectorAll('.cardBox')).filter(function (c) {
            return c.querySelector('.countIndicator');
        });
        if (!cards.length) return;

        var userId = window.ApiClient.getCurrentUserId();

        // 走 getJson 的缓存（2 分钟）：这是个很重的递归查询，以前页面每变一次就发一次
        getJson('Users/' + userId + '/Items?Recursive=true&IncludeItemTypes=Series' +
            '&Fields=RecursiveItemCount&Limit=500', function (res) {
            var items = (res && res.Items) || [];
            for (var i = 0; i < cards.length; i++) {
                var card = cards[i];
                var badge = card.querySelector('.countIndicator');
                var textEl = card.querySelector('.cardText, .text');
                var name = textEl ? (textEl.textContent || '').trim() : '';
                if (!badge || !name) continue;

                var match = null;
                for (var j = 0; j < items.length; j++) {
                    if (items[j].Name === name) { match = items[j]; break; }
                }
                if (!match) continue;

                var total = match.RecursiveItemCount || 0;
                if (total > 0 && badge.textContent.trim() !== String(total)) {
                    badge.textContent = total;
                }
            }
        });
    }

    function applyItemOrder() {
        var cfg = api.config;
        if (!cfg) return;

        var reverseEpisodes = cfg.ReverseEpisodeOrder === true || cfg.reverseEpisodeOrder === true;
        var reverseSeasons = cfg.ReverseSeasonOrder === true || cfg.reverseSeasonOrder === true;

        document.documentElement.classList.toggle('strm-reverse-episodes', reverseEpisodes);
        document.documentElement.classList.toggle('strm-reverse-seasons', reverseSeasons);
    }

    // 日志倒序：最新的日志显示在最上面
    function applyReverseLogLines() {
        var cfg = api.config;
        if (!cfg) return;
        var on = cfg.ReverseLogLines === true || cfg.reverseLogLines === true;
        if (!on) return;

        var containers = document.querySelectorAll('.logContainer, .log-container, #logContainer');
        for (var i = 0; i < containers.length; i++) {
            var c = containers[i];
            if (!c || c.getAttribute('data-strm-reversed') === '1') continue;
            if (!c.children || c.children.length < 2) continue;

            var kids = Array.prototype.slice.call(c.children).reverse();
            for (var k = 0; k < kids.length; k++) c.appendChild(kids[k]);
            c.setAttribute('data-strm-reversed', '1');
        }
    }

    // ---- 详情页公共：当前条目 id、带缓存的 GET（同一个 url 只请求一次） ----
    var cache = {};
    function detailId() {
        var m = /[?&]id=([0-9a-z]+)/i.exec(location.hash || '');
        return m ? m[1] : null;
    }
    // 当前真正显示的页面。Emby 会把看过的页面留在 DOM 里，只是 display:none、不一定带 .hide，
    // 以前取第一个「不带 .hide」的 view，换页后拿到的是隐藏着的旧页面。
    function currentView() {
        var views = document.querySelectorAll('.view');
        for (var i = views.length - 1; i >= 0; i--) {
            var v = views[i];
            if (!v.classList.contains('hide') && v.offsetWidth > 0 && v.offsetHeight > 0) return v;
        }
        return document.querySelector('.view:not(.hide)') || document.body;
    }
    function getJson(url, cb) {
        var hit = cache[url];
        if (hit && hit.done) { cb(hit.data); return; }
        if (hit) { hit.waiters.push(cb); return; }
        hit = cache[url] = { done: false, data: null, waiters: [cb] };
        if (!window.ApiClient) return;
        var xhr = new XMLHttpRequest();
        xhr.open('GET', window.ApiClient.getUrl(url), true);
        xhr.setRequestHeader('X-Emby-Token', window.ApiClient.accessToken ? window.ApiClient.accessToken() : '');
        xhr.onload = function () {
            try { hit.data = xhr.status === 200 ? JSON.parse(xhr.responseText) : null; } catch (e) { hit.data = null; }
            hit.done = true;
            var ws = hit.waiters; hit.waiters = [];
            ws.forEach(function (w) { try { w(hit.data); } catch (e) { } });
        };
        xhr.onerror = function () { hit.done = true; };
        xhr.send();
        // 缓存 2 分钟，之后允许重新取（集数/合集可能变化）
        setTimeout(function () { delete cache[url]; }, 120000);
    }
    function userId() {
        return window.ApiClient && window.ApiClient.getCurrentUserId ? window.ApiClient.getCurrentUserId() : null;
    }

    // 美化标题显示：没刮到元数据的分集，标题就是文件名（「测试剧.S01E01.1080p.WEB-DL」），
    // 在剧集 / 季 / 分集详情页里显示成「第 1 集」。只改页面显示，不改数据库。
    var prettyNames = {};
    var prettySeriesId = null;
    function looksLikeFileName(ep) {
        var name = (ep.Name || '').trim();
        if (!name) return false;
        var file = (ep.Path || '').split(/[\\/]/).pop().replace(/\.[^.]+$/, '');
        if (file && name === file) return true;
        if (/S\d{1,2}\s*E\d{1,4}/i.test(name)) return true;
        // 「EP01」「01」「第1话」这类纯序号标题：数字要和集号对得上才算
        var m = /^(?:EP?|第)?\s*(\d{1,4})\s*(?:集|话|話)?$/i.exec(name);
        return !!m && parseInt(m[1], 10) === ep.IndexNumber && !/^第 \d+ 集$/.test(name);
    }
    function collectPretty(items) {
        (items || []).forEach(function (ep) {
            if (ep.Type !== 'Episode' || ep.IndexNumber == null || !looksLikeFileName(ep)) return;
            var pretty = '第 ' + ep.IndexNumber + (ep.IndexNumberEnd && ep.IndexNumberEnd !== ep.IndexNumber ? '-' + ep.IndexNumberEnd : '') + ' 集';
            // 纯数字的名字（「01」「1」）不收：页面上到处都是这种数字，替换会误伤
            if (/^\d+$/.test((ep.Name || '').trim())) return;
            if (ep.Name !== pretty) prettyNames[ep.Name.trim()] = pretty;
        });
    }
    // 只替换「整段文字就是这个名字」的文本（允许前面带「S1:E1 - 」「1. 」这种序号），不做子串替换：
    // 以前按子串替换，名字是「01」时页面上的「2001」会变成「20第 1 集」。
    function replacePrettyText() {
        if (!Object.keys(prettyNames).length) return;
        var walker = document.createTreeWalker(currentView(), NodeFilter.SHOW_TEXT, null);
        var node;
        while ((node = walker.nextNode())) {
            var text = node.nodeValue;
            if (!text) continue;
            var parent = node.parentNode;
            if (!parent || /^(SCRIPT|STYLE|TEXTAREA|INPUT)$/.test(parent.nodeName)) continue;
            var trimmed = text.trim();
            if (prettyNames.hasOwnProperty(trimmed)) {
                node.nodeValue = text.replace(trimmed, prettyNames[trimmed]);
                continue;
            }
            // Emby 的分集卡片是「S1:E1 - 名字」，列表里也有「1. 名字」
            var m = /^((?:S\d+\s*:\s*E\d+(?:-\d+)?|\d+)\s*[.．、:\-–]\s*)(.+)$/.exec(trimmed);
            if (m && prettyNames.hasOwnProperty(m[2])) node.nodeValue = text.replace(trimmed, m[1] + prettyNames[m[2]]);
        }
    }
    function applyBeautifyMissingMetadata() {
        var cfg = api.config;
        if (!cfg) return;
        var on = cfg.BeautifyMissingMetadata === true || cfg.beautifyMissingMetadata === true;
        if (!on) return;

        var id = detailId(), uid = userId();
        if (id && uid) {
            getJson('Users/' + uid + '/Items/' + id, function (item) {
                if (!item) return;
                var seriesId = item.Type === 'Series' ? item.Id : item.SeriesId;
                if (!seriesId) return;
                // 换了一部剧就清空：以前名字表从不清空，换到别的页面也继续替换
                if (seriesId !== prettySeriesId) { prettyNames = {}; prettySeriesId = seriesId; }
                var url = 'Shows/' + seriesId + '/Episodes?UserId=' + uid + '&Fields=Path';
                getJson(url, function (res) {
                    if (prettySeriesId !== seriesId) return;
                    collectPretty(res && res.Items);
                    replacePrettyText();
                });
            });
        }
    }

    // 按季显示合集：合集里放的是「季」时，剧集详情页原生的「收录于」看不到它。
    // 这里在剧集详情页追加一个区块，列出包含本剧任意一季的合集。
    function applyShowCollectionsFromSeasons() {
        var cfg = api.config;
        if (!cfg) return;
        var on = cfg.ShowCollectionsFromSeasons === true || cfg.showCollectionsFromSeasons === true;
        if (!on) return;

        var id = detailId(), uid = userId();
        if (!id || !uid) return;
        var view = currentView();
        var existing = view.querySelector('.strm-collections-section');
        if (existing && existing.getAttribute('data-item') === id) return;
        if (existing) existing.parentNode.removeChild(existing);

        getJson('Users/' + uid + '/Items/' + id, function (item) {
            if (!item || item.Type !== 'Series') return;
            getJson('Shows/' + id + '/Seasons?UserId=' + uid, function (seasons) {
                var ids = ((seasons && seasons.Items) || []).map(function (x) { return x.Id; });
                if (!ids.length) return;
                getJson('Users/' + uid + '/Items?Recursive=true&IncludeItemTypes=BoxSet&ListItemIds=' + ids.join(','), function (res) {
                    var boxes = (res && res.Items) || [];
                    if (!boxes.length || detailId() !== id) return;
                    var v = currentView();
                    if (v.querySelector('.strm-collections-section')) return;
                    var host = v.querySelector('.details-additionalContent') || v.querySelector('.detailPageContent');
                    if (!host) return;

                    var section = document.createElement('div');
                    section.className = 'verticalSection strm-collections-section';
                    section.id = 'strm-collections-section';
                    section.setAttribute('data-item', id);
                    var title = document.createElement('h2');
                    title.className = 'sectionTitle padded-left';
                    title.textContent = '包含本剧的合集';
                    section.appendChild(title);
                    var row = document.createElement('div');
                    row.className = 'padded-left padded-right';
                    boxes.forEach(function (box) {
                        var a = document.createElement('a');
                        a.href = '#!/item?id=' + box.Id + '&serverId=' + (box.ServerId || '');
                        a.className = 'button-link emby-button';
                        a.style.marginRight = '1.5em';
                        a.textContent = box.Name;
                        row.appendChild(a);
                    });
                    section.appendChild(row);
                    host.insertBefore(section, host.firstChild);
                });
            });
        });
    }

    // 关闭搜索建议：把建议浮层隐藏掉
    function applySuppressSearchSuggestions() {
        var cfg = api.config;
        if (!cfg || !cfg.SearchTuningPreferences) return;
        if (cfg.SearchTuningPreferences.indexOf('SuppressSearchSuggestions') < 0) return;

        var id = 'strm-suppress-suggestions';
        if (document.getElementById(id)) return;

        var style = document.createElement('style');
        style.id = id;
        style.textContent = '.searchSuggestions, .autocomplete, .searchSuggestionsContainer { display: none !important; }';
        (document.head || document.documentElement).appendChild(style);
    }

    // 搜索结果显示媒体信息：在结果卡片上加清晰度徽标
    function applyShowMediaInfo() {
        var cfg = api.config;
        if (!cfg || !cfg.SearchTuningPreferences) return;
        if (cfg.SearchTuningPreferences.indexOf('ShowMediaInfo') < 0) return;

        var m = /[?#&]query=([^&]+)/.exec(location.hash || '');
        if (!m) return;
        var term = decodeURIComponent(m[1]);
        if (!term) return;

        if (!window.ApiClient || !window.ApiClient.getCurrentUserId) return;
        var userId = window.ApiClient.getCurrentUserId();

        // 同一个搜索词只查一次（走 getJson 缓存）：以前搜索页每变一次就发一次带 MediaSources 的递归搜索
        getJson('Users/' + userId + '/Items?Recursive=true&SearchTerm=' +
            encodeURIComponent(term) + '&Fields=MediaSources&Limit=60', function (res) {
            var items = (res && res.Items) || [];

            var byName = {};
            items.forEach(function (it) {
                var src = (it.MediaSources || [])[0];
                if (!src) return;
                var label = mediaLabel(src);
                if (label) byName[it.Name] = label;
            });

            var cards = document.querySelectorAll('.cardBox');
            for (var i = 0; i < cards.length; i++) {
                var card = cards[i];
                if (card.querySelector('.strm-media-badge')) continue;

                var textEl = card.querySelector('.cardText, .text');
                var name = textEl ? (textEl.textContent || '').trim() : '';
                var label = byName[name];
                if (!label) continue;

                var badge = document.createElement('span');
                badge.className = 'strm-media-badge';
                badge.textContent = label;
                badge.style.cssText = 'position:absolute;right:4px;bottom:4px;padding:1px 4px;' +
                    'font-size:11px;line-height:14px;border-radius:3px;background:rgba(0,0,0,.65);color:#fff;z-index:2;';
                card.appendChild(badge);
            }
        });
    }

    function mediaLabel(src) {
        var streams = src.MediaStreams || [];
        var video = null;
        for (var i = 0; i < streams.length; i++) {
            if (streams[i].Type === 'Video') { video = streams[i]; break; }
        }
        if (!video || !video.Height) return '';
        if (video.Height >= 2000) return '2160p';
        if (video.Height >= 1000) return '1080p';
        if (video.Height >= 700) return '720p';
        return video.Height + 'p';
    }

    function applyAll() {
        injectStyle();
        applyHidePerson();
        applyItemOrder();
        applyLibraryCount();
        applyReplaceUnplayed();
        applyReverseLogLines();
        applyBeautifyMissingMetadata();
        applyShowCollectionsFromSeasons();
        applyShowMediaInfo();
        applySuppressSearchSuggestions();
        api.stats.runs++;
    }

    function loadConfig() {
        var xhr = new XMLHttpRequest();
        xhr.open('GET', '/emby/StrmAssistant/UIScript/Config', true);
        xhr.onload = function () {
            if (xhr.status !== 200) return;
            try { api.config = JSON.parse(xhr.responseText); } catch (e) { return; }
            applyAll();
        };
        xhr.send();
    }

    api.apply = applyAll;
    injectStyle();
    loadConfig();

    // 合并短时间内的多次 DOM 变化：以前每一次变化都同步跑一遍全部功能（还会发请求）
    var applyTimer = null;
    var observer = new MutationObserver(function () {
        if (applyTimer) return;
        applyTimer = setTimeout(function () { applyTimer = null; applyAll(); }, 250);
    });
    document.addEventListener('DOMContentLoaded', function () {
        observer.observe(document.body, { childList: true, subtree: true });
        applyAll();
        console.log('[StrmAssistant] ready', api.version);
    });
})();
";

        /// <summary>脚本内容的指纹：注入到 index.html 的地址带上它，脚本一改浏览器就会重新取。</summary>
        internal static readonly string ScriptVersion = ComputeVersion();

        private static string ComputeVersion()
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Js));
                return System.BitConverter.ToString(hash, 0, 4).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private readonly IHttpResultFactory _resultFactory;

        public IRequest Request { get; set; }

        public ScriptInjectService(IHttpResultFactory resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public object Get(ScriptInjectApi request)
        {
            var bytes = Encoding.UTF8.GetBytes(Js);
            return _resultFactory.GetResult(Request, bytes, "application/javascript");
        }
    }
}
