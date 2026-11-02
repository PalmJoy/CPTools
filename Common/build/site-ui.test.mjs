import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import vm from 'node:vm';
const code=readFileSync(new URL('../../docs/shared/site-ui.js',import.meta.url),'utf8');
test('shared palette changes both colors and keeps a single selected preset',()=>{
  const values={};
  const buttons=[['#00E5FF','#FF2BD6'],['#FF2E4D','#37EBF3']].map(colors=>({
    addEventListener(_,callback){this.click=callback;},
    getAttribute(key){return colors[key==='data-c1'?0:1];},
    setAttribute(key,value){this[key]=value;}
  }));
  const document={documentElement:{style:{setProperty(key,value){values[key]=value;}}},querySelectorAll:()=>buttons,querySelector:()=>null};
  vm.runInNewContext(code,{document});buttons[1].click();
  assert.equal(values['--c1'],'#FF2E4D');assert.equal(values['--c2'],'#37EBF3');
  assert.equal(buttons[0]['aria-pressed'],'false');assert.equal(buttons[1]['aria-pressed'],'true');
});
test('shared clock updates and optional controls may be absent',()=>{
  const clock={};let delay;
  vm.runInNewContext(code,{document:{querySelectorAll:()=>[],querySelector:()=>clock},Date,setTimeout(_,value){delay=value;}});
  assert.match(clock.textContent,/^\d{4}\/\d{2}\/\d{2} \d{2}:\d{2}$/);assert.ok(delay>0&&delay<=60020);
});
test('both product languages load shared styling and controls with unique section IDs',()=>{
  for(const product of ['CPClipboard','CPFileLauncher']){
    assert.match(readFileSync(new URL(`../../docs/${product}/assets/site.css`,import.meta.url),'utf8'),/@import url\("\.\.\/\.\.\/shared\/site.css"\)/);
    for(const lang of ['','ja/']){
      const html=readFileSync(new URL(`../../docs/${product}/${lang}index.html`,import.meta.url),'utf8');
      assert.match(html,/shared\/site-ui.js/);assert.match(html,/class="swatches"/);assert.match(html,/class="beam"/);
      const ids=[...html.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]);assert.equal(ids.length,new Set(ids).size,product+lang);
    }
  }
});
test('all products link to the localized suite and have three square app links',()=>{
  for(const product of ['CPClipboard','CPFileLauncher','CPSystemMonitor'])for(const lang of ['','ja/']){
    const html=readFileSync(new URL(`../../docs/${product}/${lang}index.html`,import.meta.url),'utf8');
    const up=lang?'../../':'../';
    const header=html.match(/<header[\s\S]*?<\/header>/)[0];
    assert.ok(header.includes(`href="${up}${lang}"`),product+lang);
    assert.ok(header.includes('shared/palm.svg'));
    const section=html.match(/<!-- CPTOOLS-LINKS:START -->([\s\S]*?)<!-- CPTOOLS-LINKS:END -->/)[1];
    assert.equal((section.match(/class="suite-cell"/g)||[]).length,3);
    assert.equal((section.match(/aria-current="page"/g)||[]).length,1);
    for(const target of ['CPFileLauncher','CPClipboard','CPSystemMonitor'])assert.ok(section.includes(`href="${up}${target}/${lang}"`));
    const ids=[...html.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]);assert.equal(ids.length,new Set(ids).size,product+lang);
  }
});
