import {selectRelease} from './release-selection.js';
// CPFileLauncher 配布サイトの JS（SPEC §10.4「配布サイト」、issue/RELEASE_SITE.md D）。
// JS が無くてもダウンロード・手順は読める。ここでやるのは: おすすめの表示・最新版の表示・言語の記録・配色の見本・模型の時計・コピー。
(function () {
  'use strict';

  var REPO = 'PalmJoy/CPTools';
  var ASSETS = {
    'win-x64': 'CPFileLauncher-win-x64.zip',
    'win-arm64': 'CPFileLauncher-win-arm64.zip',
    'macos-arm64': 'CPFileLauncher-macos-arm64.zip'
  };
  var lang = document.documentElement.lang || 'en';
  var T = lang === 'ja'
    ? { for: { 'win-x64': 'Windows 版をダウンロード', 'win-arm64': 'Windows（ARM）版をダウンロード', 'macos-arm64': 'macOS 版をダウンロード' }, copied: 'コピーしました' }
    : { for: { 'win-x64': 'Download for Windows', 'win-arm64': 'Download for Windows on ARM', 'macos-arm64': 'Download for macOS' }, copied: 'Copied' };

  function $(sel) { return document.querySelector(sel); }
  function $$(sel) { return Array.prototype.slice.call(document.querySelectorAll(sel)); }
  function store(kind, key, value) {
    try { return value === undefined ? window[kind].getItem(key) : window[kind].setItem(key, value); } catch (e) { return null; }
  }
  var releaseUrls = {};
  var detectedOs = null;
  function assetUrl(name) { return releaseUrls[name] || 'https://github.com/' + REPO + '/releases/latest/download/' + name; }

  // ---------- 言語の案内（URL ごとの言語を保ち、自動転送しない） ----------
  var languageSuggestion = $('[data-language-suggestion]');
  var preferredLanguage = (navigator.languages && navigator.languages[0]) || navigator.language || '';
  if (languageSuggestion && !store('localStorage', 'cpfl.lang') && /^ja\b/i.test(preferredLanguage)) {
    languageSuggestion.hidden = false;
  }
  $$('[data-lang]').forEach(function (a) {
    a.addEventListener('click', function () { store('localStorage', 'cpfl.lang', a.getAttribute('data-lang')); });
  });

  // ---------- OS の推定 → おすすめ ----------
  function detectOs() {
    var uad = navigator.userAgentData;
    var platform = (uad && uad.platform) || navigator.platform || '';
    var ua = navigator.userAgent || '';
    if (/iPhone|iPad|iPod|Android/i.test(ua)) return Promise.resolve(null);
    if (/Win/i.test(platform) || /Windows/i.test(ua)) {
      if (uad && uad.getHighEntropyValues) {
        return uad.getHighEntropyValues(['architecture'])
          .then(function (v) { return v.architecture === 'arm' ? 'win-arm64' : 'win-x64'; })
          .catch(function () { return 'win-x64'; });
      }
      return Promise.resolve('win-x64');
    }
    // Mac はブラウザから Intel / Apple シリコンを区別できない（UA は常に Intel）。ボタンには常に「Apple シリコン専用」を添えてある
    if (/Mac/i.test(platform) || /Macintosh/i.test(ua)) return Promise.resolve('macos-arm64');
    return Promise.resolve(null);
  }

  detectOs().then(function (os) {
    if (!os) return;
    detectedOs = os;
    $$('.dl-item').forEach(function (el) { el.classList.toggle('is-recommended', el.getAttribute('data-asset') === os); });
    var hero = $('[data-hero-download]');
    if (hero) {
      hero.href = assetUrl(ASSETS[os]);
      var label = $('[data-hero-label]');
      if (label) label.textContent = T.for[os];
    }
    var other = $('[data-hero-other]');
    if (other) other.hidden = false;
    var target = $('[data-console-target]');
    if (target) target.textContent = os;
    var radio = document.getElementById(os === 'macos-arm64' ? 'os-mac' : 'os-win');
    if (radio) radio.checked = true;
  });

  // ---------- 最新版（毎回再検証。取得失敗時は HTML のバージョン表示を保つ） ----------
  function showLatest(info) {
    if (!info || !info.tag) return;
    $$('[data-version]').forEach(function (el) { el.textContent = info.tag; el.hidden = false; });
    if (info.published) {
      var d = new Date(info.published);
      $$('[data-date]').forEach(function (el) {
        el.textContent = d.toLocaleDateString(lang === 'ja' ? 'ja-JP' : 'en-US', { year: 'numeric', month: 'short', day: 'numeric' });
      });
      $$('[data-date-wrap]').forEach(function (el) { el.hidden = false; });
    }
    $$('[data-size]').forEach(function (el) {
      var bytes = info.sizes && info.sizes[el.getAttribute('data-size')];
      if (!bytes) return;
      el.textContent = '· ' + (bytes / 1048576).toFixed(1) + ' MB';
      el.hidden = false;
    });
  }

  if (window.fetch) {
    fetch('https://api.github.com/repos/' + REPO + '/releases?per_page=100', {
      headers: { Accept: 'application/vnd.github+json' }, cache: 'no-store'
    })
      .then(function (r) { if (!r.ok) throw new Error(String(r.status)); return r.json(); })
      .then(function (releases) {
        var rel = selectRelease(releases, 'v');
        if (!rel) return;
        Object.keys(ASSETS).forEach(function (os) {
          var asset = (rel.assets || []).find(function (a) { return a.name === ASSETS[os]; });
          if (!asset) return;
          var url = new URL(asset.browser_download_url);
          if (url.origin !== 'https://github.com' || !url.pathname.startsWith('/PalmJoy/CPTools/releases/download/')) return;
          releaseUrls[asset.name] = url.href;
          $$('.dl-item[data-asset=\"' + os + '\"] a[href]').forEach(function (a) { a.href = url.href; });
        });
        var hero = $('[data-hero-download]');
        if (hero && detectedOs) hero.href = assetUrl(ASSETS[detectedOs]);
        var sizes = {};
        (rel.assets || []).forEach(function (a) { sizes[a.name] = a.size; });
        var info = { tag: rel.tag_name, published: rel.published_at, sizes: sizes };
        showLatest(info);
      })
      .catch(function () { /* オフライン・API 上限時は HTML の表示とダウンロードリンクを保つ */ });
  }

  // ---------- コピー ----------
  $$('[data-copy]').forEach(function (b) {
    if (!navigator.clipboard) return;
    b.hidden = false;
    var label = b.textContent;
    b.addEventListener('click', function () {
      navigator.clipboard.writeText(b.getAttribute('data-copy')).then(function () {
        b.textContent = T.copied;
        setTimeout(function () { b.textContent = label; }, 1500);
      });
    });
  });

})();
