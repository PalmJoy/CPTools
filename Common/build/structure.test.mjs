import test from 'node:test';
import {runInNewContext} from 'node:vm';
import assert from 'node:assert/strict';
import {readFileSync,existsSync,readdirSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {join,dirname} from 'node:path';
import {selectRelease} from '../Web/release-selection.js';
const root=fileURLToPath(new URL('../../',import.meta.url));
test('each shared source is compiled from one physical file',()=>{const entries=JSON.parse(readFileSync(join(root,'Common/shared-sources.json'),'utf8'));assert.ok(entries.length>40);for(const e of entries){assert.ok(existsSync(join(root,e.source)),e.source);assert.ok(!existsSync(join(root,e.clipboard)),e.clipboard);assert.ok(!existsSync(join(root,e.launcher)),e.launcher);}});
test('product release streams do not cross and stable versions sort numerically',()=>{const rel=(tag,more={})=>({tag_name:tag,draft:false,prerelease:false,...more});const list=[rel('v1.9.0'),rel('cpclipboard-v9.0.0'),rel('v1.10.0'),rel('v99.0.0',{draft:true}),rel('v98.0.0',{prerelease:true}),rel('v97.0.0-beta')];assert.equal(selectRelease(list,'v').tag_name,'v1.10.0');assert.equal(selectRelease(list,'cpclipboard-v').tag_name,'cpclipboard-v9.0.0');assert.equal(selectRelease([], 'v'),null);assert.equal(selectRelease({}, 'v'),null);});
test('Launcher release URLs retain their established tag scheme and Clipboard is never marked latest',()=>{const a=readFileSync(join(root,'.github/workflows/release.yml'),'utf8'),b=readFileSync(join(root,'.github/workflows/release-clipboard.yml'),'utf8');assert.ok(a.includes("tags: ['v[0-9]+.[0-9]+.[0-9]+']"));assert.ok(b.includes('cpclipboard-v'));assert.ok(b.includes('--latest=false'));assert.ok(existsSync(join(root,'docs/index.html')));assert.ok(existsSync(join(root,'docs/ja/index.html')));});

test('moved and legacy site entries keep all local links resolvable',()=>{function walk(dir){for(const e of readdirSync(dir,{withFileTypes:true})){const p=join(dir,e.name);if(e.isDirectory())walk(p);else if(e.name.endsWith('.html')){for(const m of readFileSync(p,'utf8').matchAll(/(?:href|src)=["']([^"']+)/g)){const raw=m[1].split('#')[0].split('?')[0];if(!raw||/^[a-z]+:/i.test(raw)||raw.startsWith('/'))continue;assert.ok(existsSync(join(dirname(p),raw)),p+' -> '+raw);}}}}walk(join(root,'docs'));});

test('root entries are real localized app catalogs with no redirect',()=>{
  for(const lang of ['','ja/']){
    const html=readFileSync(join(root,'docs',lang,'index.html'),'utf8');
    assert.ok(html.includes('data-product="CPTools"'));
    assert.ok(!html.includes('http-equiv="refresh"'));
    assert.ok(!html.includes('location.replace'));
    for(const product of ['CPFileLauncher','CPClipboard','CPSystemMonitor'])assert.ok(html.includes(`href="${lang?'../':''}${product}/${lang}"`));
  }
  const ja=readFileSync(join(root,'docs/ja/index.html'),'utf8');
  assert.ok(ja.includes('サイバーパンクなデスクトップツール'));
  assert.ok(!ja.includes('デスクトップに、ちょうどいい道具を。'));
});
