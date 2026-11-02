import {selectRelease} from './release-selection.js';
// 公開済みZIPはHTMLだけで取得可能。APIは最新版への更新にだけ使う。
(async function () {
  'use strict';
  const ja = document.documentElement.lang === 'ja';
  const assets = {'win-x64':'CPClipboard-win-x64.zip','win-arm64':'CPClipboard-win-arm64.zip','macos-arm64':'CPClipboard-macos-arm64.zip'};
  const cards = [...document.querySelectorAll('.dl-item[data-asset]')];
  const urls = {};
  for (const card of cards) urls[assets[card.getAttribute('data-asset')]] = card.querySelector('a[href]').href;
  let detectedPlatform = null;
  function updatePlatform() {
    const platform = detectedPlatform;
    if (!platform) return;
    for (const card of cards) card.classList.toggle('is-recommended', card.getAttribute('data-asset') === platform);
    const target = document.querySelector('[data-console-target]');
    if (target) target.textContent = platform;
    const radio = document.getElementById(platform === 'macos-arm64' ? 'os-mac' : 'os-win');
    if (radio) radio.checked = true;
    const hero = document.querySelector('[data-hero-download]');
    if (hero && urls[assets[platform]]) {
      hero.href = urls[assets[platform]];
      const labels = ja
        ? {'win-x64':'Windows版をダウンロード', 'win-arm64':'Windows（ARM64）版をダウンロード', 'macos-arm64':'macOS版をダウンロード（Apple silicon専用）'}
        : {'win-x64':'Download for Windows', 'win-arm64':'Download for Windows on ARM', 'macos-arm64':'Download for macOS (Apple silicon only)'};
      const label = document.querySelector('[data-hero-label]');
      if (label) label.textContent = labels[platform];
      const other = document.querySelector('[data-hero-other]');
      if (other) other.hidden = false;
    }
  }
  async function detectPlatform() {
    const data = navigator.userAgentData;
    const platform = data?.platform || navigator.platform || '';
    const ua = navigator.userAgent || '';
    if (/iPhone|iPad|iPod|Android/i.test(ua)) return null;
    if (/Win/i.test(platform) || /Windows/i.test(ua)) {
      try {
        const values = await data?.getHighEntropyValues?.(['architecture']);
        return values?.architecture === 'arm' ? 'win-arm64' : 'win-x64';
      } catch (_) { return 'win-x64'; }
    }
    // MacのCPUは確実に判別できないため、ボタンにもApple silicon専用と表示する。
    if (/Mac/i.test(platform) || /Macintosh/i.test(ua)) return 'macos-arm64';
    return null;
  }
  // リリース一覧の通信完了を待たず、HTMLにある公開済みZIPへ直接つなぐ。
  const platformReady = detectPlatform().then(platform => { detectedPlatform = platform; updatePlatform(); });
  try {
    const response = await fetch('https://api.github.com/repos/PalmJoy/CPTools/releases?per_page=100', {cache:'no-store',headers:{Accept:'application/vnd.github+json'}});
    if (!response.ok) return;
    const release = selectRelease(await response.json(), 'cpclipboard-v');
    if (!release) return;
    let updated = 0;
    for (const card of cards) {
      const name = assets[card.getAttribute('data-asset')];
      const asset = (release.assets || []).find(asset => asset.name === name);
      if (!asset) continue;
      let url;
      try { url = new URL(asset.browser_download_url); } catch (_) { continue; }
      if (url.origin !== 'https://github.com' || url.pathname !== `/PalmJoy/CPTools/releases/download/${release.tag_name}/${name}`) continue;
      urls[name] = url.href;
      card.querySelector('a[href]').href = url.href;
      const size = card.querySelector('[data-size]');
      if (size && asset.size) size.textContent = ` · ${(asset.size / 1048576).toFixed(1)} MB`;
      updated++;
    }
    if (updated === Object.keys(assets).length) {
      const version = release.tag_name.replace('cpclipboard-', '');
      document.querySelector('[data-release-status]').textContent = (ja ? '公開版: ' : 'Latest release: ') + version;
      document.querySelectorAll('[data-version]').forEach(node => { node.textContent = version; });
      const link = document.querySelector('[data-release-link]');
      if (link) link.href = 'https://github.com/PalmJoy/CPTools/releases/tag/' + release.tag_name;
    }
    updatePlatform();
  } catch (_) { /* 通信失敗・API制限時もHTMLの公開済みリンクを維持する。 */ }
  finally { await platformReady; }
})();
