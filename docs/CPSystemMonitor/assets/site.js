import {selectRelease} from './release-selection.js';
const product='CPSystemMonitor';
const prefix='cpsystemmonitor-v';
const repository='https://github.com/PalmJoy/CPFileLauncher';
export const assetNames={
  'win-x64':product+'-win-x64.zip',
  'win-arm64':product+'-win-arm64.zip',
  'macos-arm64':product+'-macos-arm64.zip'
};
function trustedAsset(asset,tag,name){
  if(!asset||asset.name!==name)return false;
  try{
    const url=new URL(asset.browser_download_url);
    return url.origin==='https://github.com'&&url.pathname===`/PalmJoy/CPFileLauncher/releases/download/${tag}/${name}`&&!url.search&&!url.hash;
  }catch{return false;}
}
export function publishedMonitorRelease(releases){
  if(!Array.isArray(releases))return null;
  const complete=releases.filter(release=>release&&typeof release.tag_name==='string'&&Array.isArray(release.assets)
    &&Object.values(assetNames).every(name=>release.assets.some(asset=>trustedAsset(asset,release.tag_name,name))));
  return selectRelease(complete,prefix);
}
export async function detectPlatform(navigator){
  const data=navigator.userAgentData;
  const platform=data?.platform||navigator.platform||'';
  const agent=navigator.userAgent||'';
  if(/iPhone|iPad|iPod|Android/i.test(agent)||(navigator.maxTouchPoints>1&&/Mac/i.test(platform)))return null;
  if(/Win/i.test(platform)||/Windows/i.test(agent)){
    try{return (await data?.getHighEntropyValues?.(['architecture']))?.architecture==='arm'?'win-arm64':'win-x64';}
    catch{return 'win-x64';}
  }
  // The browser cannot reliably distinguish Intel Macs, so all Mac labels say Apple silicon only.
  return /Mac/i.test(platform)||/Macintosh/i.test(agent)?'macos-arm64':null;
}
export async function startMonitorPage({document=globalThis.document,navigator=globalThis.navigator,fetch=globalThis.fetch}={}){
  const ja=document.documentElement.lang==='ja';
  const cards=[...document.querySelectorAll('.dl-item[data-asset]')];
  const urls={};
  const platformReady=detectPlatform(navigator);
  function updatePlatform(platform){
    if(!platform)return;
    for(const card of cards)card.classList.toggle('is-recommended',card.getAttribute('data-asset')===platform);
    const radio=document.getElementById(platform==='macos-arm64'?'os-mac':'os-win');
    if(radio)radio.checked=true;
    const hero=document.querySelector('[data-hero-download]');
    if(hero&&urls[platform]){
      hero.href=urls[platform];
      const label=document.querySelector('[data-hero-label]');
      if(label)label.textContent=platform==='macos-arm64'
        ?(ja?'macOS版をダウンロード（Apple silicon専用）':'Download for macOS (Apple silicon only)')
        :platform==='win-arm64'?(ja?'Windows ARM64版をダウンロード':'Download for Windows ARM64')
        :(ja?'Windows版をダウンロード':'Download for Windows');
      const other=document.querySelector('[data-hero-other]');if(other)other.hidden=false;
    }
  }
  function showReleaseLookupFailure(){
    const status=document.querySelector('[data-release-status]');
    if(status)status.textContent=ja?'配布状況を取得できませんでした。GitHubのRelease一覧をご確認ください。':'Release availability could not be loaded. Please check the GitHub releases.';
    for(const card of cards){
      const link=card.querySelector('[data-download-link]');
      link.href=repository+'/releases';
      link.querySelector('.cut').textContent=ja?'Releaseを確認 ↗':'Browse releases ↗';
    }
  }
  platformReady.then(updatePlatform);
  const preview=document.getElementById('monitor-preview');
  document.querySelectorAll('[data-preview-src]').forEach(button=>button.addEventListener('click',()=>{
    if(!preview)return;
    preview.src=button.getAttribute('data-preview-src');
    preview.alt=button.getAttribute('data-preview-alt');
    document.querySelectorAll('[data-preview-src]').forEach(other=>other.setAttribute('aria-pressed',String(other===button)));
  }));
  try{
    const response=await fetch('https://api.github.com/repos/PalmJoy/CPFileLauncher/releases?per_page=100',{cache:'no-store',headers:{Accept:'application/vnd.github+json'}});
    if(!response.ok){showReleaseLookupFailure();return;}
    const release=publishedMonitorRelease(await response.json());
    if(!release)return;
    for(const card of cards){
      const platform=card.getAttribute('data-asset');
      const asset=release.assets.find(asset=>trustedAsset(asset,release.tag_name,assetNames[platform]));
      const link=card.querySelector('[data-download-link]');
      urls[platform]=asset.browser_download_url;
      link.href=asset.browser_download_url;link.classList.remove('is-pending');
      link.querySelector('.cut').textContent=ja?'↓ ダウンロード':'↓ Download';
      const size=card.querySelector('[data-size]');
      if(size&&Number.isFinite(asset.size)&&asset.size>0)size.textContent=` · ${(asset.size/1048576).toFixed(1)} MB`;
    }
    const status=document.querySelector('[data-release-status]');
    if(status)status.textContent=(ja?'公開版: ':'Latest release: ')+'v'+release.tag_name.slice(prefix.length);
    const link=document.querySelector('[data-release-link]');if(link)link.href=repository+'/releases/tag/'+release.tag_name;
    updatePlatform(await platformReady);
  }catch{showReleaseLookupFailure();}
  finally{await platformReady;}
}
if(typeof document!=='undefined')startMonitorPage().catch(()=>{});
