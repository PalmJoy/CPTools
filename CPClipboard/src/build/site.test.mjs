import {readFileSync} from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import assert from 'node:assert/strict';
import {selectRelease} from '../../../Common/Web/release-selection.js';
const code=readFileSync(new URL('../../../docs/CPClipboard/assets/site.js',import.meta.url),'utf8').replace(/^import .*;\n/, '');
const base='https://github.com/PalmJoy/CPTools/releases/download/';
const ids=['win-x64','win-arm64','macos-arm64'];
const published='cpclipboard-v1.0.5';
function start(response,navigator={},lang='en'){
  const html=readFileSync(new URL(`../../../docs/CPClipboard/${lang==='ja'?'ja/':''}index.html`,import.meta.url),'utf8');
  const cards=ids.map(rid=>{
    const name=`CPClipboard-${rid}.zip`;
    const link={href:base+published+'/'+name},size={textContent:''};
    assert.ok(html.includes(`href="${link.href}"`),'Published ZIP must be present without JavaScript');
    const card={rid,link,size,recommended:false,getAttribute:()=>rid,querySelector:s=>s==='a[href]'?link:size};
    card.classList={toggle(_,value){card.recommended=value;}};return card;
  });
  const status={textContent:'Latest release: v1.0.5'},hero={href:'#download'},label={textContent:'Download'},other={hidden:true};
  const elements={'[data-release-status]':status,'[data-hero-download]':hero,'[data-hero-label]':label,'[data-hero-other]':other};
  const document={documentElement:{lang},querySelector:s=>elements[s]??null,querySelectorAll:s=>s==='.dl-item[data-asset]'?cards:[],getElementById:()=>null};
  const fetch=typeof response==='function'?response:async()=>response;
  const completed=vm.runInNewContext(code,{document,navigator,URL,selectRelease,fetch});
  return {cards,status,hero,label,other,completed};
}
const responseFor=(tag='cpclipboard-v1.0.6',extra={})=>({ok:true,json:async()=>[
  {tag_name:'v99.0.0',draft:false,prerelease:false,assets:[]},
  {tag_name:tag,draft:false,prerelease:false,assets:ids.map(rid=>({name:`CPClipboard-${rid}.zip`,browser_download_url:base+tag+`/CPClipboard-${rid}.zip`})),...extra}
]});
test('API failures retain all published ZIPs and the OS-specific hero in both languages',async()=>{
  for(const lang of ['en','ja'])for(const response of [{ok:false,status:403},{ok:false,status:404},{ok:false,status:500},async()=>{throw Error('offline');}]){
    const r=start(response,{platform:'MacIntel'},lang);await r.completed;
    assert.equal(r.cards.length,3);assert.ok(r.hero.href.endsWith('/'+published+'/CPClipboard-macos-arm64.zip'));
    assert.match(r.label.textContent,/Apple silicon/);assert.equal(r.other.hidden,false);
    assert.ok(r.cards.every(card=>card.link.href.includes('/'+published+'/')));
  }
});
test('hero is ready before the release request completes',async()=>{
  const r=start(()=>new Promise(()=>{}),{platform:'MacIntel'});
  await new Promise(resolve=>setImmediate(resolve));
  assert.equal(r.hero.href,base+published+'/CPClipboard-macos-arm64.zip');
});
test('Windows x64 and ARM use direct ZIP links even when the API fails',async()=>{
  for(const [navigator,rid] of [[{platform:'Win32'},'win-x64'],[{userAgentData:{platform:'Windows',getHighEntropyValues:async()=>({architecture:'arm'})}},'win-arm64'],[{platform:'Win32',userAgentData:{getHighEntropyValues:async()=>{throw Error('denied');}}},'win-x64']]){
    const r=start({ok:false,status:403},navigator);await r.completed;
    assert.equal(r.hero.href,base+published+`/CPClipboard-${rid}.zip`);
    assert.equal(r.cards.find(c=>c.recommended)?.rid,rid);
  }
});
test('complete valid Clipboard releases update links and version without crossing product streams',async()=>{
  const r=start(responseFor(),{platform:'MacIntel'});await r.completed;
  assert.ok(r.cards.every(card=>card.link.href.includes('/cpclipboard-v1.0.6/')));
  assert.ok(r.hero.href.includes('/cpclipboard-v1.0.6/'));assert.equal(r.status.textContent,'Latest release: v1.0.6');
});
test('draft, prerelease and untrusted assets cannot replace published fallback links',async()=>{
  for(const extra of [{draft:true},{prerelease:true},{assets:[{name:'CPClipboard-macos-arm64.zip',browser_download_url:'https://example.com/untrusted.zip'}]}]){
    const r=start(responseFor('cpclipboard-v1.0.6',extra),{platform:'MacIntel'});await r.completed;
    assert.ok(r.cards.every(card=>card.link.href.includes('/'+published+'/')));
  }
});
test('unknown platforms and mobile devices keep the visible platform chooser',async()=>{
  for(const navigator of [{platform:'Linux'},{platform:'MacIntel',userAgent:'iPad'}]){
    const r=start({ok:false,status:403},navigator);await r.completed;
    assert.equal(r.hero.href,'#download');assert.equal(r.cards.length,3);
  }
});
