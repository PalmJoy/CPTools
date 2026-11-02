// 両製品のサイト計測。ダウンロードとページ操作を妨げない。
const firebaseConfig = {
  apiKey: 'AIzaSyAG1mJUN96IQztYl49KUneIskvXmhRvD5E',
  authDomain: 'cpfilelauncher.firebaseapp.com',
  projectId: 'cpfilelauncher',
  storageBucket: 'cpfilelauncher.firebasestorage.app',
  messagingSenderId: '398881184018',
  appId: '1:398881184018:web:c086cc7fb384e88155fbcb',
  measurementId: 'G-2VWQWZKZC4'
};

async function loadFirebase() {
  const [app, analytics] = await Promise.all([
    import('https://www.gstatic.com/firebasejs/13.0.0/firebase-app.js'),
    import('https://www.gstatic.com/firebasejs/13.0.0/firebase-analytics.js')
  ]);
  return { ...app, ...analytics };
}

export async function startAnalytics({document = globalThis.document, location = globalThis.location, loadSDK = loadFirebase} = {}) {
  const product = document.documentElement.dataset.product;
  if (!['CPTools', 'CPFileLauncher', 'CPClipboard', 'CPSystemMonitor'].includes(product)) return;
  const common = {
    product,
    content_group: product,
    language: document.documentElement.lang || 'en',
    page_language: document.documentElement.lang || 'en',
    page_path: location.pathname,
    page_location: location.href
  };
  let send;
  const pending = [['page_view', {...common, page_title: document.title}]];
  // SDKの読み込み待ちでも押下を受け付ける。リンクは常に通常どおり開く。
  document.addEventListener('click', event => {
    const link = event.target?.closest?.('.dl-item[data-asset] a.btn, [data-hero-download]');
    if (!link || product === 'CPTools') return;
    let url;
    try { url = new URL(link.href, location.href); } catch (_) { return; }
    if (url.origin !== 'https://github.com' || !url.pathname.startsWith('/PalmJoy/CPTools/releases/')) return;
    const file = url.pathname.split('/').pop();
    const match = file.match(new RegExp('^' + product + '-(win-x64|win-arm64|macos-arm64)\\.zip$'));
    if (!match) return; // 配布一覧へのページ内リンクや別製品のZIPは数えない。
    const card = link.closest('.dl-item');
    const params = {
      ...common,
      platform: match[1],
      placement: card ? 'platform_card' : 'hero',
      file_name: file,
      link_url: url.href
    };
    if (send) send('download_click', params);
    else if (pending.length < 64) pending.push(['download_click', params]);
  }, true);
  try {
    const sdk = await loadSDK();
    if (!await sdk.isSupported()) { pending.length = 0; return; }
    const app = sdk.initializeApp(firebaseConfig);
    // 自動の初回page_viewと手動送信を重複させず、製品情報を必ず含める。
    sdk.setDefaultEventParameters(common);
    const analytics = sdk.initializeAnalytics(app, {config: {send_page_view: false, content_group: product}});
    send = (name, params) => {
      try { sdk.logEvent(analytics, name, params); } catch (_) { /* 計測失敗でリンクを止めない。 */ }
    };
    for (const [name, params] of pending) send(name, params);
    pending.length = 0;
  } catch (_) { pending.length = 0; }
}

startAnalytics().catch(() => {});
