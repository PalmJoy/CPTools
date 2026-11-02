import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import vm from 'node:vm';
const source=readFileSync(new URL('../../docs/shared/analytics.js',import.meta.url),'utf8').replace(/^export /gm,'').replace('startAnalytics().catch(() => {});','');
function setup(product='CPClipboard',lang='ja',options={}) {
  let click;
  const events=[],configs=[];
  const document={documentElement:{dataset:{product},lang},title:product,addEventListener(name,handler){assert.equal(name,'click');click=handler;}};
  const location={pathname:`/CPFileLauncher/${product}/${lang==='ja'?'ja/':''}`,href:`https://palmjoy.github.io/CPFileLauncher/${product}/${lang==='ja'?'ja/':''}`};
  const sdk={initializeApp:()=>({}),isSupported:async()=>true,setDefaultEventParameters(params){configs.push(params);},initializeAnalytics(_,options){configs.push(options);return {};},logEvent(_,name,params){events.push({name,params});},...options.sdk};
  const context=vm.createContext({URL});vm.runInContext(source,context);
  const completed=context.startAnalytics({document,location,loadSDK:options.loadSDK??(async()=>sdk)});
  function press(href,placement='hero'){
    const link={href,closest:()=>placement==='hero'?null:{dataset:{asset:'wrong-value'}}};
    const event={target:{closest:()=>link},preventDefault(){throw Error('Analytics must not prevent downloads');}};
    click(event);
  }
  return {events,configs,completed,press,sdk};
}
test('one explicit page_view identifies each product and language, with automatic page_view disabled',async()=>{
  for(const product of ['CPFileLauncher','CPClipboard','CPSystemMonitor'])for(const lang of ['en','ja']){
    const r=setup(product,lang);await r.completed;
    assert.equal(r.events.length,1);assert.equal(r.events[0].name,'page_view');
    assert.equal(r.events[0].params.content_group,product);assert.equal(r.events[0].params.product,product);
    assert.equal(r.events[0].params.language,lang);assert.equal(r.events[0].params.page_language,lang);assert.ok(r.events[0].params.page_path.includes('/'+product+'/'));
    assert.equal(r.configs[1].config.send_page_view,false);
  }
});
test('hero and card downloads record app, actual ZIP platform, language and placement',async()=>{
  for(const product of ['CPFileLauncher','CPClipboard','CPSystemMonitor']){
    const r=setup(product);await r.completed;
    for(const placement of ['hero','platform_card'])for(const rid of ['win-x64','win-arm64','macos-arm64']){
      r.press(`https://github.com/PalmJoy/CPFileLauncher/releases/download/tag/${product}-${rid}.zip`,placement);
      const event=r.events.at(-1);
      assert.equal(event.name,'download_click');assert.equal(event.params.content_group,product);
      assert.equal(event.params.platform,rid);assert.equal(event.params.placement,placement);assert.equal(event.params.language,'ja');assert.equal(event.params.page_language,'ja');
      assert.equal(event.params.file_name,`${product}-${rid}.zip`);
    }
    assert.equal(r.events.length,7);
  }
});
test('SDK loading preserves early clicks and reads the link resolved at click time',async()=>{
  let finish;const r=setup('CPClipboard','en',{loadSDK:()=>new Promise(resolve=>finish=resolve)});
  r.press('https://github.com/PalmJoy/CPFileLauncher/releases/download/cpclipboard-v1.0.1/CPClipboard-macos-arm64.zip');
  assert.equal(r.events.length,0);finish(r.sdk);await r.completed;
  assert.deepEqual(r.events.map(e=>e.name),['page_view','download_click']);
  assert.equal(r.events[1].params.platform,'macos-arm64');
});
test('in-page links, other products and foreign hosts do not count as downloads',async()=>{
  const r=setup();await r.completed;
  for(const href of ['https://palmjoy.github.io/CPFileLauncher/CPClipboard/#download','https://example.com/CPClipboard-win-x64.zip','https://github.com/PalmJoy/CPFileLauncher/releases/download/v1.2.0/CPFileLauncher-win-x64.zip'])r.press(href);
  assert.equal(r.events.length,1);
});
test('unsupported or unavailable analytics does not affect the click handler',async()=>{
  for(const options of [{sdk:{isSupported:async()=>false}},{loadSDK:async()=>{throw Error('blocked');}},{sdk:{logEvent(){throw Error('failed');}}}]){
    const r=setup('CPClipboard','ja',options);await r.completed;
    assert.doesNotThrow(()=>r.press('https://github.com/PalmJoy/CPFileLauncher/releases/download/cpclipboard-v1.0.1/CPClipboard-win-x64.zip'));
  }
});
test('all localized pages use one shared tracker, including the app catalog',()=>{
  for(const product of ['CPClipboard','CPFileLauncher','CPSystemMonitor'])for(const lang of ['','ja/']){
    const html=readFileSync(new URL(`../../docs/${product}/${lang}index.html`,import.meta.url),'utf8');
    assert.equal((html.match(/src="[^"]*analytics.js(?:\?v=\d+)?"/g)||[]).length,1);
    assert.ok(html.includes(`data-product="${product}"`));assert.ok(html.includes('shared/analytics.js'));
  }
  for(const path of ['index.html','ja/index.html']){const html=readFileSync(new URL('../../docs/'+path,import.meta.url),'utf8');assert.equal((html.match(/src="[^"]*analytics.js(?:\?v=\d+)?"/g)||[]).length,1);assert.ok(html.includes('data-product="CPTools"'));}
});

test('suite pages have page views without reporting app downloads',async()=>{
  const r=setup('CPTools');await r.completed;
  assert.equal(r.events.length,1);assert.equal(r.events[0].params.product,'CPTools');
  r.press('https://github.com/PalmJoy/CPFileLauncher/releases/download/cpsystemmonitor-v0.1.0/CPSystemMonitor-win-x64.zip');
  assert.equal(r.events.length,1);
});
