import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {assetNames,publishedMonitorRelease,detectPlatform,startMonitorPage} from '../../docs/CPSystemMonitor/assets/site.js';
const repo='https://github.com/PalmJoy/CPTools';
function release(version='1.2.0',options={}){
  const tag='cpsystemmonitor-v'+version;
  return {tag_name:tag,draft:false,prerelease:false,assets:Object.values(assetNames).map(name=>({name,size:1048576,browser_download_url:`${repo}/releases/download/${tag}/${name}`})),...options};
}
function setup({releases=[],failure=false,published=false,navigator={platform:'Win32',userAgent:''},lang='ja'}={}){
  const cards=Object.keys(assetNames).map(platform=>{
    const label={textContent:'pending'},size={textContent:''};
    const link={href:published?`${repo}/releases/download/cpsystemmonitor-v0.1.0/${assetNames[platform]}`:repo+'/tree/main/CPSystemMonitor',classList:{remove(){}},querySelector(){return label;}};
    return {platform,link,label,size,recommended:false,getAttribute(){return platform;},querySelector(selector){return selector==='[data-download-link]'?link:size;},classList:{toggle(_,value){cards.find(card=>card.platform===platform).recommended=value;}}};
  });
  const preview={src:'assets/standard.png',alt:'Standard'};
  const buttons=['standard','compact'].map(name=>({selected:name==='standard',getAttribute(key){return key==='data-preview-src'?`assets/${name}.png`:name;},addEventListener(_,fn){this.click=fn;},setAttribute(key,value){if(key==='aria-pressed')this.selected=value==='true';}}));
  const nodes={'[data-hero-download]':{href:'#download'},'[data-hero-label]':{textContent:'availability'},'[data-hero-other]':{hidden:true},'[data-release-status]':{textContent:'pending'},'[data-release-link]':{href:repo+'/releases'}};
  if(published)nodes['[data-downloads]']={getAttribute(){return '0.1.0';}};
  const radio={};
  const document={documentElement:{lang},querySelectorAll(selector){return selector==='.dl-item[data-asset]'?cards:buttons;},querySelector(selector){return nodes[selector]||null;},getElementById(id){return id==='monitor-preview'?preview:radio;}};
  const fetch=async()=>{if(failure)throw Error('offline');return{ok:true,json:async()=>releases};};
  return {cards,nodes,preview,buttons,completed:startMonitorPage({document,navigator,fetch})};
}
test('monitor downloads are isolated from other streams and require all trusted assets',()=>{
  const good=release(),partial=release('2.0.0');partial.assets.pop();
  const wrong=release('99.0.0',{tag_name:'cpclipboard-v99.0.0'});
  const bad=release('9.0.0');bad.assets[0].browser_download_url='https://example.com/CPSystemMonitor-win-x64.zip';
  assert.equal(publishedMonitorRelease([wrong,partial,bad,release('8.0.0',{draft:true}),release('7.0.0',{prerelease:true}),good]),good);
  for(const list of [null,{},[],[partial],[bad]])assert.equal(publishedMonitorRelease(list),null);
});
test('offline or unpublished monitor keeps an honest non-download state',async()=>{
  for(const options of [{releases:[]},{failure:true}]){
    const r=setup(options);await r.completed;
    assert.equal(r.nodes['[data-hero-download]'].href,'#download');
    if(options.failure)assert.match(r.nodes['[data-release-status]'].textContent,/取得できません/);
    else assert.equal(r.nodes['[data-release-status]'].textContent,'pending');
    for(const card of r.cards)assert.equal(card.link.href,options.failure?repo+'/releases':repo+'/tree/main/CPSystemMonitor');
  }
});
test('complete releases enable real platform links, size and the detected hero download',async()=>{
  const r=setup({releases:[release()]});await r.completed;
  for(const card of r.cards){assert.equal(card.link.href,`${repo}/releases/download/cpsystemmonitor-v1.2.0/${assetNames[card.platform]}`);assert.equal(card.size.textContent,' · 1.0 MB');}
  assert.equal(r.nodes['[data-hero-download]'].href,r.cards[0].link.href);
  assert.match(r.nodes['[data-release-status]'].textContent,/v1\.2\.0/);
});
test('published ZIP links and OS-specific hero survive release API failure or an empty result',async()=>{
  for(const failure of [false,true]){
    const r=setup({published:true,failure});await r.completed;
    for(const card of r.cards)assert.equal(card.link.href,`${repo}/releases/download/cpsystemmonitor-v0.1.0/${assetNames[card.platform]}`);
    assert.equal(r.nodes['[data-hero-download]'].href,r.cards[0].link.href);
    if(failure)assert.match(r.nodes['[data-release-status]'].textContent,/公開済み v0\.1\.0/);
  }
});
test('an untrusted duplicate asset cannot replace the trusted download URL',async()=>{
  const data=release();data.assets.unshift({...data.assets[0],browser_download_url:'https://example.com/CPSystemMonitor-win-x64.zip'});
  const r=setup({releases:[data]});await r.completed;
  assert.ok(r.cards[0].link.href.startsWith(repo+'/releases/download/'));
});
test('platform detection distinguishes Windows ARM and avoids claiming Intel Mac support',async()=>{
  assert.equal(await detectPlatform({platform:'Win32',userAgent:'',userAgentData:{platform:'Windows',getHighEntropyValues:async()=>({architecture:'arm'})}}),'win-arm64');
  assert.equal(await detectPlatform({platform:'MacIntel',userAgent:'iPad',maxTouchPoints:5}),null);
  const r=setup({releases:[release()],navigator:{platform:'MacIntel',userAgent:'Macintosh'},lang:'en'});await r.completed;
  assert.match(r.nodes['[data-hero-label]'].textContent,/Apple silicon only/);
});
test('preview controls switch the image, accessible text and pressed state',async()=>{
  const r=setup();await r.completed;r.buttons[1].click();
  assert.equal(r.preview.src,'assets/compact.png');assert.equal(r.preview.alt,'compact');
  assert.deepEqual(r.buttons.map(button=>button.selected),[false,true]);
});
test('monitor localized pages keep fallback links useful without JavaScript',()=>{
  for(const lang of ['','ja/']){
    const html=readFileSync(new URL(`../../docs/CPSystemMonitor/${lang}index.html`,import.meta.url),'utf8');
    assert.equal((html.match(/data-download-link/g)||[]).length,3);
    assert.ok(!html.includes('/releases/latest/download/'));
    for(const name of Object.values(assetNames))assert.ok(html.includes(`/releases/download/cpsystemmonitor-v0.1.0/${name}`));
    assert.ok(!html.includes('is-pending'));
    assert.ok(html.includes('data-release-version="0.1.0"'));
    assert.ok(html.includes('data-release-status'));
  }
});
