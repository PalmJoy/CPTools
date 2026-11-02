import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import vm from 'node:vm';
import {selectRelease} from '../Web/release-selection.js';
const code=readFileSync(new URL('../../docs/CPFileLauncher/assets/site.js',import.meta.url),'utf8').replace(/^import .*;\n/,'');
test('Launcher download and hero links select Launcher assets in a mixed repository',async()=>{
  const link={href:'original'},hero={href:'original'},version={textContent:''};
  const item={classList:{toggle(){}},getAttribute(){return 'win-x64';}};
  const document={documentElement:{lang:'en',style:{setProperty(){}}},querySelector(s){return s==='[data-hero-download]'?hero:null;},querySelectorAll(s){if(s==='.dl-item')return[item];if(s==='[data-version]')return[version];if(s==='.dl-item[data-asset="win-x64"] a[href]')return[link];return[];},addEventListener(){},getElementById(){return null;}};
  const asset='https://github.com/PalmJoy/CPTools/releases/download/v1.10.0/CPFileLauncher-win-x64.zip';
  const releases=[{draft:false,prerelease:false,tag_name:'cpclipboard-v99.0.0',assets:[]},{draft:false,prerelease:false,tag_name:'v1.10.0',assets:[{name:'CPFileLauncher-win-x64.zip',browser_download_url:asset}]}];
  let requested;
  const fetch=async url=>{requested=url;return{ok:true,json:async()=>releases};};
  vm.runInNewContext(code,{document,window:{fetch},navigator:{platform:'Win32',userAgent:''},fetch,selectRelease,URL,setTimeout(){},Date,Promise});
  await new Promise(resolve=>setImmediate(resolve));
  assert.match(requested,/releases\?per_page=100$/);assert.equal(link.href,asset);assert.equal(hero.href,asset);assert.equal(version.textContent,'v1.10.0');
});
